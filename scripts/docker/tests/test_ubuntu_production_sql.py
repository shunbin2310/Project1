"""Opt-in integration against disposable, unpublished Docker containers ONLY.

No production/private env files, SSH or host SQL are used. SQL image must already
be cached. Every created resource has a random name and ownership label.
"""
import json
import importlib.util
import os
from pathlib import Path
import re
import subprocess
import time
import unittest
import uuid
import types

REPO = Path(__file__).resolve().parents[3]
# PUBLIC disposable fixture credential; never use this for any actual deployment.
PASSWORD = 'CutoverFixtureOnly!2026aA7'
SQL_IMAGE = 'mcr.microsoft.com/mssql/server:2022-latest'
SQLCMD = '/opt/mssql-tools18/bin/sqlcmd'
LABEL = 'project1.cutover-test'

PAYLOAD = r'''
import hashlib, importlib.util, json, os, pwd, stat, subprocess, sys
from unittest.mock import patch
spec = importlib.util.spec_from_file_location('prod', '/tmp/ubuntu_production.py')
prod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(prod)
password = 'CutoverFixtureOnly!2026aA7'
config = {'PROD_SQL_PASSWORD': 'Production!A7' + '0' * 64}
manifest = {'migration_ids': json.loads(sys.argv[2])}
if sys.argv[1] == 'backup':
    prod.create_runtime_login(config, password)
    prod.runtime_check(config, manifest)
    prod.no_other_clients(password)
    before = prod.db_snapshot('sa', password)
    secure = prod.secure_new_backup
    reproduced = []
    def secure_with_host_mode(path, uid):
        # Reproduce the real Ubuntu SQL Server 0660 output, not the image's umask.
        os.chmod(path, 0o660)
        assert stat.S_IMODE(path.stat().st_mode) == 0o660
        try:
            prod.regular(path, uid=uid)
        except prod.Stop:
            pass  # General permission guards remain strict.
        else:
            raise AssertionError('general guard was weakened')
        secure(path, uid)
        assert stat.S_IMODE(path.stat().st_mode) == 0o600
        reproduced.append(path)
    prod.secure_new_backup = secure_with_host_mode
    backup = prod.make_backup(password, manifest, before)
    prod.secure_new_backup = secure
    assert len(reproduced) == 1
    assert backup['bytes'] > 0 and len(backup['sha256']) == 64
    assert backup['restore_database'].startswith('Project1DockerRestore_')
    assert prod.db_snapshot('sa', password) == before
    assert prod.sql("SELECT CONVERT(int,is_read_only) FROM sys.databases WHERE name=DB_NAME();", 'sa', password) == '0'
    assert prod.sql("SELECT CONVERT(int,is_read_only) FROM sys.databases WHERE name=DB_NAME();", 'sa', password, backup['restore_database']) == '1'
    try:
        user = pwd.getpwnam('shunbin')
    except KeyError:
        subprocess.run(['/usr/sbin/useradd', '--create-home', '--shell', '/bin/sh', 'shunbin'], check=True)
        user = pwd.getpwnam('shunbin')
    download = prod.Path(prod.backup_download(backup))
    assert download.stat().st_uid == user.pw_uid and stat.S_IMODE(download.stat().st_mode) == 0o600
    assert download.parent.stat().st_uid == user.pw_uid and stat.S_IMODE(download.parent.stat().st_mode) == 0o700
    assert prod.digest(download) == backup['sha256'] and download.stat().st_size == backup['bytes']
    private = prod.Path('/opt/project1-contract-files')
    prod.directory(private)
    prod.write_new(private / 'fixture', b'public-only-fixture')
    prod.root_file(private / 'fixture', private=True)
    prod.replace_file(private / 'fixture', b'public-updated-fixture')
    assert (private / 'fixture').read_bytes() == b'public-updated-fixture'
    os.symlink('fixture', private / 'unsafe-link')
    try:
        prod.root_file(private / 'unsafe-link')
    except prod.Stop:
        pass
    else:
        raise AssertionError('symlink was accepted')
    mssql = pwd.getpwnam('mssql')
    original_backup = prod.Path(backup['path'])
    for number, kind in enumerate(('symlink', 'hardlink', 'fifo', 'wrong-owner', 'world-writable'), 1):
        unsafe = prod.BACKUP_DIR / f'Project1Db-before-docker-{number:016x}.bak'
        if kind == 'symlink':
            os.symlink(original_backup.name, unsafe)
        elif kind == 'hardlink':
            os.link(original_backup, unsafe)
        elif kind == 'fifo':
            os.mkfifo(unsafe, 0o600)
            os.chown(unsafe, mssql.pw_uid, mssql.pw_gid)
        else:
            prod.write_new(unsafe, b'public-unsafe-fixture')
            if kind == 'world-writable':
                os.chown(unsafe, mssql.pw_uid, mssql.pw_gid)
                os.chmod(unsafe, 0o666)
        try:
            try:
                prod.secure_new_backup(unsafe, mssql.pw_uid)
            except (prod.Stop, OSError):
                pass
            else:
                raise AssertionError('unsafe native backup accepted: ' + kind)
        finally:
            unsafe.unlink()  # Only this explicitly created fixture, inside disposable container.
        assert stat.S_IMODE(original_backup.stat().st_mode) == 0o600
    print('PASS native permissions, COPY_ONLY/CHECKSUM, VERIFYONLY, isolated restore, CHECKDB and preserved original snapshot')
elif sys.argv[1] == 'retry':
    # Native Linux filesystem + real restricted SQL check. Only service/Docker
    # commands are mocked here; no host service/config is accessible in this container.
    user = pwd.getpwnam('shunbin')
    prod.ROOT = prod.Path('/opt/project1-retry-contract')
    prod.directory(prod.ROOT)
    prod.STATE_DIR = prod.ROOT / 'state'
    prod.STATE_FILE = prod.STATE_DIR / 'state.json'
    prod.ENV_FILE = prod.ROOT / 'private.env'
    prod.OLD_SITE = prod.ROOT / 'legacy-site'
    prod.INSTALLED_HELPER = prod.ROOT / 'installed.py'
    for path in (prod.STATE_DIR, prod.ROOT / 'releases'):
        prod.directory(path)
    old_tag = 'prod-20261019t000000z-00000000'
    tag = 'prod-20261019t000001z-00000001'
    old_folder = prod.ROOT / 'releases' / old_tag
    prod.directory(old_folder)
    prod.write_new(prod.OLD_SITE, b'public-legacy-site')
    prod.write_new(prod.ROOT / 'old-nginx.conf', b'public-legacy-site')
    helper = prod.Path('/tmp/ubuntu_production.py').read_bytes()
    prod.write_new(prod.INSTALLED_HELPER, helper)
    old_manifest = dict(format=1, tag=old_tag, migration_ids=manifest['migration_ids'],
                        archive_sha256='a'*64, frontend_sha256='b'*64,
                        images={name: dict(reference=f'project1-ubuntu-{name}:{old_tag}', id='sha256:'+'a'*64, user='1654') for name in ('api','frontend')})
    old_manifest['files_sha256'] = {}
    for name in prod.PUBLIC_FILES:
        data = b'old-helper-public-fixture' if name == 'ubuntu_production.py' else b'public-contract-fixture'
        prod.write_new(old_folder / name, data)
        old_manifest['files_sha256'][name] = hashlib.sha256(data).hexdigest()
    prod.write_new(old_folder / 'release.json', json.dumps(old_manifest).encode())
    prod.RETRY_INCOMING.mkdir(mode=0o700)
    os.chown(prod.RETRY_INCOMING, user.pw_uid, user.pw_gid)
    incoming = prod.RETRY_INCOMING / tag
    incoming.mkdir(mode=0o700)
    os.chown(incoming, user.pw_uid, user.pw_gid)
    new = json.loads(json.dumps(old_manifest))
    new.update(tag=tag, archive_sha256=hashlib.sha256(b'public-fake-archive').hexdigest())
    for name in ('api','frontend'):
        new['images'][name]['reference'] = f'project1-ubuntu-{name}:{tag}'
    for name in prod.PUBLIC_FILES:
        data = helper if name == 'ubuntu_production.py' else (old_folder / name).read_bytes()
        prod.write_new(incoming / name, data)
        new['files_sha256'][name] = hashlib.sha256(data).hexdigest()
    prod.write_new(incoming / 'images.tar', b'public-fake-archive')
    prod.write_new(incoming / 'release.json', json.dumps(new).encode())
    for path in incoming.iterdir():
        os.chown(path, user.pw_uid, user.pw_gid)
    settings = dict(PROD_TAG=old_tag, PROD_SQL_PASSWORD=config['PROD_SQL_PASSWORD'], PROD_JWT_KEY='0'*64,
                    PROD_NEW_USER_PASSWORD='NewUser!A7'+'0'*32, PROD_GMAIL_ADDRESS='fixture@gmail.com', PROD_GMAIL_APP_PASSWORD='abcdefghijklmnop')
    raw_config = ''.join(f'{k}={v}\n' for k,v in settings.items()).encode()
    prod.write_new(prod.ENV_FILE, raw_config)
    state = dict(phase='rolled-back', tag=old_tag, old_nginx_sha256=prod.digest(prod.OLD_SITE),
                 old_index_sha256='b'*64, backup={'sha256': 'c'*64}, frozen_counts={'users':1},
                 manifest_sha256=prod.digest(old_folder / 'release.json'))
    prod.save_state(state)
    with patch.object(prod, 'old_running'), patch.object(prod, 'old_files'), patch.object(prod, 'site_contract'), \
         patch.object(prod, 'web_check'), patch.object(prod, 'loaded_images'), patch.object(prod, 'compose', return_value=''), \
         patch.object(prod, 'systemctl', side_effect=AssertionError('service mutation forbidden')), \
         patch.object(prod, 'create_runtime_login', side_effect=AssertionError('account DDL forbidden')):
        prod.prepare_retry(state, old_folder, old_manifest, tag)
    final = prod.read_state()
    assert final['phase'] == 'configured' and final['tag'] == tag
    assert 'backup' not in final and 'frozen_counts' not in final
    assert prod.read_config(tag) == dict(settings, PROD_TAG=tag)
    entry = final['attempt_history'][-1]
    assert prod.Path(entry['config_copy']).read_bytes() == raw_config
    assert json.loads(prod.Path(entry['state_copy']).read_text())['phase'] == 'rolled-back'
    for path in (prod.ENV_FILE, prod.Path(entry['config_copy']), prod.Path(entry['state_copy'])):
        prod.root_file(path, private=True)
    assert prod.load_manifest(old_folder) == old_manifest
    assert prod.load_manifest(prod.ROOT / 'releases' / tag) == new
    prod.runtime_check(prod.read_config(tag), new)
    spec = importlib.util.spec_from_file_location('recheck', '/tmp/recheck_ubuntu_activation.py')
    recheck = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(recheck)
    recheck.TARGET_TAG = tag  # This disposable fixture has its own sealed tag.
    prod.replace_file(prod.OLD_SITE, (prod.ROOT / 'releases' / tag / 'project1-docker.conf').read_bytes())
    backup_path = next(prod.BACKUP_DIR.glob('Project1Db-before-docker-*.bak'))
    failed = prod.read_state()
    failed.update(phase='activation-failed', backup=dict(path=str(backup_path), sha256=prod.digest(backup_path), bytes=backup_path.stat().st_size))
    prod.save_state(failed)
    initial_state_hash = prod.digest(prod.STATE_FILE)
    env_hash = prod.digest(prod.ENV_FILE)
    with patch.object(prod, 'loaded_images'), patch.object(prod, 'old_files'), patch.object(prod, 'old_stopped'), \
         patch.object(prod, 'site_contract'), patch.object(prod, 'web_check'), patch.object(prod, 'compose', return_value='api\nfrontend'), \
         patch.object(recheck, 'inspect_container'), patch.object(recheck.time, 'sleep'), \
         patch.object(prod, 'systemctl', side_effect=AssertionError('service mutation forbidden')):
        recheck.reconcile(prod, failed['backup']['sha256'], failed['backup']['bytes'], apply=False)
        assert prod.digest(prod.STATE_FILE) == initial_state_hash
        recheck.reconcile(prod, failed['backup']['sha256'], failed['backup']['bytes'], apply=True)
    checked = prod.read_state()
    assert checked['phase'] == 'active-pending-verification'
    assert checked['activation_recheck']['initial_failure_cause'] == 'undetermined'
    audit = prod.Path(checked['activation_recheck']['audit_file'])
    prod.root_file(audit, private=True)
    assert json.loads(audit.read_text()) == failed
    assert prod.digest(prod.ENV_FILE) == env_hash
    print('PASS native retry sealing/history/config preservation and actual existing SQL credentials; no services or account DDL')
elif sys.argv[1] == 'roles':
    prod.sql('ALTER ROLE db_owner ADD MEMBER [project1_docker_production];', 'sa', password)
    try:
        try:
            prod.runtime_check(config, manifest)
        except prod.Stop:
            pass
        else:
            raise AssertionError('broad role was accepted')
    finally:
        prod.sql('ALTER ROLE db_owner DROP MEMBER [project1_docker_production];', 'sa', password)
    prod.runtime_check(config, manifest)
    print('PASS real SQL negative role-scope check')
else:
    raise AssertionError('unsupported test step')
'''


@unittest.skipUnless(os.environ.get('PROJECT1_CUTOVER_SQL_TESTS') == 'true', 'explicit isolated Docker test flag not set')
class SqlDockerTests(unittest.TestCase):
    @classmethod
    def docker(cls, *args, check=True, data=None, timeout=180):
        result = subprocess.run(['docker', *args], input=data, capture_output=True, text=True, timeout=timeout, check=False)
        if check and result.returncode:
            # This fixture contains ONLY public fake data/credentials. Production helpers
            # deliberately do not expose raw stderr, but fixture diagnostics are safe.
            raise AssertionError('Disposable Docker test failed (no production target): ' + result.stderr[-2000:] + result.stdout[-1000:])
        return result

    @classmethod
    def query(cls, query, master=False):
        return cls.docker('exec', '--env', 'SQLCMDPASSWORD=' + PASSWORD, cls.sql_name, SQLCMD,
                          '-S', 'tcp:127.0.0.1,1433', '-U', 'sa', '-d', 'master' if master else 'Project1Db',
                          '-C', '-b', '-I', '-x', '-h', '-1', '-W', '-Q', 'SET NOCOUNT ON; ' + query).stdout.strip()

    @classmethod
    def setUpClass(cls):
        cls.token = uuid.uuid4().hex[:12]
        cls.net = 'codex-project1-cutover-net-' + cls.token
        cls.sql_name = 'codex-project1-cutover-sql-' + cls.token
        cls.api_name = 'codex-project1-cutover-api-' + cls.token
        cls.front_name = 'codex-project1-cutover-front-' + cls.token
        cls.created = []
        cls.network_created = False
        cls.api_image = os.environ.get('PROJECT1_CUTOVER_API_IMAGE', '')
        cls.front_image = os.environ.get('PROJECT1_CUTOVER_FRONTEND_IMAGE', '')
        cls.migrate_image = os.environ.get('PROJECT1_CUTOVER_MIGRATE_IMAGE', '')
        for name, pattern in ((cls.api_image, r'project1-ubuntu-api:prod-[0-9]{8}t[0-9]{6}z-[0-9a-f]{8}'),
                              (cls.front_image, r'project1-ubuntu-frontend:prod-[0-9]{8}t[0-9]{6}z-[0-9a-f]{8}'),
                              (cls.migrate_image, r'project1-ubuntu-practice-migrate:practice-[0-9]{8}t[0-9]{6}z-[0-9a-f]{8}')):
            if not re.fullmatch(pattern, name):
                raise AssertionError('Explicit reviewed local image tags are required for this isolated test.')
        cls.migrations = sorted(path.stem for path in (REPO / 'backend/Project1.Api/Migrations').glob('*.cs')
                                if re.fullmatch(r'[0-9]{14}_[A-Za-z0-9_]+', path.stem))
        try:
            cls.docker('image', 'inspect', '--format', '{{.Id}}', SQL_IMAGE)
            cls.docker('network', 'create', '--internal', '--label', LABEL + '=' + cls.token, cls.net)
            cls.network_created = True
            cls.docker('run', '-d', '--rm', '--pull', 'never', '--name', cls.sql_name, '--hostname', 'homelab-server',
                       '--label', LABEL + '=' + cls.token, '--network', cls.net, '--network-alias', 'sqlfixture',
                       '--memory', '3g', '--cpus', '1', '--env', 'ACCEPT_EULA=Y', '--env', 'MSSQL_PID=Developer',
                       '--env', 'MSSQL_MEMORY_LIMIT_MB=2048', '--env', 'MSSQL_SA_PASSWORD=' + PASSWORD, SQL_IMAGE)
            cls.created.append(cls.sql_name)
            for attempt in range(90):
                ready = cls.docker('exec', '--env', 'SQLCMDPASSWORD=' + PASSWORD, cls.sql_name, SQLCMD,
                                   '-S', 'tcp:127.0.0.1,1433', '-U', 'sa', '-C', '-b', '-l', '2', '-Q', 'SELECT 1;', check=False)
                if ready.returncode == 0:
                    break
                time.sleep(1)
            else:
                raise AssertionError('Disposable SQL did not become ready.')
            cls.query('CREATE DATABASE [Project1Db];', master=True)
            cls.docker('run', '--rm', '--pull', 'never', '--network', cls.net,
                       '--env', 'ASPNETCORE_ENVIRONMENT=CutoverFixture',
                       '--env', 'ConnectionStrings__DefaultConnection=Server=sqlfixture,1433;Database=Project1Db;User ID=sa;Password=' + PASSWORD + ';Encrypt=True;TrustServerCertificate=True',
                       '--env', 'Jwt__SigningKey=public-fixture-model-key-not-used-for-authentication-000000',
                       cls.migrate_image, 'database', 'update', '--project', 'backend/Project1.Api', '--startup-project', 'backend/Project1.Api',
                       '--configuration', 'Release', '--no-build', '--context', 'AppDbContext', '--', '--environment', 'CutoverFixture')
            cls.query("INSERT INTO dbo.AspNetUsers (UserName,NormalizedUserName,Email,NormalizedEmail,EmailConfirmed,PasswordHash,"
                      "SecurityStamp,ConcurrencyStamp,PhoneNumberConfirmed,TwoFactorEnabled,LockoutEnabled,AccessFailedCount,FullName,IsActive,CreatedAtUtc) "
                      "VALUES (N'existing@test.invalid',N'EXISTING@TEST.INVALID',N'existing@test.invalid',N'EXISTING@TEST.INVALID',1,"
                      "N'public-fixture-hash-not-a-real-password',N'fixture-stamp',N'fixture-concurrency',0,0,1,0,N'Existing Fixture User',1,SYSUTCDATETIME());")
            cls.docker('cp', str(REPO / 'scripts/docker/ubuntu_production.py'), cls.sql_name + ':/tmp/ubuntu_production.py')
            cls.docker('cp', str(REPO / 'scripts/docker/recheck_ubuntu_activation.py'), cls.sql_name + ':/tmp/recheck_ubuntu_activation.py')
            # New disposable image does not include the host's existing backup directory.
            cls.docker('exec', '--user', '0', cls.sql_name, '/usr/bin/install', '-d', '-o', 'mssql', '-g', 'mssql',
                       '-m', '0700', '/var/opt/mssql/backup')
        except Exception:
            cls.tearDownClass()
            raise

    @classmethod
    def tearDownClass(cls):
        # Remove ONLY our random, labelled disposable resources. No prune or host paths.
        for name in reversed(cls.created):
            result = cls.docker('inspect', '--format', '{{index .Config.Labels "' + LABEL + '"}}', name, check=False)
            if result.returncode == 0 and result.stdout.strip() == cls.token:
                cls.docker('stop', '--time', '10', name, check=False)
        if cls.network_created:
            result = cls.docker('network', 'inspect', '--format', '{{index .Labels "' + LABEL + '"}}', cls.net, check=False)
            if result.returncode == 0 and result.stdout.strip() == cls.token:
                cls.docker('network', 'rm', cls.net, check=False)

    def payload(self, operation):
        result = self.docker('exec', '-i', '--user', '0', self.sql_name, '/usr/bin/python3', '-', operation,
                             json.dumps(self.migrations), data=PAYLOAD)
        self.assertIn('PASS', result.stdout)

    def test_01_backup_restore_and_permissions_on_real_isolated_sql(self):
        self.payload('backup')

    def test_02_production_image_and_frontend_proxy_preserve_existing_user(self):
        self.docker('run', '-d', '--rm', '--pull', 'never', '--name', self.api_name,
                    '--label', LABEL + '=' + self.token, '--network', self.net, '--network-alias', 'api',
                    '--label', 'com.docker.compose.project=project1-ubuntu-production', '--label', 'com.docker.compose.service=api',
                    '--health-cmd', 'curl --fail --silent http://127.0.0.1:8080/api/health', '--health-interval', '1s',
                    '--memory', '1g', '--env', 'ASPNETCORE_ENVIRONMENT=Production', '--env', 'ASPNETCORE_URLS=http://+:8080',
                    '--env', 'ConnectionStrings__DefaultConnection=Server=sqlfixture,1433;Database=Project1Db;User ID=project1_docker_production;Password=Production!A7' + '0' * 64 + ';Encrypt=True;TrustServerCertificate=True',
                    '--env', 'Jwt__SigningKey=' + '0' * 64, '--env', 'Jwt__Issuer=Fixture', '--env', 'Jwt__Audience=Fixture',
                    '--env', 'DemoUsers__Enabled=false', '--env', 'DemoUsers__DefaultPassword=public-new-user-fixture-only-A7!',
                    '--env', 'Email__Smtp__Host=127.0.0.1', '--env', 'Email__Smtp__Port=1', self.api_image)
        self.created.append(self.api_name)
        for attempt in range(60):
            ready = self.docker('exec', self.api_name, 'curl', '-sS', '-o', '/dev/null', '-w', '%{http_code}',
                                'http://127.0.0.1:8080/api/health', check=False)
            if ready.returncode == 0 and ready.stdout == '200':
                break
            time.sleep(1)
        else:
            self.fail('Disposable production API did not become healthy.')
        no_openapi = self.docker('exec', self.api_name, 'curl', '-sS', '-o', '/dev/null', '-w', '%{http_code}',
                                 'http://127.0.0.1:8080/openapi/v1.json').stdout
        self.assertEqual('404', no_openapi)
        self.docker('run', '-d', '--rm', '--pull', 'never', '--name', self.front_name,
                    '--label', LABEL + '=' + self.token, '--network', self.net, '--network-alias', 'frontfixture',
                    '--label', 'com.docker.compose.project=project1-ubuntu-production', '--label', 'com.docker.compose.service=frontend',
                    '--health-cmd', 'wget -q -O - http://127.0.0.1:8080/healthz', '--health-interval', '1s', self.front_image)
        self.created.append(self.front_name)
        for attempt in range(30):
            ready = self.docker('exec', self.api_name, 'curl', '-sS', '-o', '/dev/null', '-w', '%{http_code}',
                                'http://frontfixture:8080/healthz', check=False)
            if ready.returncode == 0 and ready.stdout == '200':
                break
            time.sleep(1)
        else:
            self.fail('Disposable frontend did not start.')
        for path, expected in (('/api/health', '200'), ('/api/auth/me', '401'), ('/login', '200')):
            actual = self.docker('exec', self.api_name, 'curl', '-sS', '-o', '/dev/null', '-w', '%{http_code}',
                                 'http://frontfixture:8080' + path).stdout
            self.assertEqual(expected, actual)
        self.assertEqual('1', self.query('SELECT COUNT(*) FROM dbo.AspNetUsers;'))
        self.assertEqual('1', self.query("SELECT COUNT(*) FROM dbo.AspNetUsers WHERE UserName=N'existing@test.invalid' AND PasswordHash=N'public-fixture-hash-not-a-real-password';"))
        self.docker('cp', str(REPO / 'scripts/docker/nginx/project1-docker.conf'), self.front_name + ':/tmp/project1-docker.conf')
        self.docker('cp', str(REPO / 'scripts/docker/tests/host-nginx-validation.conf'), self.front_name + ':/tmp/test-host-nginx.conf')
        self.docker('exec', '--user', '0', self.front_name, 'nginx', '-t', '-c', '/tmp/test-host-nginx.conf', '-p', '/tmp')
        spec = importlib.util.spec_from_file_location('native_recheck', REPO / 'scripts/docker/recheck_ubuntu_activation.py')
        recheck = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(recheck)
        for service, name, reference in (('api', self.api_name, self.api_image), ('frontend', self.front_name, self.front_image)):
            for attempt in range(15):
                if self.docker('inspect', '--format', '{{.State.Health.Status}}', name).stdout.strip() == 'healthy':
                    break
                time.sleep(1)
            else:
                self.fail('Disposable native metadata fixture was not healthy.')
            recheck.inspect_container(types.SimpleNamespace(docker=lambda *args: self.docker(*args).stdout.strip()),
                                      service, {'reference': reference}, name=name)

    def test_03_real_role_escalation_is_rejected(self):
        self.payload('roles')

    def test_04_native_retry_files_and_existing_sql_credentials(self):
        self.payload('retry')


if __name__ == '__main__':
    unittest.main()
