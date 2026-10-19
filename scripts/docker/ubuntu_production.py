"""Initial, manual Docker cutover for homelab-server. No migrations or DB rollback.

All mutations require --apply. Run the ROOT-INSTALLED copy using python3 -I.
archive is the only command run as shunbin in the fixed incoming directory.
No command reads the old application's env file, prints credentials, sends mail,
deletes a database, overwrites an existing backup, or performs an automatic retry.
"""

import argparse
from contextlib import contextmanager
import getpass
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import secrets
import shutil
import socket
import stat
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request

INCOMING = Path('/home/shunbin/project1-ubuntu-production')
RETRY_INCOMING = Path('/home/shunbin/project1-ubuntu-production-retry')
ROOT = Path('/opt/project1-docker')
STATE_DIR = Path('/var/lib/project1-docker')
STATE_FILE = STATE_DIR / 'state.json'
CONFIG_DIR = Path('/etc/project1-docker')
ENV_FILE = CONFIG_DIR / 'project1.env'
INSTALLED_HELPER = Path('/usr/local/libexec/project1-docker.py')
LEGACY_STATE = Path('/var/lib/project1-deploy')
OLD_SITE = Path('/etc/nginx/sites-available/project1')
SITE_LINK = Path('/etc/nginx/sites-enabled/project1')
OLD_API = Path('/opt/project1/api/Project1.Api.dll')
OLD_INDEX = Path('/var/www/project1/index.html')
OLD_UNIT = Path('/etc/systemd/system/project1-api.service')
BACKUP_DIR = Path('/var/opt/mssql/backup/project1-docker-cutover')
SQLCMD = '/opt/mssql-tools18/bin/sqlcmd'
SERVICE = 'project1-api.service'
DATABASE = 'Project1Db'
LOGIN = 'project1_docker_production'
COMPOSE = 'compose.ubuntu.yml'
TAG_PATTERN = r'prod-[0-9]{8}t[0-9]{6}z-[0-9a-f]{8}'
PUBLIC_FILES = {COMPOSE, '.env.docker.ubuntu.example', 'ubuntu_production.py',
                'project1-docker.conf', 'project1-legacy.conf'}
ENV_KEYS = {'PROD_TAG', 'PROD_SQL_PASSWORD', 'PROD_JWT_KEY', 'PROD_NEW_USER_PASSWORD',
            'PROD_GMAIL_ADDRESS', 'PROD_GMAIL_APP_PASSWORD'}
FROZEN_PHASES = {'freezing', 'frozen', 'backup-ready', 'activating', 'activation-failed',
                 'active-pending-verification', 'active', 'rolling-back', 'rollback-failed'}


class Stop(RuntimeError):
    pass


def require(ok, message):
    if not ok:
        raise Stop(message)


def safe_environment():
    # No inherited SQLCMD*, Compose overrides, HTTP proxy, app secrets or Python path.
    return {'PATH': '/usr/sbin:/usr/bin:/sbin:/bin', 'HOME': '/root', 'LANG': 'C.UTF-8'}


def command(args, *, data=None, env=None, timeout=60):
    try:
        result = subprocess.run(args, input=data, text=True, capture_output=True,
                                env=env or safe_environment(), timeout=timeout, check=False)
    except (OSError, subprocess.TimeoutExpired):
        raise Stop('Command timed out/unavailable. Keep state and inspect; do not blindly retry.') from None
    require(result.returncode == 0, 'Command/check failed; raw output withheld to protect credentials.')
    return result.stdout.strip()


def docker(*args, timeout=60):
    return command(['/usr/bin/docker', *args], timeout=timeout)


def systemctl(*args):
    return command(['/usr/bin/systemctl', *args], timeout=60)


def regular(path, uid=0, private=False):
    info = path.lstat()
    require(stat.S_ISREG(info.st_mode) and info.st_nlink == 1 and info.st_uid == uid,
            'Unexpected file type/link/owner.')
    require(not info.st_mode & 0o022, 'Group/other-writable file is not accepted.')
    if private:
        require(stat.S_IMODE(info.st_mode) == 0o600, 'Private file must be mode 0600.')
    return info


def root_parents(path):
    for directory in reversed(path.parents):
        info = directory.lstat()
        require(stat.S_ISDIR(info.st_mode) and info.st_uid == 0 and not info.st_mode & 0o022,
                'Administrative path has a symlink or writable/non-root ancestor.')


def root_file(path, private=False):
    root_parents(path)
    return regular(path, private=private)


def directory(path):
    root_parents(path)
    if not os.path.lexists(path):
        path.mkdir(mode=0o700)
    info = path.lstat()
    require(stat.S_ISDIR(info.st_mode) and info.st_uid == 0 and stat.S_IMODE(info.st_mode) == 0o700,
            'Administrative directory must be ordinary, root-owned and mode 0700.')


def digest(path):
    value = hashlib.sha256()
    with path.open('rb') as file:
        for block in iter(lambda: file.read(1024 * 1024), b''):
            value.update(block)
    return value.hexdigest()


def write_new(path, data, mode=0o600):
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, mode)
    with os.fdopen(fd, 'wb') as file:
        file.write(data)
        file.flush()
        os.fsync(file.fileno())


def replace_file(path, data, mode=0o600):
    root_file(path)
    fd, temporary = tempfile.mkstemp(prefix='.project1-', dir=path.parent)
    try:
        os.fchmod(fd, mode)
        with os.fdopen(fd, 'wb') as file:
            file.write(data)
            file.flush()
            os.fsync(file.fileno())
        os.replace(temporary, path)
        parent = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY)
        try:
            os.fsync(parent)
        finally:
            os.close(parent)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def save_state(state):
    data = (json.dumps(state, sort_keys=True, indent=2) + '\n').encode()
    if os.path.lexists(STATE_FILE):
        root_file(STATE_FILE, private=True)
        replace_file(STATE_FILE, data)
    else:
        write_new(STATE_FILE, data)


def read_state():
    root_file(STATE_FILE, private=True)
    state = json.loads(STATE_FILE.read_text())
    require(isinstance(state, dict) and re.fullmatch(TAG_PATTERN, state.get('tag', '')),
            'Invalid cutover state.')
    return state


@contextmanager
def locks():
    import fcntl  # Linux only; previews/tests never require privileged Linux access.
    directory(STATE_DIR)
    root_parents(LEGACY_STATE / 'deploy.lock')
    require(LEGACY_STATE.is_dir() and stat.S_IMODE(LEGACY_STATE.stat().st_mode) == 0o700,
            'Expected private legacy deploy state directory.')
    require(not os.path.lexists(LEGACY_STATE / 'pending.json'), 'Legacy deployment is pending; resolve it first.')
    descriptors = []
    try:
        for path in (STATE_DIR / 'cutover.lock', LEGACY_STATE / 'deploy.lock'):
            fd = os.open(path, os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW, 0o600)
            descriptors.append(fd)
            regular(path, private=True)
            try:
                fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except BlockingIOError:
                raise Stop('Another Docker/legacy deployment holds the lock.') from None
        yield
    finally:
        for fd in reversed(descriptors):
            os.close(fd)


def check_host(admin=True, incoming=INCOMING):
    require(sys.platform == 'linux' and platform.machine() == 'x86_64'
            and socket.gethostname().lower() == 'homelab-server', 'Only the reviewed Ubuntu host is supported.')
    require('VERSION_ID="24.04"' in Path('/etc/os-release').read_text(), 'Expected Ubuntu 24.04.')
    if admin:
        require(os.geteuid() == 0 and Path(__file__).absolute() == INSTALLED_HELPER,
                'Run the root-installed helper with sudo /usr/bin/python3 -I, not the uploaded script.')
        root_file(INSTALLED_HELPER)
    else:
        import pwd
        require(os.geteuid() == pwd.getpwnam('shunbin').pw_uid and Path.cwd() == incoming
                and incoming.resolve() == incoming and stat.S_IMODE(incoming.stat().st_mode) == 0o700,
                'Archive check must run as shunbin in the ordinary private incoming directory.')
    os.umask(0o077)


def validate_manifest(manifest):
    require(isinstance(manifest, dict) and manifest.get('format') == 1
            and re.fullmatch(TAG_PATTERN, manifest.get('tag', '')), 'Invalid production release tag/format.')
    for key in ('archive_sha256', 'frontend_sha256'):
        require(re.fullmatch(r'[0-9a-f]{64}', manifest.get(key, '')), 'Invalid release checksum.')
    ids = manifest.get('migration_ids')
    require(isinstance(ids, list) and bool(ids)
            and all(isinstance(i, str) and re.fullmatch(r'[0-9]{14}_[A-Za-z0-9_]+', i) for i in ids)
            and ids == sorted(set(ids)), 'Invalid complete migration list.')
    images = manifest.get('images')
    require(isinstance(images, dict) and set(images) == {'api', 'frontend'}, 'Only two runtime images are allowed.')
    for name, metadata in images.items():
        require(isinstance(metadata, dict)
                and metadata.get('reference') == f'project1-ubuntu-{name}:{manifest["tag"]}'
                and re.fullmatch(r'sha256:[0-9a-f]{64}', metadata.get('id', ''))
                and re.fullmatch(r'[1-9][0-9]*', metadata.get('user', '')), 'Unexpected image reference/ID/user.')
    hashes = manifest.get('files_sha256')
    require(isinstance(hashes, dict) and set(hashes) == PUBLIC_FILES
            and all(isinstance(v, str) and re.fullmatch(r'[0-9a-f]{64}', v) for v in hashes.values()),
            'Missing/invalid release file hashes.')
    return manifest


def load_manifest(folder, uid=0):
    regular(folder / 'release.json', uid=uid)
    manifest = validate_manifest(json.loads((folder / 'release.json').read_text(encoding='utf-8')))
    for name, expected in manifest['files_sha256'].items():
        info = regular(folder / name, uid=uid)
        require(info.st_size < 256 * 1024 and digest(folder / name) == expected, 'Public release file changed.')
    return manifest


def archive_check(folder=INCOMING):
    import pwd
    uid = pwd.getpwnam('shunbin').pw_uid
    require(folder == INCOMING or (folder.parent == RETRY_INCOMING and re.fullmatch(TAG_PATTERN, folder.name)),
            'Unexpected incoming directory.')
    for path in ([folder] if folder == INCOMING else [RETRY_INCOMING, folder]):
        info = path.lstat()
        require(path.resolve() == path and stat.S_ISDIR(info.st_mode) and info.st_uid == uid
                and stat.S_IMODE(info.st_mode) == 0o700, 'Incoming directories must be ordinary, shunbin-owned, mode 0700.')
    manifest = load_manifest(folder, uid)
    if folder != INCOMING:
        require(folder.name == manifest['tag'], 'Retry directory/release tag mismatch.')
    regular(folder / 'images.tar', uid)
    require(digest(folder / 'images.tar') == manifest['archive_sha256'], 'Image archive checksum mismatch.')
    return manifest


def loaded_images(manifest):
    for metadata in manifest['images'].values():
        actual = docker('image', 'inspect', '--format', '{{.Id}}|{{.Os}}|{{.Architecture}}|{{.Config.User}}', metadata['reference'])
        require(actual == metadata['id'] + '|linux|amd64|' + metadata['user'], 'Loaded runtime image differs from release.')


def canonical_nginx(text):
    return re.sub(r'\s+', '', '\n'.join(line.split('#', 1)[0] for line in text.splitlines()))


def site_contract(test=True):
    root_file(OLD_SITE)
    root_parents(SITE_LINK)
    require(SITE_LINK.is_symlink() and os.readlink(SITE_LINK) == str(OLD_SITE), 'Project1 enabled-site target changed.')
    if test:
        command(['/usr/sbin/nginx', '-t'])


def service_properties():
    output = systemctl('show', SERVICE, '--no-pager',
                       '--property=ActiveState,SubState,MainPID,UnitFileState,FragmentPath,User,Group,WorkingDirectory,DropInPaths,NoNewPrivileges')
    return dict(line.split('=', 1) for line in output.splitlines() if '=' in line)


def old_running():
    props = service_properties()
    expected = {'ActiveState': 'active', 'SubState': 'running', 'UnitFileState': 'enabled',
                'FragmentPath': str(OLD_UNIT), 'User': 'project1', 'Group': 'project1',
                'WorkingDirectory': '/opt/project1/api', 'DropInPaths': '', 'NoNewPrivileges': 'yes'}
    require(all(props.get(k) == v for k, v in expected.items()) and int(props.get('MainPID', '0')) > 0,
            'Legacy service layout/state differs from the reviewed active/enabled service.')


def old_stopped():
    props = service_properties()
    require(props.get('ActiveState') == 'inactive' and props.get('MainPID') == '0'
            and props.get('UnitFileState') == 'disabled', 'Old API must be inactive, PID 0 and disabled.')


def old_files(state):
    for name, path in (('old_api_sha256', OLD_API), ('old_index_sha256', OLD_INDEX), ('old_unit_sha256', OLD_UNIT)):
        root_file(path)
        require(digest(path) == state[name], 'Retained old application/service files changed; review before continuing.')


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, message, headers, newurl):
        raise Stop('Unexpected HTTP redirect.')


def http_get(port, path):
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())
    request = urllib.request.Request(f'http://127.0.0.1:{port}{path}', headers={'Host': '192.168.1.158'})
    try:
        with opener.open(request, timeout=10) as response:
            require(response.status == 200, 'HTTP health check failed.')
            data = response.read(4 * 1024 * 1024 + 1)
            require(len(data) <= 4 * 1024 * 1024, 'Unexpected oversized HTTP response.')
            return data
    except (OSError, urllib.error.URLError, TimeoutError):
        raise Stop('HTTP check failed. Do not treat container liveness as business verification.') from None


def web_check(port, index_sha):
    require(json.loads(http_get(port, '/api/health')).get('status') == 'ok', 'Wrong API health response.')
    require(hashlib.sha256(http_get(port, '/index.html')).hexdigest() == index_sha, 'Wrong frontend version/routing.')


def sql(query, login, password, database=DATABASE, timeout=120):
    require(database in ('master', DATABASE) or re.fullmatch(r'Project1DockerRestore_[0-9a-f]{16}', database),
            'Unexpected SQL database target.')
    require(login in ('sa', LOGIN), 'Unexpected SQL login.')
    env = safe_environment()
    env['SQLCMDPASSWORD'] = password
    return command([SQLCMD, '-S', 'tcp:127.0.0.1,1433', '-U', login, '-d', database,
                    '-C', '-b', '-I', '-x', '-l', '10', '-t', str(timeout), '-h', '-1', '-W', '-w', '65535', '-s', '|'],
                   data='SET NOCOUNT ON;\n' + query + '\nGO\n', env=env, timeout=timeout + 15)


def hidden(prompt):
    require(sys.stdin.isatty(), 'An interactive terminal is required for hidden password input.')
    return getpass.getpass(prompt)


def admin_password():
    password = hidden('Ubuntu SQL sa password (not saved): ')
    sql("IF IS_SRVROLEMEMBER(N'sysadmin') <> 1 OR "
        "LOWER(CONVERT(nvarchar(128),SERVERPROPERTY('MachineName'))) <> N'homelab-server' "
        "THROW 50000, 'Unexpected SQL host/admin.', 1;", 'sa', password, 'master')
    return password


def schema_check(manifest, login, password, database=DATABASE):
    actual = sql('SELECT MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;', login, password, database).splitlines()
    require(actual == manifest['migration_ids'], 'Database migrations differ from release. NO migration will be executed.')
    sql("IF COL_LENGTH(N'dbo.Products',N'StorageLocation') IS NULL OR "
        "COL_LENGTH(N'dbo.Products',N'CicdPracticeNote') IS NOT NULL "
        "THROW 50000, 'Unexpected product schema.', 1;", login, password, database)


def pending_check(login, password):
    sql("IF EXISTS(SELECT 1 FROM dbo.EmailOutboxes WHERE Status=N'Pending') "
        "THROW 50000, 'Pending mail must be reviewed before any API start/restart.', 1;", login, password)


def runtime_check(config, manifest):
    password = config['PROD_SQL_PASSWORD']
    sql(f"""
IF DB_NAME() <> N'{DATABASE}' OR ORIGINAL_LOGIN() <> N'{LOGIN}'
    THROW 50000, 'Unexpected production connection.', 1;
IF LOWER(CONVERT(nvarchar(128),SERVERPROPERTY('MachineName'))) <> N'homelab-server'
    THROW 50000, 'Unexpected SQL host.', 1;
IF COALESCE(IS_SRVROLEMEMBER(N'sysadmin'),1) <> 0 OR COALESCE(IS_MEMBER(N'db_owner'),1) <> 0
    OR COALESCE(IS_MEMBER(N'db_ddladmin'),1) <> 0
    OR COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'ALTER'),1) <> 0
    THROW 50000, 'Runtime permissions are too broad.', 1;
IF IS_MEMBER(N'db_datareader') <> 1 OR IS_MEMBER(N'db_datawriter') <> 1
    THROW 50000, 'Runtime data permissions missing.', 1;
""", LOGIN, password)
    schema_check(manifest, LOGIN, password)
    pending_check(LOGIN, password)


def db_snapshot(login, password, database=DATABASE):
    data = sql("SELECT (SELECT COUNT_BIG(*) FROM dbo.AspNetUsers) AS users, "
               "(SELECT COUNT_BIG(*) FROM dbo.Products) AS products, "
               "(SELECT COUNT_BIG(*) FROM dbo.PurchaseOrders) AS purchase_orders, "
               "(SELECT COUNT_BIG(*) FROM dbo.EmailOutboxes) AS emails FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;",
               login, password, database)
    value = json.loads(data)
    require(set(value) == {'users', 'products', 'purchase_orders', 'emails'}
            and all(isinstance(v, int) and v >= 0 for v in value.values()), 'Invalid count snapshot.')
    return value


def no_other_clients(password):
    sql("IF EXISTS(SELECT 1 FROM sys.dm_exec_sessions WHERE is_user_process=1 "
        "AND session_id<>@@SPID AND database_id=DB_ID(N'Project1Db')) "
        "THROW 50000, 'Other original-database clients are connected; stop and review.', 1;", 'sa', password)


def validate_config(config, tag):
    require(set(config) == ENV_KEYS and config['PROD_TAG'] == tag, 'Wrong production configuration/tag.')
    require(re.fullmatch(r'Production!A7[0-9a-f]{64}', config['PROD_SQL_PASSWORD']), 'Invalid generated runtime password.')
    require(re.fullmatch(r'[0-9a-f]{64}', config['PROD_JWT_KEY']), 'Invalid signing key.')
    require(re.fullmatch(r'NewUser!A7[0-9a-f]{32}', config['PROD_NEW_USER_PASSWORD']), 'Invalid new-user password.')
    require(re.fullmatch(r'[A-Za-z0-9._%+-]+@gmail\.com', config['PROD_GMAIL_ADDRESS'])
            and re.fullmatch(r'[A-Za-z]{16}', config['PROD_GMAIL_APP_PASSWORD']), 'Invalid Gmail address/App Password format.')


def read_config(tag):
    root_file(ENV_FILE, private=True)
    config = {}
    for line in ENV_FILE.read_text().splitlines():
        key, separator, value = line.partition('=')
        require(separator and key in ENV_KEYS and key not in config and not any(c in value for c in '$\r\n\x00'),
                'Private configuration contains unexpected/duplicate keys or unsafe characters.')
        config[key] = value
    validate_config(config, tag)
    return config


def compose(folder, *args, timeout=120):
    # Explicit root-only env, sealed single profile; never inherit COMPOSE_* or PROD_*.
    return docker('compose', '--env-file', str(ENV_FILE), '-f', str(folder / COMPOSE), *args, timeout=timeout)


def release(state):
    folder = ROOT / 'releases' / state['tag']
    root_parents(folder / 'release.json')
    manifest = load_manifest(folder)
    require(manifest['tag'] == state['tag'], 'Sealed release/state mismatch.')
    require(digest(folder / 'release.json') == state['manifest_sha256'], 'Sealed manifest changed.')
    loaded_images(manifest)
    return folder, manifest


def stage():
    require(not os.path.lexists(STATE_FILE), 'An initial-cutover state already exists. Do not overwrite or retry.')
    manifest = archive_check()
    loaded_images(manifest)
    require(digest(INSTALLED_HELPER) == manifest['files_sha256']['ubuntu_production.py'], 'Installed helper/release mismatch.')
    old_running()
    site_contract()
    require(canonical_nginx(OLD_SITE.read_text()) == canonical_nginx((INCOMING / 'project1-legacy.conf').read_text()),
            'Current Project1 site differs from the reviewed legacy configuration.')
    web_check(80, digest(OLD_INDEX))
    with socket.socket() as listener:
        try:
            listener.bind(('127.0.0.1', 8084))
        except OSError:
            raise Stop('Port 8084 is already used; do not stop another service.') from None
    require(shutil.disk_usage('/opt').free >= 3 * 1024**3, 'At least 3 GiB free disk is required.')
    directory(ROOT)
    directory(ROOT / 'releases')
    folder = ROOT / 'releases' / manifest['tag']
    require(not os.path.lexists(folder), 'Sealed release directory already exists.')
    directory(folder)
    for name in PUBLIC_FILES | {'release.json'}:
        data = (INCOMING / name).read_bytes()
        write_new(folder / name, data)
    sealed = load_manifest(folder)
    require(sealed == manifest, 'Source release changed during staging.')
    write_new(ROOT / 'old-nginx.conf', OLD_SITE.read_bytes())
    state = {'phase': 'staged', 'tag': manifest['tag'], 'manifest_sha256': digest(folder / 'release.json'),
             'old_nginx_sha256': digest(OLD_SITE), 'old_nginx_mode': stat.S_IMODE(OLD_SITE.stat().st_mode),
             'old_api_sha256': digest(OLD_API), 'old_index_sha256': digest(OLD_INDEX), 'old_unit_sha256': digest(OLD_UNIT),
             'original_enabled': True}
    save_state(state)
    print('Sealed production files and retained exact old Nginx snapshot. No SQL/service changes or API startup.')


def create_runtime_login(config, password):
    sql(f"IF SUSER_ID(N'{LOGIN}') IS NOT NULL THROW 50000, 'Production login already exists.', 1;", 'sa', password, 'master')
    sql(f"IF USER_ID(N'{LOGIN}') IS NOT NULL THROW 50000, 'Production user already exists.', 1;", 'sa', password)
    sql(f"CREATE LOGIN [{LOGIN}] WITH PASSWORD=N'{config['PROD_SQL_PASSWORD']}', "
        f"CHECK_POLICY=ON,CHECK_EXPIRATION=OFF,DEFAULT_DATABASE=[{DATABASE}];", 'sa', password, 'master')
    sql(f"SET XACT_ABORT ON; BEGIN TRANSACTION; CREATE USER [{LOGIN}] FOR LOGIN [{LOGIN}]; "
        f"ALTER ROLE db_datareader ADD MEMBER [{LOGIN}]; ALTER ROLE db_datawriter ADD MEMBER [{LOGIN}]; COMMIT;", 'sa', password)


def configure(state, folder, manifest):
    require(state['phase'] == 'staged' and not os.path.lexists(ENV_FILE), 'Configure is initial-only; preserve existing state/config.')
    old_running()
    old_files(state)
    require(digest(OLD_SITE) == state['old_nginx_sha256'], 'Old site changed after staging.')
    password = admin_password()
    schema_check(manifest, 'sa', password)
    pending_check('sa', password)
    address = input('Production Gmail sender address (your account): ').strip()
    gmail_password = hidden('Google App Password (not ordinary Google password): ').replace(' ', '')
    config = {'PROD_TAG': state['tag'], 'PROD_SQL_PASSWORD': 'Production!A7' + secrets.token_hex(32),
              'PROD_JWT_KEY': secrets.token_hex(32), 'PROD_NEW_USER_PASSWORD': 'NewUser!A7' + secrets.token_hex(16),
              'PROD_GMAIL_ADDRESS': address, 'PROD_GMAIL_APP_PASSWORD': gmail_password}
    validate_config(config, state['tag'])
    directory(CONFIG_DIR)
    write_new(ENV_FILE, ''.join(f'{k}={v}\n' for k, v in config.items()).encode())
    state['phase'] = 'configuring'
    save_state(state)  # Partial login/user creation is retained, never silently overwritten.
    create_runtime_login(config, password)
    runtime_check(config, manifest)
    compose(folder, 'config', '--quiet')
    require(not compose(folder, 'ps', '--status', 'running', '--services'), 'Production containers already running.')
    state['phase'] = 'configured'
    save_state(state)
    print('Root-only production config and dedicated data-reader/writer login created. Existing users/passwords preserved.')
    print('No application was started, no SMTP login/email was attempted, and no migration was executed.')


def seal_retry_release(incoming, manifest):
    folder = ROOT / 'releases' / manifest['tag']
    require(not os.path.lexists(folder), 'Retry release already exists; preserve partial state and inspect.')
    directory(folder)
    for name in PUBLIC_FILES | {'release.json'}:
        write_new(folder / name, (incoming / name).read_bytes())
    require(load_manifest(folder) == manifest, 'Retry source changed during sealing.')
    return folder


def prepare_retry(state, old_folder, old_manifest, tag):
    require(state['phase'] == 'rolled-back', 'Prepare retry ONLY after a verified rollback; never reset a failed/frozen state.')
    require(isinstance(tag, str) and re.fullmatch(TAG_PATTERN, tag) and tag != state['tag'], 'A different reviewed retry tag is required.')
    incoming = RETRY_INCOMING / tag
    manifest = archive_check(incoming)
    loaded_images(manifest)
    require(digest(INSTALLED_HELPER) == manifest['files_sha256']['ubuntu_production.py'], 'Installed retry helper/release mismatch.')
    # This command is a helper-only repair, not an application/schema/configuration upgrade.
    require(manifest['migration_ids'] == old_manifest['migration_ids']
            and manifest['frontend_sha256'] == old_manifest['frontend_sha256']
            and all(manifest['files_sha256'][name] == old_manifest['files_sha256'][name]
                    for name in PUBLIC_FILES - {'ubuntu_production.py'}), 'Retry changes the deployment contract; separate review required.')
    old_running()
    old_files(state)
    site_contract()
    require(digest(OLD_SITE) == state['old_nginx_sha256'], 'Old site changed since rollback.')
    root_file(ROOT / 'old-nginx.conf', private=True)
    require(digest(ROOT / 'old-nginx.conf') == state['old_nginx_sha256'], 'Retained old site snapshot changed.')
    web_check(80, state['old_index_sha256'])
    config = read_config(state['tag'])
    runtime_check(config, manifest)  # Existing account only. No sa prompt or account DDL.
    require(not compose(old_folder, 'ps', '--status', 'running', '--services'), 'Production containers must remain stopped.')
    require(not os.path.lexists(ROOT / 'releases' / tag), 'Retry release directory already exists.')
    history = state.get('attempt_history', [])
    require(isinstance(history, list), 'Invalid attempt history.')
    history_dir = STATE_DIR / 'history'
    directory(history_dir)
    token = secrets.token_hex(8)
    state_copy = history_dir / f'{state["tag"]}-{token}.json'
    config_copy = history_dir / f'{state["tag"]}-{token}.env'
    write_new(state_copy, (json.dumps(state, sort_keys=True, indent=2) + '\n').encode())
    write_new(config_copy, ENV_FILE.read_bytes())  # Root-only snapshot, never printed.
    original = dict(state)
    state.update(phase='retry-preparing', retry={'next_tag': tag, 'state_copy': str(state_copy), 'config_copy': str(config_copy)})
    save_state(state)  # Interruption is explicit; no silent cross-file retry/reset.
    folder = seal_retry_release(incoming, manifest)
    updated = dict(config, PROD_TAG=tag)
    validate_config(updated, tag)
    replace_file(ENV_FILE, ''.join(f'{key}={value}\n' for key, value in updated.items()).encode())
    compose(folder, 'config', '--quiet')
    require(not compose(folder, 'ps', '--status', 'running', '--services'), 'Production containers unexpectedly running.')
    result = {key: value for key, value in original.items() if key not in ('backup', 'frozen_counts', 'retry')}
    result.update(phase='configured', tag=tag, manifest_sha256=digest(folder / 'release.json'),
                  attempt_history=history + [{'tag': original['tag'], 'state_copy': str(state_copy), 'config_copy': str(config_copy)}])
    save_state(result)
    print('Retry prepared; old website stays running. Existing SQL account, passwords and JWT preserved; only release tag changed.')
    print('Previous state/config snapshots, releases and backups retained. A NEW freeze/backup/off-server verification is required.')


def secure_new_backup(path, uid):
    """Tighten ONLY a newly generated SQL backup, through a verified descriptor.

    SQL Server's umask can produce 0660. General regular()/root_file() rules
    must NOT be relaxed. Refuse symlinks, hard links, wrong owners and devices
    before fchmod; never chmod an unverified pathname or an old backup.
    """
    require(path.parent == BACKUP_DIR and re.fullmatch(r'Project1Db-before-docker-[0-9a-f]{16}\.bak', path.name),
            'Unexpected new backup path.')
    require(BACKUP_DIR.resolve() == BACKUP_DIR, 'Backup directory has a symlink ancestor.')
    parent = os.open(BACKUP_DIR, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    try:
        info = os.fstat(parent)
        require(stat.S_ISDIR(info.st_mode) and info.st_uid == uid and stat.S_IMODE(info.st_mode) == 0o700,
                'Unsafe backup directory.')
        fd = os.open(path.name, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK, dir_fd=parent)
        try:
            info = os.fstat(fd)
            require(stat.S_ISREG(info.st_mode) and info.st_uid == uid and info.st_nlink == 1
                    and stat.S_IMODE(info.st_mode) in (0o600, 0o640, 0o660, 0o644, 0o664), 'Unsafe new SQL backup identity/mode.')
            os.fchmod(fd, 0o600)
            secured = os.fstat(fd)
            named = os.stat(path.name, dir_fd=parent, follow_symlinks=False)
            require(stat.S_IMODE(secured.st_mode) == 0o600 and secured.st_uid == uid and secured.st_nlink == 1
                    and (secured.st_dev, secured.st_ino) == (info.st_dev, info.st_ino)
                    and (named.st_dev, named.st_ino) == (secured.st_dev, secured.st_ino), 'Backup identity changed while securing.')
        finally:
            os.close(fd)
    finally:
        os.close(parent)
    regular(path, uid=uid, private=True)


def backup_capacity(password):
    require(BACKUP_DIR.parent.is_dir() and BACKUP_DIR.parent.resolve() == BACKUP_DIR.parent,
            'Existing SQL backup parent is missing/not ordinary; review it BEFORE maintenance.')
    command(['/usr/sbin/runuser', '--user', 'mssql', '--', '/usr/bin/test', '-x', str(BACKUP_DIR.parent)])
    if os.path.lexists(BACKUP_DIR):
        import pwd
        info = BACKUP_DIR.lstat()
        require(stat.S_ISDIR(info.st_mode) and info.st_uid == pwd.getpwnam('mssql').pw_uid
                and stat.S_IMODE(info.st_mode) == 0o700, 'Existing cutover backup directory has unexpected ownership/mode.')
    database_bytes = int(sql('SELECT SUM(CONVERT(bigint,size))*8192 FROM sys.database_files;', 'sa', password))
    require(database_bytes > 0 and shutil.disk_usage(BACKUP_DIR.parent).free >= 3 * database_bytes + 2 * 1024**3,
            'Insufficient disk for backup, restore rehearsal and download copy; no backup was started.')


def make_backup(password, manifest, snapshot):
    import pwd
    mssql = pwd.getpwnam('mssql')
    backup_capacity(password)
    if not os.path.lexists(BACKUP_DIR):
        BACKUP_DIR.mkdir(mode=0o700)
        os.chown(BACKUP_DIR, mssql.pw_uid, mssql.pw_gid)
    info = BACKUP_DIR.lstat()
    require(stat.S_ISDIR(info.st_mode) and info.st_uid == mssql.pw_uid and stat.S_IMODE(info.st_mode) == 0o700,
            'Backup directory must be ordinary mssql-owned mode 0700.')
    token = secrets.token_hex(8)
    path = BACKUP_DIR / f'Project1Db-before-docker-{token}.bak'
    require(not os.path.lexists(path), 'Never overwrite an existing backup.')
    sql(f"BACKUP DATABASE [Project1Db] TO DISK=N'{path}' WITH COPY_ONLY,CHECKSUM,NOINIT,NOFORMAT; "
        f"RESTORE VERIFYONLY FROM DISK=N'{path}' WITH FILE=1,CHECKSUM;", 'sa', password, 'master', timeout=600)
    secure_new_backup(path, mssql.pw_uid)
    # Actual rehearsal into a unique, restricted, read-only COPY; never WITH REPLACE.
    rows = sql(f"RESTORE FILELISTONLY FROM DISK=N'{path}' WITH FILE=1,CHECKSUM;", 'sa', password, 'master').splitlines()
    fields = [row.split('|') for row in rows if row.strip()]
    require(len(fields) == 2 and all(len(row) >= 3 for row in fields), 'Expected exactly two ordinary backup files; review other layouts.')
    logical = {row[2].strip(): row[0].strip() for row in fields}
    require(set(logical) == {'D', 'L'} and all(re.fullmatch(r'[A-Za-z0-9_]+', v) for v in logical.values()),
            'Unsupported logical file names/types; do not guess restore paths.')
    restoredb = f'Project1DockerRestore_{token}'
    restoredir = Path('/var/opt/mssql') / f'project1-docker-restore-{token}'
    require(not os.path.lexists(restoredir), 'Restore directory already exists.')
    restoredir.mkdir(mode=0o700)
    os.chown(restoredir, mssql.pw_uid, mssql.pw_gid)
    sql(f"IF DB_ID(N'{restoredb}') IS NOT NULL THROW 50000, 'Restore database exists.', 1; "
        f"RESTORE DATABASE [{restoredb}] FROM DISK=N'{path}' WITH FILE=1, "
        f"MOVE N'{logical['D']}' TO N'{restoredir}/data.mdf', MOVE N'{logical['L']}' TO N'{restoredir}/log.ldf', "
        f"CHECKSUM,RECOVERY,RESTRICTED_USER; ALTER DATABASE [{restoredb}] SET READ_ONLY WITH NO_WAIT;",
        'sa', password, 'master', timeout=600)
    sql('DBCC CHECKDB WITH NO_INFOMSGS;', 'sa', password, restoredb, timeout=600)
    schema_check(manifest, 'sa', password, restoredb)
    require(db_snapshot('sa', password, restoredb) == snapshot, 'Restored snapshot counts differ; do not activate.')
    return {'path': str(path), 'sha256': digest(path), 'bytes': path.stat().st_size,
            'restore_database': restoredb, 'restore_directory': str(restoredir)}


def backup_download(backup):
    import pwd
    user = pwd.getpwnam('shunbin')
    destination = Path('/home/shunbin') / ('project1-production-backup-download.' + secrets.token_hex(8))
    require(destination.parent.resolve() == destination.parent and not os.path.lexists(destination), 'Unsafe download directory.')
    destination.mkdir(mode=0o700)
    output = destination / 'Project1Db.bak'
    fd = os.open(output, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    with os.fdopen(fd, 'wb') as target, Path(backup['path']).open('rb') as source:
        shutil.copyfileobj(source, target, length=1024 * 1024)
        target.flush()
        os.fsync(target.fileno())
    require(digest(output) == backup['sha256'] and output.stat().st_size == backup['bytes'], 'Download copy differs from backup.')
    os.chown(output, user.pw_uid, user.pw_gid)
    os.chown(destination, user.pw_uid, user.pw_gid)
    return str(output)


def freeze(state, manifest, writers_stopped):
    require(writers_stopped, 'Explicit --writers-stopped confirmation is required; close other writers/clients first.')
    require(state['phase'] == 'configured', 'Freeze is only allowed from configured state.')
    old_running()
    old_files(state)
    site_contract()
    require(digest(OLD_SITE) == state['old_nginx_sha256'], 'Old site changed after staging.')
    config = read_config(state['tag'])
    runtime_check(config, manifest)
    password = admin_password()
    backup_capacity(password)  # Check BEFORE disabling/stopping the old service.
    state['phase'] = 'freezing'
    save_state(state)  # Persist BEFORE any outage/reboot-policy change.
    systemctl('disable', SERVICE)
    systemctl('stop', SERVICE)
    old_stopped()
    state['phase'] = 'frozen'
    save_state(state)
    no_other_clients(password)
    runtime_check(config, manifest)
    snapshot = db_snapshot('sa', password)
    backup = make_backup(password, manifest, snapshot)
    old_stopped()
    no_other_clients(password)
    require(db_snapshot('sa', password) == snapshot, 'Database counts changed during frozen backup; investigate writers.')
    backup['download_file'] = backup_download(backup)
    state.update(phase='backup-ready', backup=backup, frozen_counts=snapshot)
    save_state(state)
    print('OLD API IS STOPPED/DISABLED. Fresh COPY_ONLY/CHECKSUM backup and actual restore/CHECKDB rehearsal passed.')
    print('Retained backup and restricted restore copy; no original-database restore or migration occurred.')
    print('Download file: ' + backup['download_file'])
    print('SHA256: ' + backup['sha256'])
    print('Bytes: ' + str(backup['bytes']))
    print('Keep all writers stopped. Download and verify a protected off-server copy before activate.')


def backup_matches(state, sha256, size):
    backup = state.get('backup', {})
    require(re.fullmatch(r'[0-9a-f]{64}', sha256 or '') and sha256 == backup.get('sha256')
            and size == backup.get('bytes'), 'Supply the SHA256/bytes verified on your protected off-server copy.')
    path = Path(backup['path'])
    require(path.parent == BACKUP_DIR and re.fullmatch(r'Project1Db-before-docker-[0-9a-f]{16}\.bak', path.name), 'Unexpected backup path.')
    require(not path.is_symlink() and digest(path) == sha256 and path.stat().st_size == size, 'Retained backup changed.')


def activate(state, folder, manifest, sha256, size, writers_stopped=False):
    require(writers_stopped, 'Keep all writers stopped and explicitly confirm --writers-stopped before activation.')
    require(state['phase'] == 'backup-ready', 'Activate only from backup-ready; failed/partial states need review/rollback.')
    backup_matches(state, sha256, size)
    old_stopped()
    old_files(state)
    site_contract()
    require(digest(OLD_SITE) == state['old_nginx_sha256'], 'Old Nginx site changed after freeze.')
    config = read_config(state['tag'])
    runtime_check(config, manifest)
    require(db_snapshot(LOGIN, config['PROD_SQL_PASSWORD']) == state['frozen_counts'], 'Counts changed while frozen; inspect writers.')
    no_other_clients(admin_password())
    require(not compose(folder, 'ps', '--status', 'running', '--services'), 'Production containers already running.')
    compose(folder, 'config', '--quiet')
    state['phase'] = 'activating'
    save_state(state)
    try:
        compose(folder, 'up', '-d', '--wait', timeout=240)
        web_check(8084, manifest['frontend_sha256'])
        old_stopped()
        replace_file(OLD_SITE, (folder / 'project1-docker.conf').read_bytes(), state['old_nginx_mode'])
        command(['/usr/sbin/nginx', '-t'])
        systemctl('reload', 'nginx.service')
        web_check(80, manifest['frontend_sha256'])
        runtime_check(config, manifest)
        state['phase'] = 'active-pending-verification'
        save_state(state)
    except (Stop, OSError, ValueError, KeyError):
        state['phase'] = 'activation-failed'
        save_state(state)
        raise Stop('Activation did not verify. Old API stays stopped. Inspect status; use explicit rollback, not repeated activate.') from None
    print('Docker now serves the original port-80 URL using Project1Db; old API remains stopped/disabled.')
    print('Existing user passwords were not reset. New JWT key requires login again. Verify business/PDF/Gmail before finalize.')


def finalize(state, manifest):
    require(state['phase'] == 'active-pending-verification', 'Finalize only after a verified initial activation.')
    old_stopped()
    site_contract()
    require(digest(OLD_SITE) == manifest['files_sha256']['project1-docker.conf'], 'Live site changed.')
    runtime_check(read_config(state['tag']), manifest)
    web_check(80, manifest['frontend_sha256'])
    state['phase'] = 'active'
    save_state(state)
    print('Cutover recorded active. Keep old files/config, backups and restore copy. Legacy non-Docker CD must not be used.')


def rollback(state, folder, manifest, writers_stopped):
    require(writers_stopped and state['phase'] in FROZEN_PHASES,
            'Rollback requires a frozen/activation state and --writers-stopped confirmation.')
    old_files(state)
    # A failed activation may have left a syntactically invalid candidate on disk.
    # Validate ownership/identity now, restore known old bytes, THEN test syntax.
    site_contract(test=False)
    require(digest(OLD_SITE) in {state['old_nginx_sha256'], manifest['files_sha256']['project1-docker.conf']},
            'Nginx has unrelated changes; do not overwrite them.')
    root_file(ROOT / 'old-nginx.conf', private=True)
    require(digest(ROOT / 'old-nginx.conf') == state['old_nginx_sha256'], 'Retained old Nginx backup changed.')
    state['phase'] = 'rolling-back'
    save_state(state)
    try:
        # Stop the new writer FIRST; if Docker cannot stop it, never start the old API.
        compose(folder, 'down', '--timeout', '45', timeout=120)
        require(not compose(folder, 'ps', '--status', 'running', '--services'), 'New containers still running.')
        config = read_config(state['tag'])
        runtime_check(config, manifest)
        replace_file(OLD_SITE, (ROOT / 'old-nginx.conf').read_bytes(), state['old_nginx_mode'])
        command(['/usr/sbin/nginx', '-t'])
        systemctl('enable', SERVICE)
        systemctl('start', SERVICE)
        systemctl('reload', 'nginx.service')
        for attempt in range(15):
            try:
                old_running()
                web_check(80, state['old_index_sha256'])
                break
            except Stop:
                if attempt == 14:
                    raise
                time.sleep(2)
        state['phase'] = 'rolled-back'
        save_state(state)
    except (Stop, OSError, ValueError, KeyError):
        state['phase'] = 'rollback-failed'
        save_state(state)
        raise Stop('Rollback did not verify. Preserve state; do not force retries or restore the original database.') from None
    print('Old Nginx bytes/service startup restored and checked. New containers stopped; no DB/data/email rollback occurred.')


def main(arguments=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('operation', choices=('archive', 'stage', 'configure', 'prepare-retry', 'freeze', 'activate', 'finalize', 'rollback', 'status'))
    parser.add_argument('--apply', action='store_true')
    parser.add_argument('--writers-stopped', action='store_true')
    parser.add_argument('--backup-sha256')
    parser.add_argument('--backup-bytes', type=int)
    parser.add_argument('--retry-tag')
    args = parser.parse_args(arguments)
    if args.operation not in ('archive', 'status') and not args.apply:
        print('Preview only: ' + args.operation + ' requires --apply. No file/SQL/service changes. Target: original Project1Db.')
        return 0
    try:
        require(args.retry_tag is None or (args.operation in ('archive', 'prepare-retry')
                and re.fullmatch(TAG_PATTERN, args.retry_tag)), 'Retry tag is only accepted for archive/prepare-retry.')
        require(args.operation != 'prepare-retry' or args.retry_tag is not None, 'Prepare retry requires --retry-tag.')
        incoming = RETRY_INCOMING / args.retry_tag if args.retry_tag else INCOMING
        check_host(admin=args.operation != 'archive', incoming=incoming)
        if args.operation == 'archive':
            archive_check(incoming)
            print('Production archive/public files verified. Separate manual docker load is next; nothing started.')
            return 0
        if args.operation == 'status':
            state = read_state()
            print(json.dumps({k: state[k] for k in ('phase', 'tag', 'backup') if k in state}, indent=2))
            return 0
        with locks():
            if args.operation == 'stage':
                stage()
                return 0
            state = read_state()
            folder, manifest = release(state)
            if args.operation == 'configure':
                configure(state, folder, manifest)
            elif args.operation == 'prepare-retry':
                prepare_retry(state, folder, manifest, args.retry_tag)
            elif args.operation == 'freeze':
                freeze(state, manifest, args.writers_stopped)
            elif args.operation == 'activate':
                activate(state, folder, manifest, args.backup_sha256, args.backup_bytes, args.writers_stopped)
            elif args.operation == 'finalize':
                finalize(state, manifest)
            else:
                rollback(state, folder, manifest, args.writers_stopped)
        return 0
    except Stop as error:
        print('STOP: ' + str(error) + ' No automatic database restore/cleanup/retry.', file=sys.stderr)
        return 1
    except (OSError, ValueError, KeyError, TypeError):
        print('STOP: invalid file/state/response. Details withheld to protect secrets. Preserve state and inspect; no blind retries.', file=sys.stderr)
        return 1


if __name__ == '__main__':
    sys.exit(main())
