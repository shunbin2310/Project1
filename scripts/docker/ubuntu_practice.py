"""Manual, fail-closed Ubuntu practice setup. No original application config is read.

Preview: python3 ubuntu_practice.py init
Mutations always require a named command and --apply. No cleanup/drop/restore exists.
Run as shunbin in /home/shunbin/project1-ubuntu-practice, NOT with sudo python3.
"""

import argparse
import getpass
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import secrets
import socket
import stat
import subprocess
import sys
import tempfile

ROOT = Path('/home/shunbin/project1-ubuntu-practice')
DATABASE = 'Project1DockerUbuntuPracticeDb'
RUNTIME_LOGIN = 'project1_ubuntu_practice'
MIGRATION_LOGIN = 'project1_ubuntu_practice_migrator'
RUNTIME_FILE = '.env.docker.ubuntu-practice.local'
MIGRATION_FILE = '.env.docker.ubuntu-practice.migrate.local'
STATE_FILE = '.ubuntu-practice-state.json'
COMPOSE = 'compose.ubuntu-practice.yml'
SQLCMD = '/opt/mssql-tools18/bin/sqlcmd'
TAG_PATTERN = r'practice-[0-9]{8}t[0-9]{6}z-[0-9a-f]{8}'
ENV_KEYS = {
    'PRACTICE_TAG', 'PRACTICE_SQL_PASSWORD', 'PRACTICE_JWT_KEY',
    'PRACTICE_DEMO_PASSWORD', 'PRACTICE_SMTP_HOST', 'PRACTICE_SMTP_PORT',
    'PRACTICE_SMTP_SSL', 'PRACTICE_SMTP_USERNAME', 'PRACTICE_SMTP_PASSWORD',
    'PRACTICE_SMTP_FROM',
}


class Stop(RuntimeError):
    pass


def require(condition, message):
    if not condition:
        raise Stop(message)


def environment():
    # Do not inherit SQLCMD*, Compose overrides, application or Gmail secrets.
    return {'PATH': '/usr/bin:/bin', 'HOME': str(ROOT.parent), 'LANG': 'C.UTF-8'}


def regular_file(path, private=False):
    info = path.lstat()
    require(stat.S_ISREG(info.st_mode) and info.st_nlink == 1,
            'Expected an ordinary file, not a symlink/hardlink.')
    require(info.st_uid == os.getuid(), 'File must belong to the current user.')
    if private:
        require(stat.S_IMODE(info.st_mode) == 0o600, 'Private file must have mode 0600.')


def check_host():
    require(sys.platform == 'linux' and platform.machine() == 'x86_64',
            'Only the reviewed Ubuntu x86_64 host is supported.')
    require(socket.gethostname().lower() == 'homelab-server' and getpass.getuser() == 'shunbin'
            and os.geteuid() != 0, 'Run as shunbin on homelab-server, not sudo python3.')
    require('VERSION_ID="24.04"' in Path('/etc/os-release').read_text(),
            'Expected Ubuntu 24.04; review before using another host.')
    require(Path.cwd() == ROOT and ROOT.resolve() == ROOT,
            'Run from the ordinary /home/shunbin/project1-ubuntu-practice directory.')
    info = ROOT.lstat()
    require(stat.S_ISDIR(info.st_mode) and info.st_uid == os.getuid()
            and stat.S_IMODE(info.st_mode) == 0o700, 'Practice directory must be owned by shunbin, mode 0700.')
    require(Path(SQLCMD).is_file(), 'The reviewed native sqlcmd 18 client is missing.')
    os.umask(0o077)


def process(args, *, data=None, env=None, timeout=60):
    try:
        result = subprocess.run(args, input=data, text=True, capture_output=True,
                                env=env or environment(), timeout=timeout, check=False)
    except (OSError, subprocess.TimeoutExpired):
        raise Stop('Command could not complete. No automatic retry or cleanup was performed.') from None
    # Never echo SQL batches, credentials, full config/inspect, or raw exception output.
    require(result.returncode == 0,
            'Command/check failed; output withheld to protect credentials. Stop and inspect server state; do not blindly retry.')
    return result.stdout.strip()


def docker(*args, timeout=60):
    return process(['sudo', '-n', '/usr/bin/docker', *args], timeout=timeout)


def sql(query, login, password, database=DATABASE):
    require(database in ('master', DATABASE), 'Original databases may not be targeted.')
    require(login in ('sa', RUNTIME_LOGIN, MIGRATION_LOGIN), 'Unexpected SQL login.')
    env = environment()
    env['SQLCMDPASSWORD'] = password
    return process([SQLCMD, '-S', 'tcp:127.0.0.1,1433', '-U', login, '-d', database,
                    '-C', '-b', '-I', '-x', '-l', '10', '-t', '120', '-h', '-1', '-W', '-w', '2000'],
                   data='SET NOCOUNT ON;\n' + query + '\nGO\n', env=env, timeout=150)


def load_manifest():
    regular_file(ROOT / 'release.json')
    manifest = json.loads((ROOT / 'release.json').read_text(encoding='utf-8'))
    require(isinstance(manifest, dict), 'Invalid release object.')
    require(manifest.get('format') == 1 and re.fullmatch(TAG_PATTERN, manifest.get('tag', '')),
            'Invalid release format/tag.')
    require(re.fullmatch(r'[0-9a-f]{64}', manifest.get('archive_sha256', '')), 'Invalid archive hash.')
    ids = manifest.get('migration_ids')
    require(isinstance(ids, list) and bool(ids)
            and all(isinstance(i, str) and re.fullmatch(r'[0-9]{14}_[A-Za-z0-9_]+', i) for i in ids)
            and ids == sorted(set(ids)), 'Invalid migration manifest.')
    require(isinstance(manifest.get('images'), dict)
            and set(manifest['images']) == {'api', 'frontend', 'migrate'}, 'Expected three images.')
    for name, metadata in manifest['images'].items():
        require(isinstance(metadata, dict), 'Invalid image metadata.')
        require(metadata.get('reference') == f'project1-ubuntu-practice-{name}:{manifest["tag"]}'
                and re.fullmatch(r'sha256:[0-9a-f]{64}', metadata.get('id', '')),
                'Unexpected image reference/ID.')
    hashes = manifest.get('files_sha256')
    require(isinstance(hashes, dict)
            and set(hashes) == {COMPOSE, 'ubuntu_practice.py', '.env.docker.ubuntu-practice.example'},
            'Release file hashes are missing.')
    for name, expected in hashes.items():
        require(isinstance(expected, str) and re.fullmatch(r'[0-9a-f]{64}', expected), 'Invalid release file hash.')
        regular_file(ROOT / name)
        require(secrets.compare_digest(hashlib.sha256((ROOT / name).read_bytes()).hexdigest(), expected),
                'A release helper/configuration file changed. Stop and review; do not bypass the hash.')
    return manifest


def verify_images(manifest):
    for metadata in manifest['images'].values():
        actual = docker('image', 'inspect', '--format', '{{.Id}}|{{.Os}}|{{.Architecture}}', metadata['reference'])
        require(actual == metadata['id'] + '|linux|amd64', 'Loaded image does not match the release.')


def verify_archive(manifest):
    regular_file(ROOT / 'images.tar')
    with (ROOT / 'images.tar').open('rb') as file:
        actual = hashlib.file_digest(file, 'sha256').hexdigest()
    require(secrets.compare_digest(actual, manifest['archive_sha256']), 'Archive SHA256 mismatch; do not load.')


def write_new(path, text):
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    with os.fdopen(descriptor, 'w', encoding='utf-8', newline='\n') as file:
        file.write(text)


def replace_private(path, text):
    regular_file(path, private=True)
    descriptor, temporary = tempfile.mkstemp(prefix='.practice-', dir=ROOT)
    try:
        with os.fdopen(descriptor, 'w', encoding='utf-8', newline='\n') as file:
            file.write(text)
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def dump_env(values):
    return ''.join(f'{key}={value}\n' for key, value in values.items())


def read_env(path, keys):
    regular_file(path, private=True)
    values = {}
    for line in path.read_text(encoding='utf-8').splitlines():
        if not line or line.startswith('#'):
            continue
        key, separator, value = line.partition('=')
        require(separator and key in keys and key not in values and not any(c in value for c in '$\r\n\x00'),
                'Private configuration has unexpected/duplicate keys or unsafe characters.')
        values[key] = value
    require(set(values) == keys, 'Private configuration keys are missing.')
    return values


def validate_runtime(values, manifest):
    require(values['PRACTICE_TAG'] == manifest['tag'], 'Private configuration/release tag mismatch.')
    require(re.fullmatch(r'Practice!A7[0-9a-f]{64}', values['PRACTICE_SQL_PASSWORD']), 'Unexpected generated SQL password format.')
    require(re.fullmatch(r'[0-9a-f]{64}', values['PRACTICE_JWT_KEY']), 'Unexpected JWT key format.')
    require(re.fullmatch(r'Demo!A7[0-9a-f]{32}', values['PRACTICE_DEMO_PASSWORD']), 'Unexpected demo password format.')
    mail = tuple(values[k] for k in ('PRACTICE_SMTP_HOST', 'PRACTICE_SMTP_PORT', 'PRACTICE_SMTP_SSL',
                                    'PRACTICE_SMTP_USERNAME', 'PRACTICE_SMTP_PASSWORD', 'PRACTICE_SMTP_FROM'))
    disabled = ('127.0.0.1', '1', 'false', '', '', 'purchasing@project1.invalid')
    gmail = mail[:3] == ('smtp.gmail.com', '587', 'true') and bool(re.fullmatch(r'[A-Za-z0-9._%+-]+@gmail\.com', mail[3])) \
        and bool(re.fullmatch(r'[a-zA-Z]{16}', mail[4])) and mail[5] == mail[3]
    require(mail == disabled or gmail, 'Only disabled mail or explicitly configured Gmail is supported.')


def compose(runtime, *args, migration=False, timeout=60):
    regular_file(ROOT / COMPOSE)
    command = ['compose', '--env-file', RUNTIME_FILE]
    if migration:
        values = read_env(ROOT / MIGRATION_FILE, {'PRACTICE_MIGRATION_PASSWORD'})
        require(re.fullmatch(r'Practice!A7[0-9a-f]{64}', values['PRACTICE_MIGRATION_PASSWORD']),
                'Unexpected migration password format.')
        command += ['--env-file', MIGRATION_FILE]
    return docker(*command, '-f', COMPOSE, *args, timeout=timeout)


def runtime_query():
    return f"""
IF DB_NAME() <> N'{DATABASE}' THROW 50000, 'Wrong practice database.', 1;
IF LOWER(CONVERT(nvarchar(128),SERVERPROPERTY('MachineName'))) <> N'homelab-server'
    THROW 50000, 'Unexpected SQL Server host.', 1;
IF COALESCE(IS_SRVROLEMEMBER(N'sysadmin'),1) <> 0 OR COALESCE(IS_MEMBER(N'db_owner'),1) <> 0
    OR COALESCE(IS_MEMBER(N'db_ddladmin'),1) <> 0
    OR COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'ALTER'),1) <> 0
    THROW 50000, 'Runtime permissions are too broad.', 1;
IF IS_MEMBER(N'db_datareader') <> 1 OR IS_MEMBER(N'db_datawriter') <> 1
    THROW 50000, 'Missing data permissions.', 1;
IF COALESCE(HAS_DBACCESS(N'Project1Db'),0) <> 0 THROW 50000, 'Runtime login can access original database.', 1;
"""


def inspect_schema(runtime, manifest, empty=False):
    sql(runtime_query(), RUNTIME_LOGIN, runtime['PRACTICE_SQL_PASSWORD'])
    if empty:
        sql("IF EXISTS(SELECT 1 FROM sys.tables WHERE is_ms_shipped=0) THROW 50000, 'Initial migration requires an empty database.', 1;",
            RUNTIME_LOGIN, runtime['PRACTICE_SQL_PASSWORD'])
        return
    actual = sql('SELECT MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;',
                 RUNTIME_LOGIN, runtime['PRACTICE_SQL_PASSWORD']).splitlines()
    require(actual == manifest['migration_ids'], 'Applied migrations differ from the release; stop.')
    sql("IF EXISTS(SELECT 1 FROM dbo.EmailOutboxes WHERE Status=N'Pending') THROW 50000, 'Pending mail requires review before startup.', 1;",
        RUNTIME_LOGIN, runtime['PRACTICE_SQL_PASSWORD'])


def initialize(manifest):
    for name in (RUNTIME_FILE, MIGRATION_FILE, STATE_FILE):
        require(not os.path.lexists(ROOT / name), 'Setup/state already exists. Do not overwrite or blindly retry.')
    verify_archive(manifest)
    verify_images(manifest)
    # Bound to loopback, one fixed instance; no remote admin SQL connection is accepted.
    require(sys.stdin.isatty(), 'Use an interactive terminal for hidden password input.')
    password = getpass.getpass('Existing Ubuntu SQL sa password (not saved): ')
    sql(f"""
IF IS_SRVROLEMEMBER(N'sysadmin') <> 1 THROW 50000, 'Administrator required.', 1;
IF LOWER(CONVERT(nvarchar(128),SERVERPROPERTY('MachineName'))) <> N'homelab-server'
    THROW 50000, 'Unexpected SQL host.', 1;
IF DB_ID(N'Project1Db') IS NULL THROW 50000, 'Expected existing server was not found.', 1;
IF DB_ID(N'{DATABASE}') IS NOT NULL OR SUSER_ID(N'{RUNTIME_LOGIN}') IS NOT NULL
    OR SUSER_ID(N'{MIGRATION_LOGIN}') IS NOT NULL THROW 50000, 'Practice resources already exist.', 1;
""", 'sa', password, 'master')
    runtime = {
        'PRACTICE_TAG': manifest['tag'], 'PRACTICE_SQL_PASSWORD': 'Practice!A7' + secrets.token_hex(32),
        'PRACTICE_JWT_KEY': secrets.token_hex(32), 'PRACTICE_DEMO_PASSWORD': 'Demo!A7' + secrets.token_hex(16),
        'PRACTICE_SMTP_HOST': '127.0.0.1', 'PRACTICE_SMTP_PORT': '1', 'PRACTICE_SMTP_SSL': 'false',
        'PRACTICE_SMTP_USERNAME': '', 'PRACTICE_SMTP_PASSWORD': '', 'PRACTICE_SMTP_FROM': 'purchasing@project1.invalid',
    }
    migration_password = 'Practice!A7' + secrets.token_hex(32)
    write_new(ROOT / RUNTIME_FILE, dump_env(runtime))
    write_new(ROOT / MIGRATION_FILE, dump_env({'PRACTICE_MIGRATION_PASSWORD': migration_password}))
    write_new(ROOT / STATE_FILE, json.dumps({'status': 'credentials-prepared', 'database': DATABASE}))
    # Intentionally separate steps: CREATE DATABASE cannot participate in the user transaction.
    # If any step fails, retain files/resources for inspection; NEVER drop or overwrite anything.
    sql(f'CREATE DATABASE [{DATABASE}];', 'sa', password, 'master')
    sql(f"""
CREATE LOGIN [{RUNTIME_LOGIN}] WITH PASSWORD=N'{runtime['PRACTICE_SQL_PASSWORD']}', CHECK_POLICY=ON, CHECK_EXPIRATION=OFF, DEFAULT_DATABASE=[{DATABASE}];
CREATE LOGIN [{MIGRATION_LOGIN}] WITH PASSWORD=N'{migration_password}', CHECK_POLICY=ON, CHECK_EXPIRATION=OFF, DEFAULT_DATABASE=[{DATABASE}];
""", 'sa', password, 'master')
    sql(f"""
SET XACT_ABORT ON;
BEGIN TRANSACTION;
CREATE USER [{RUNTIME_LOGIN}] FOR LOGIN [{RUNTIME_LOGIN}];
ALTER ROLE db_datareader ADD MEMBER [{RUNTIME_LOGIN}];
ALTER ROLE db_datawriter ADD MEMBER [{RUNTIME_LOGIN}];
CREATE USER [{MIGRATION_LOGIN}] FOR LOGIN [{MIGRATION_LOGIN}];
ALTER ROLE db_owner ADD MEMBER [{MIGRATION_LOGIN}];
COMMIT;
""", 'sa', password)
    inspect_schema(runtime, manifest, empty=True)
    sql("IF IS_SRVROLEMEMBER(N'sysadmin') <> 0 OR IS_MEMBER(N'db_owner') <> 1 "
        "OR COALESCE(HAS_DBACCESS(N'Project1Db'),0) <> 0 "
        "THROW 50000, 'Migration account scope is incorrect.', 1;",
        MIGRATION_LOGIN, migration_password)
    replace_private(ROOT / STATE_FILE, json.dumps({'status': 'empty-database-ready', 'database': DATABASE}))
    print('Created only the empty practice database and two dedicated logins. Private files: mode 0600.')
    print('Original API/Nginx/SQL settings/Project1Db unchanged. No migration or email was executed.')


def enable_gmail(runtime, manifest):
    require(not compose(runtime, 'ps', '--status', 'running', '--services'),
            'Stop ONLY the practice stack before enabling external mail.')
    # Refuse enabling real SMTP over any pending queue, including one from failed earlier tests.
    inspect_schema(runtime, manifest)
    require(sys.stdin.isatty(), 'Use an interactive terminal for hidden password input.')
    address = input('Gmail sender address (your account): ').strip()
    password = getpass.getpass('Google App Password (not your ordinary password): ').replace(' ', '')
    runtime.update(PRACTICE_SMTP_HOST='smtp.gmail.com', PRACTICE_SMTP_PORT='587',
                   PRACTICE_SMTP_SSL='true', PRACTICE_SMTP_USERNAME=address,
                   PRACTICE_SMTP_PASSWORD=password, PRACTICE_SMTP_FROM=address)
    validate_runtime(runtime, manifest)
    replace_private(ROOT / RUNTIME_FILE, dump_env(runtime))
    print('Saved Gmail configuration privately; no SMTP authentication or email was attempted.')
    print('Start only the practice api/frontend, then manually create ONE NEW test email to your own address.')
    print('Do not retry old snapshots: their saved sender/content is not rewritten by this setting.')


def main(arguments=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=('archive', 'init', 'check', 'migrate', 'gmail'))
    parser.add_argument('--apply', action='store_true')
    args = parser.parse_args(arguments)
    if args.command in ('init', 'migrate', 'gmail') and not args.apply:
        print(f'Preview only: {args.command} needs --apply. Target is ONLY {DATABASE}. No commands/files/SQL were changed.')
        return 0
    try:
        check_host()
        manifest = load_manifest()
        if args.command == 'archive':
            verify_archive(manifest)
            print('Archive hash matches release.json; ready for a separate manual docker load.')
        elif args.command == 'init':
            initialize(manifest)
        else:
            runtime = read_env(ROOT / RUNTIME_FILE, ENV_KEYS)
            validate_runtime(runtime, manifest)
            verify_images(manifest)
            compose(runtime, 'config', '--quiet')
            if args.command == 'migrate':
                require(not compose(runtime, 'ps', '--status', 'running', '--services'),
                        'Stop ONLY this practice stack before an explicit initial migration.')
                inspect_schema(runtime, manifest, empty=True)
                compose(runtime, '--profile', 'tools', 'run', '--rm', '--no-deps', 'migrate', migration=True, timeout=600)
                inspect_schema(runtime, manifest)
                replace_private(ROOT / STATE_FILE, json.dumps({'status': 'schema-ready', 'database': DATABASE}))
                print('Initial migration verified against the manifest. API was not started; original database was not targeted.')
            elif args.command == 'gmail':
                enable_gmail(runtime, manifest)
            else:
                inspect_schema(runtime, manifest)
                print('Read-only host SQL checks passed: practice DB, restricted runtime login, exact migrations, no Pending mail.')
                print('Host check alone is NOT a container-network, Gmail, PDF or browser test.')
        return 0
    except Stop as error:
        # Stop messages are fixed developer-authored strings, never raw SQL/tool output.
        print(f'STOP: {error} No automatic cleanup/restore was performed.', file=sys.stderr)
        return 1
    except (OSError, ValueError, KeyError, TypeError):
        # No tracebacks: values from configuration/password-bearing batches must never leak.
        print('STOP: validation/command failed. No automatic retry, cleanup, restore or original-service changes. '
              'Check directory/file permissions, loaded release, sudo -v, and SQL access; retain private files/state.', file=sys.stderr)
        return 1


if __name__ == '__main__':
    sys.exit(main())
