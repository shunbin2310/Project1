"""Offline production contracts/state-machine tests. No original env/SQL/SSH/systemctl access."""
import contextlib
import hashlib
import importlib.util
import io
import json
import os
import stat
from pathlib import Path
import subprocess
import tempfile
import types
import unittest
from unittest.mock import Mock, patch

SCRIPT = Path(__file__).resolve().parents[1] / 'ubuntu_production.py'
REPO = SCRIPT.parents[2]
SPEC = importlib.util.spec_from_file_location('ubuntu_production', SCRIPT)
prod = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(prod)
TAG = 'prod-20261019t010203z-01234567'
SHA = 'a' * 64


def manifest():
    return {'format': 1, 'tag': TAG, 'archive_sha256': SHA, 'frontend_sha256': 'b' * 64,
            'migration_ids': ['20261011094217_RemoveProductCicdPracticeNote'],
            'images': {name: {'reference': f'project1-ubuntu-{name}:{TAG}', 'id': 'sha256:' + SHA, 'user': '1654'}
                       for name in ('api', 'frontend')},
            'files_sha256': {name: SHA for name in prod.PUBLIC_FILES}}


def config():
    # Public fake fixture values, not real credentials and never written as live env files.
    return {'PROD_TAG': TAG, 'PROD_SQL_PASSWORD': 'Production!A7' + '0' * 64,
            'PROD_JWT_KEY': '0' * 64, 'PROD_NEW_USER_PASSWORD': 'NewUser!A7' + '0' * 32,
            'PROD_GMAIL_ADDRESS': 'test@gmail.com', 'PROD_GMAIL_APP_PASSWORD': 'abcdefghijklmnop'}


def state(phase='backup-ready'):
    return {'tag': TAG, 'phase': phase, 'old_nginx_sha256': SHA, 'old_nginx_mode': 0o644,
            'old_api_sha256': SHA, 'old_index_sha256': SHA, 'old_unit_sha256': SHA,
            'frozen_counts': {'users': 2, 'products': 3, 'purchase_orders': 0, 'emails': 0},
            'backup': {'path': str(prod.BACKUP_DIR / ('Project1Db-before-docker-' + '0' * 16 + '.bak')),
                       'sha256': SHA, 'bytes': 12345}}


@contextlib.contextmanager
def mocked_runtime():
    """Replace every external side effect. Tests inspect only calls/order and public state."""
    names = ('backup_matches', 'old_stopped', 'old_running', 'old_files', 'site_contract', 'runtime_check', 'no_other_clients',
             'replace_file', 'command', 'systemctl', 'web_check', 'save_state', 'root_file', 'backup_capacity')
    with contextlib.ExitStack() as stack:
        mocks = {name: stack.enter_context(patch.object(prod, name)) for name in names}
        mocks['compose'] = stack.enter_context(patch.object(prod, 'compose', return_value=''))
        mocks['digest'] = stack.enter_context(patch.object(prod, 'digest', return_value=SHA))
        stack.enter_context(patch.object(prod, 'read_config', return_value=config()))
        stack.enter_context(patch.object(prod, 'admin_password', return_value='public-test-admin'))
        stack.enter_context(patch.object(prod, 'db_snapshot', return_value=state()['frozen_counts']))
        stack.enter_context(patch.object(Path, 'read_bytes', return_value=b'public-config-fixture'))
        stack.enter_context(contextlib.redirect_stdout(io.StringIO()))
        yield mocks


class HelperTests(unittest.TestCase):
    def test_mutations_preview_before_host_or_command_access(self):
        for operation in ('stage', 'configure', 'prepare-retry', 'freeze', 'activate', 'finalize', 'rollback'):
            with self.subTest(operation=operation), patch.object(prod, 'check_host') as host, \
                    patch.object(prod, 'command') as cmd, contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(0, prod.main([operation]))
                host.assert_not_called()
                cmd.assert_not_called()

    def test_environment_never_inherits_credentials_or_compose_overrides(self):
        with patch.dict(os.environ, {'PROD_TAG': 'hostile', 'COMPOSE_PROFILES': 'tools',
                                     'SQLCMDPASSWORD': 'do-not-inherit', 'HTTP_PROXY': 'do-not-inherit'}):
            self.assertEqual({'PATH', 'HOME', 'LANG'}, set(prod.safe_environment()))

    def test_retry_tag_rejects_traversal_and_unrelated_commands_before_host_access(self):
        for args in (['archive', '--retry-tag', '../bad'], ['freeze', '--apply', '--retry-tag', TAG],
                     ['prepare-retry', '--apply']):
            with self.subTest(args=args), patch.object(prod, 'check_host') as host, contextlib.redirect_stderr(io.StringIO()):
                self.assertEqual(1, prod.main(args))
                host.assert_not_called()

    def test_prepare_retry_requires_verified_rollback_before_file_or_sql_access(self):
        for phase in ('frozen', 'backup-ready', 'configured', 'active', 'rollback-failed', 'retry-preparing'):
            with self.subTest(phase=phase), patch.object(prod, 'archive_check') as archive, self.assertRaises(prod.Stop):
                prod.prepare_retry(state(phase), Path('/old'), manifest(), TAG)
            archive.assert_not_called()

    @contextlib.contextmanager
    def retry_runtime(self):
        new_tag = 'prod-20261019t120000z-abcdef01'
        new_manifest = manifest()
        new_manifest['tag'] = new_tag
        for name in new_manifest['images']:
            new_manifest['images'][name]['reference'] = f'project1-ubuntu-{name}:{new_tag}'
        with mocked_runtime() as mocks, contextlib.ExitStack() as stack:
            for name in ('directory', 'write_new', 'loaded_images'):
                mocks[name] = stack.enter_context(patch.object(prod, name))
            mocks['archive_check'] = stack.enter_context(patch.object(prod, 'archive_check', return_value=new_manifest))
            mocks['seal_retry_release'] = stack.enter_context(patch.object(prod, 'seal_retry_release', return_value=Path('/new')))
            stack.enter_context(patch.object(prod.os.path, 'lexists', return_value=False))
            yield mocks, new_tag, new_manifest

    def test_prepare_retry_preserves_private_values_archives_old_state_and_requires_fresh_backup(self):
        data = state('rolled-back')
        snapshots = []
        with self.retry_runtime() as (mocks, tag, new_manifest):
            mocks['save_state'].side_effect = lambda value: snapshots.append(json.loads(json.dumps(value)))
            prod.prepare_retry(data, Path('/old'), manifest(), tag)
            self.assertEqual('retry-preparing', snapshots[0]['phase'])
            result = snapshots[-1]
            self.assertEqual('configured', result['phase'])
            self.assertEqual(tag, result['tag'])
            self.assertNotIn('backup', result)
            self.assertNotIn('frozen_counts', result)
            self.assertEqual(TAG, result['attempt_history'][-1]['tag'])
            writes = mocks['write_new'].call_args_list
            self.assertEqual(2, len(writes))
            self.assertEqual(state('rolled-back'), json.loads(writes[0].args[1]))
            self.assertEqual(b'public-config-fixture', writes[1].args[1])
            path, content = mocks['replace_file'].call_args.args
            self.assertEqual(prod.ENV_FILE, path)
            actual = dict(line.split('=', 1) for line in content.decode().splitlines())
            self.assertEqual(dict(config(), PROD_TAG=tag), actual)
            mocks['systemctl'].assert_not_called()
            self.assertFalse(any('up' in call.args[1:] or 'down' in call.args[1:] for call in mocks['compose'].call_args_list))

    def test_retry_rejects_changed_contract_before_mutations(self):
        for name in prod.PUBLIC_FILES - {'ubuntu_production.py'}:
            with self.subTest(name=name), self.retry_runtime() as (mocks, tag, new_manifest):
                new_manifest['files_sha256'][name] = 'c' * 64
                with self.assertRaises(prod.Stop):
                    prod.prepare_retry(state('rolled-back'), Path('/old'), manifest(), tag)
                mocks['write_new'].assert_not_called()
                mocks['save_state'].assert_not_called()

    def test_retry_refuses_running_production_or_bad_database_before_any_write(self):
        for failure in ('runtime_check', 'compose'):
            with self.subTest(failure=failure), self.retry_runtime() as (mocks, tag, _):
                mocks[failure].side_effect = prod.Stop('unsafe state')
                with self.assertRaises(prod.Stop):
                    prod.prepare_retry(state('rolled-back'), Path('/old'), manifest(), tag)
                mocks['write_new'].assert_not_called()

    def test_partial_retry_keeps_explicit_recovery_state_without_service_changes(self):
        snapshots = []
        with self.retry_runtime() as (mocks, tag, _):
            mocks['save_state'].side_effect = lambda value: snapshots.append(json.loads(json.dumps(value)))
            mocks['replace_file'].side_effect = prod.Stop('disk failure')
            with self.assertRaises(prod.Stop):
                prod.prepare_retry(state('rolled-back'), Path('/old'), manifest(), tag)
            self.assertEqual(['retry-preparing'], [item['phase'] for item in snapshots])
            mocks['systemctl'].assert_not_called()

    @contextlib.contextmanager
    def backup_fd_runtime(self, mode=0o660, owner=10001, links=1, kind=stat.S_IFREG):
        info = lambda kind, mode, owner, links: types.SimpleNamespace(
            st_mode=kind | mode, st_uid=owner, st_nlink=links, st_dev=1, st_ino=22)
        with contextlib.ExitStack() as stack:
            for name in ('O_DIRECTORY', 'O_NOFOLLOW', 'O_NONBLOCK'):
                stack.enter_context(patch.object(prod.os, name, 0, create=True))
            stack.enter_context(patch.object(Path, 'resolve', lambda path: path))
            mocks = {name: stack.enter_context(patch.object(prod.os, name)) for name in ('open', 'fstat', 'fchmod', 'stat', 'close')}
            mocks['open'].side_effect = [11, 12]
            mocks['fstat'].side_effect = [info(stat.S_IFDIR, 0o700, 10001, 1), info(kind, mode, owner, links),
                                        info(stat.S_IFREG, 0o600, 10001, 1)]
            mocks['stat'].return_value = info(stat.S_IFREG, 0o600, 10001, 1)
            mocks['regular'] = stack.enter_context(patch.object(prod, 'regular'))
            yield mocks

    def test_new_660_backup_is_secured_by_descriptor_then_strictly_rechecked(self):
        path = prod.BACKUP_DIR / ('Project1Db-before-docker-' + '0' * 16 + '.bak')
        with self.backup_fd_runtime() as mocks:
            prod.secure_new_backup(path, 10001)
            mocks['fchmod'].assert_called_once_with(12, 0o600)
            mocks['regular'].assert_called_once_with(path, uid=10001, private=True)
            self.assertEqual({'dir_fd': 11}, mocks['open'].call_args.kwargs)
            self.assertEqual([12, 11], [call.args[0] for call in mocks['close'].call_args_list])

    def test_unsafe_backup_is_rejected_before_chmod(self):
        path = prod.BACKUP_DIR / ('Project1Db-before-docker-' + '0' * 16 + '.bak')
        for changes in ({'owner': 0}, {'links': 2}, {'kind': stat.S_IFLNK}, {'kind': stat.S_IFIFO}, {'mode': 0o666}, {'mode': 0o4660}):
            with self.subTest(changes=changes), self.backup_fd_runtime(**changes) as mocks:
                with self.assertRaises(prod.Stop):
                    prod.secure_new_backup(path, 10001)
                mocks['fchmod'].assert_not_called()

    def test_backup_name_replacement_is_detected(self):
        path = prod.BACKUP_DIR / ('Project1Db-before-docker-' + '0' * 16 + '.bak')
        with self.backup_fd_runtime() as mocks:
            mocks['stat'].return_value.st_ino = 99
            with self.assertRaises(prod.Stop):
                prod.secure_new_backup(path, 10001)
            mocks['regular'].assert_not_called()

    def test_sql_is_fixed_local_and_password_is_not_a_command_argument(self):
        with patch.object(prod, 'command', return_value='') as command:
            prod.sql('SELECT 1;', prod.LOGIN, 'public-test-password')
        args, kwargs = command.call_args
        self.assertNotIn('public-test-password', args[0])
        self.assertIn('-x', args[0])
        self.assertIn('tcp:127.0.0.1,1433', args[0])
        self.assertIn('Project1Db', args[0])
        self.assertEqual('public-test-password', kwargs['env']['SQLCMDPASSWORD'])

    def test_sql_rejects_practice_or_arbitrary_database_login_and_restore_names(self):
        for database in ('Project1DockerUbuntuPracticeDb', 'other', 'Project1DockerRestore_BAD', "x';DROP DATABASE y"):
            with self.subTest(database=database), patch.object(prod, 'command') as cmd:
                with self.assertRaises(prod.Stop):
                    prod.sql('SELECT 1;', prod.LOGIN, 'test', database)
                cmd.assert_not_called()
        with self.assertRaises(prod.Stop):
            prod.sql('SELECT 1;', 'other-admin', 'test')

    def test_unknown_host_rejected_before_credential_or_file_read(self):
        with patch.object(prod.sys, 'platform', 'win32'), patch.object(prod, 'read_config') as config:
            with self.assertRaises(prod.Stop):
                prod.check_host()
            config.assert_not_called()

    def test_manifest_requires_two_runtime_images_fixed_refs_and_nonroot_uid(self):
        prod.validate_manifest(manifest())
        for key, value in [('reference', 'untrusted/api:latest'), ('user', '0'), ('id', 'not-a-digest')]:
            data = manifest()
            data['images']['api'][key] = value
            with self.subTest(key=key), self.assertRaises(prod.Stop):
                prod.validate_manifest(data)
        data = manifest()
        data['images']['migrate'] = data['images']['api']
        with self.assertRaises(prod.Stop):
            prod.validate_manifest(data)

    def test_manifest_rejects_latest_tags_incomplete_or_duplicate_migrations_and_foreign_files(self):
        for change in ({'tag': 'latest'}, {'migration_ids': []},
                       {'migration_ids': manifest()['migration_ids'] * 2}, {'files_sha256': {'../secret': SHA}}):
            value = manifest()
            value.update(change)
            with self.subTest(change=change), self.assertRaises(prod.Stop):
                prod.validate_manifest(value)

    def test_loaded_image_id_or_user_mismatch_stops(self):
        with patch.object(prod, 'docker', return_value='sha256:' + SHA + '|linux|amd64|0'):
            with self.assertRaises(prod.Stop):
                prod.loaded_images(manifest())

    def test_private_config_validates_generated_passwords_keys_tag_and_google_app_password(self):
        prod.validate_config(config(), TAG)
        for key, value in [('PROD_TAG', 'latest'), ('PROD_SQL_PASSWORD', 'sa-password'),
                           ('PROD_JWT_KEY', 'weak'), ('PROD_GMAIL_ADDRESS', 'not-an-email'),
                           ('PROD_GMAIL_APP_PASSWORD', 'ordinary-password')]:
            data = config()
            data[key] = value
            with self.subTest(key=key), self.assertRaises(prod.Stop):
                prod.validate_config(data, TAG)

    def test_errors_never_echo_password_bearing_sql_or_raw_output(self):
        with patch.object(prod.subprocess, 'run', return_value=subprocess.CompletedProcess([], 1, 'private-data', 'secret-password')):
            with self.assertRaises(prod.Stop) as error:
                prod.command(['test'])
        self.assertNotIn('private-data', str(error.exception))
        self.assertNotIn('secret-password', str(error.exception))

    def test_schema_mismatch_never_attempts_ddl_or_migration(self):
        with patch.object(prod, 'sql', return_value='other-migration') as sql:
            with self.assertRaises(prod.Stop):
                prod.schema_check(manifest(), prod.LOGIN, 'test')
            self.assertEqual(1, sql.call_count)
            self.assertNotIn('CREATE', sql.call_args.args[0])

    def test_login_setup_only_grants_reader_writer_and_creates_no_users_other_than_new_runtime(self):
        with patch.object(prod, 'sql', return_value='') as sql:
            prod.create_runtime_login(config(), 'public-test-admin')
        text = '\n'.join(call.args[0] for call in sql.call_args_list)
        self.assertIn('CREATE LOGIN [project1_docker_production]', text)
        self.assertIn('ALTER ROLE db_datareader', text)
        self.assertIn('ALTER ROLE db_datawriter', text)
        for forbidden in ('db_owner', 'db_ddladmin', 'CREATE DATABASE', 'ALTER USER', 'UPDATE ', 'DROP '):
            self.assertNotIn(forbidden, text)

    def test_runtime_requires_no_admin_or_ddl_and_checks_pending_mail(self):
        with patch.object(prod, 'sql', side_effect=['', manifest()['migration_ids'][0], '', '']) as sql:
            prod.runtime_check(config(), manifest())
        text = '\n'.join(call.args[0] for call in sql.call_args_list)
        for expected in ('db_owner', 'db_ddladmin', 'sysadmin', 'ORIGINAL_LOGIN', 'Pending'):
            self.assertIn(expected, text)

    def test_freeze_requires_explicit_writer_confirmation_before_any_service_changes(self):
        with patch.object(prod, 'systemctl') as service, self.assertRaises(prod.Stop):
            prod.freeze(state('configured'), manifest(), False)
        service.assert_not_called()

    def test_freeze_only_from_configured_state(self):
        with patch.object(prod, 'systemctl') as service, self.assertRaises(prod.Stop):
            prod.freeze(state('backup-ready'), manifest(), True)
        service.assert_not_called()

    def test_freeze_persists_state_before_stop_and_backups_only_after_stop(self):
        data = state('configured')
        events = []
        with mocked_runtime() as mocks, patch.object(prod, 'admin_password', return_value='public-test-admin'), \
                patch.object(prod, 'no_other_clients'), patch.object(prod, 'make_backup', return_value=dict(data['backup'])) as backup, \
                patch.object(prod, 'backup_download', return_value='/public-download-fixture'):
            mocks['systemctl'].side_effect = lambda *a: events.append(a)
            mocks['save_state'].side_effect = lambda s: events.append(('phase', s['phase']))
            backup.side_effect = lambda *a: events.append(('backup',)) or dict(data['backup'])
            prod.freeze(data, manifest(), True)
        self.assertLess(events.index(('phase', 'freezing')), events.index(('stop', prod.SERVICE)))
        self.assertLess(events.index(('stop', prod.SERVICE)), events.index(('backup',)))
        self.assertEqual('backup-ready', data['phase'])

    def test_backup_failure_keeps_frozen_state_and_never_starts_old_or_new_api(self):
        data = state('configured')
        with mocked_runtime() as mocks, patch.object(prod, 'admin_password', return_value='public-test-admin'), \
                patch.object(prod, 'no_other_clients'), patch.object(prod, 'make_backup', side_effect=prod.Stop('backup failed')):
            with self.assertRaises(prod.Stop):
                prod.freeze(data, manifest(), True)
            self.assertEqual('frozen', data['phase'])
            mocks['compose'].assert_not_called()
            self.assertFalse(any(call.args[:1] == ('start',) for call in mocks['systemctl'].call_args_list))

    def test_backup_ack_requires_exact_sha_and_size_before_file_access(self):
        for sha, size in ((None, None), ('b' * 64, 12345), (SHA, 2)):
            with self.subTest(sha=sha, size=size), patch.object(prod, 'digest') as digest, self.assertRaises(prod.Stop):
                prod.backup_matches(state(), sha, size)
            digest.assert_not_called()

    def test_activate_refuses_any_phase_other_than_backup_ready(self):
        with mocked_runtime() as mocks, self.assertRaises(prod.Stop):
            prod.activate(state('configured'), Path('/sealed'), manifest(), SHA, 12345, True)
        mocks['compose'].assert_not_called()

    def test_old_api_must_be_disabled_stopped_before_production_starts(self):
        with mocked_runtime() as mocks:
            mocks['old_stopped'].side_effect = prod.Stop('old API still active')
            with self.assertRaises(prod.Stop):
                prod.activate(state(), Path('/sealed'), manifest(), SHA, 12345, True)
            mocks['compose'].assert_not_called()
            mocks['replace_file'].assert_not_called()

    def test_frontend_health_must_pass_before_nginx_switch(self):
        data = state()
        with mocked_runtime() as mocks:
            mocks['web_check'].side_effect = prod.Stop('candidate unhealthy')
            with self.assertRaises(prod.Stop):
                prod.activate(data, Path('/sealed'), manifest(), SHA, 12345, True)
            self.assertEqual('activation-failed', data['phase'])
            mocks['replace_file'].assert_not_called()
            self.assertFalse(any(call.args[:1] == ('start',) for call in mocks['systemctl'].call_args_list))

    def test_activate_routes_only_after_candidate_check_and_preserves_pending_verification(self):
        data = state()
        events = []
        with mocked_runtime() as mocks:
            mocks['web_check'].side_effect = lambda port, sha: events.append(('health', port))
            mocks['replace_file'].side_effect = lambda *a: events.append(('replace', a[0]))
            prod.activate(data, Path('/sealed'), manifest(), SHA, 12345, True)
            self.assertEqual('active-pending-verification', data['phase'])
            self.assertEqual(prod.OLD_SITE, mocks['replace_file'].call_args.args[0])
            self.assertLess(events.index(('health', 8084)), events.index(('replace', prod.OLD_SITE)))
            self.assertIn(('health', 80), events)

    def test_activation_failure_does_not_automatically_restore_database_or_restart_old_writer(self):
        data = state()
        with mocked_runtime() as mocks:
            mocks['compose'].side_effect = ['', '', prod.Stop('up failed')]
            with self.assertRaises(prod.Stop):
                prod.activate(data, Path('/sealed'), manifest(), SHA, 12345, True)
            self.assertEqual('activation-failed', data['phase'])
            mocks['systemctl'].assert_not_called()

    def test_rollback_requires_writer_confirmation_and_recoverable_state(self):
        with mocked_runtime() as mocks, self.assertRaises(prod.Stop):
            prod.rollback(state('staged'), Path('/sealed'), manifest(), True)
        mocks['compose'].assert_not_called()
        with mocked_runtime() as mocks, self.assertRaises(prod.Stop):
            prod.rollback(state(), Path('/sealed'), manifest(), False)
        mocks['compose'].assert_not_called()

    def test_rollback_never_starts_old_api_if_new_writer_cannot_be_stopped(self):
        data = state('activation-failed')
        with mocked_runtime() as mocks:
            mocks['compose'].side_effect = prod.Stop('Docker unavailable')
            with self.assertRaises(prod.Stop):
                prod.rollback(data, Path('/sealed'), manifest(), True)
            self.assertEqual('rollback-failed', data['phase'])
            mocks['systemctl'].assert_not_called()

    def test_rollback_pending_mail_stops_before_old_api_restart(self):
        data = state('active')
        with mocked_runtime() as mocks:
            mocks['runtime_check'].side_effect = prod.Stop('Pending mail')
            with self.assertRaises(prod.Stop):
                prod.rollback(data, Path('/sealed'), manifest(), True)
            mocks['systemctl'].assert_not_called()

    def test_rollback_restores_exact_snapshot_and_waits_for_new_writer_to_stop(self):
        data = state('active')
        events = []
        with mocked_runtime() as mocks:
            mocks['compose'].side_effect = lambda *a, **k: events.append(('compose',) + a[1:]) or ''
            mocks['systemctl'].side_effect = lambda *a: events.append(('systemctl',) + a)
            prod.rollback(data, Path('/sealed'), manifest(), True)
            self.assertEqual('rolled-back', data['phase'])
            self.assertLess(events.index(('compose', 'down', '--timeout', '45')),
                            events.index(('systemctl', 'start', prod.SERVICE)))
            mocks['site_contract'].assert_called_once_with(test=False)

    def test_rollback_refuses_unrelated_nginx_changes(self):
        with mocked_runtime() as mocks:
            mocks['digest'].return_value = 'c' * 64
            with self.assertRaises(prod.Stop):
                prod.rollback(state('active'), Path('/sealed'), manifest(), True)
            mocks['compose'].assert_not_called()

    def test_finalize_only_after_pending_verification(self):
        with mocked_runtime() as mocks, self.assertRaises(prod.Stop):
            prod.finalize(state('configured'), manifest())
        mocks['systemctl'].assert_not_called()

    def test_finalize_requires_old_disabled_live_site_and_schema(self):
        data = state('active-pending-verification')
        with mocked_runtime() as mocks:
            prod.finalize(data, manifest())
            self.assertEqual('active', data['phase'])
            mocks['old_stopped'].assert_called_once()
            mocks['runtime_check'].assert_called_once()

    def test_old_api_gate_requires_disabled_and_pid_zero_not_merely_no_listener(self):
        for properties in ({'ActiveState': 'active', 'MainPID': '1', 'UnitFileState': 'enabled'},
                           {'ActiveState': 'inactive', 'MainPID': '0', 'UnitFileState': 'enabled'}):
            with patch.object(prod, 'service_properties', return_value=properties), self.assertRaises(prod.Stop):
                prod.old_stopped()

    def test_backup_location_capacity_failure_happens_before_old_service_is_disabled(self):
        with mocked_runtime() as mocks:
            mocks['backup_capacity'].side_effect = prod.Stop('backup directory absent')
            with self.assertRaises(prod.Stop):
                prod.freeze(state('configured'), manifest(), True)
            mocks['systemctl'].assert_not_called()
            mocks['save_state'].assert_not_called()

    def test_activation_requires_fresh_writer_confirmation(self):
        with mocked_runtime() as mocks, self.assertRaises(prod.Stop):
            prod.activate(state(), Path('/sealed'), manifest(), SHA, 12345)
        mocks['backup_matches'].assert_not_called()
        mocks['compose'].assert_not_called()

    def test_reviewed_nginx_contract_allows_comments_whitespace_not_extra_routes(self):
        template = (REPO / 'scripts/docker/nginx/project1-legacy.conf').read_text(encoding='utf-8')
        self.assertEqual(prod.canonical_nginx(template), prod.canonical_nginx(template.replace('    ', '\t')))
        self.assertNotEqual(prod.canonical_nginx(template), prod.canonical_nginx(template.replace('5000', '5001')))

    def test_composition_never_uses_original_or_practice_env(self):
        with patch.object(prod, 'docker', return_value='') as docker:
            prod.compose(Path('/sealed'), 'config', '--quiet')
        args = docker.call_args.args
        self.assertIn(str(prod.ENV_FILE), args)
        self.assertNotIn('/etc/project1/project1.env', args)
        self.assertNotIn('.env.docker.ubuntu-practice.local', args)


class ComposeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        env = dict(os.environ)
        for key in list(env):
            if key.startswith(('COMPOSE_', 'PROD_')):
                del env[key]
        env.update(config())
        cls.env = env
        cls.args = ['docker', 'compose', '--env-file', str(REPO / '.env.docker.ubuntu.example'),
                    '-f', str(REPO / 'compose.ubuntu.yml')]
        result = subprocess.run(cls.args + ['config', '--format', 'json'], env=env, capture_output=True, text=True, check=False)
        if result.returncode:
            raise AssertionError('Public-fixture Compose validation failed (daemon is not needed).')
        cls.config = json.loads(result.stdout)

    def test_exactly_two_nonprivileged_runtime_services_no_migration_server_volume_or_build(self):
        self.assertEqual({'api', 'frontend'}, set(self.config['services']))
        self.assertEqual('project1-ubuntu-production', self.config['name'])
        for service in self.config['services'].values():
            for key in ('build', 'volumes', 'network_mode', 'profiles'):
                self.assertNotIn(key, service)
            self.assertFalse(service.get('privileged', False))
            self.assertEqual('never', service['pull_policy'])
        self.assertNotIn('volumes', self.config)

    def test_original_db_with_dedicated_non_sa_runtime_and_demo_disabled(self):
        env = self.config['services']['api']['environment']
        self.assertIn('Database=Project1Db;', env['ConnectionStrings__DefaultConnection'])
        self.assertIn('User ID=project1_docker_production;', env['ConnectionStrings__DefaultConnection'])
        self.assertNotIn('User ID=sa;', env['ConnectionStrings__DefaultConnection'])
        self.assertEqual('false', env['DemoUsers__Enabled'])
        self.assertEqual('Production', env['ASPNETCORE_ENVIRONMENT'])

    def test_production_health_not_development_openapi(self):
        health = self.config['services']['api']['healthcheck']['test']
        self.assertIn('http://127.0.0.1:8080/api/health', health)
        self.assertNotIn('openapi', json.dumps(health))

    def test_only_loopback_8084_frontend_api_not_host_published(self):
        self.assertNotIn('ports', self.config['services']['api'])
        ports = self.config['services']['frontend']['ports']
        self.assertEqual(1, len(ports))
        self.assertEqual('127.0.0.1', ports[0]['host_ip'])
        self.assertEqual('8084', str(ports[0]['published']))
        self.assertEqual(8080, ports[0]['target'])

    def test_gmail_tls_and_newuser_password_are_runtime_only(self):
        env = self.config['services']['api']['environment']
        self.assertEqual('smtp.gmail.com', env['Email__Smtp__Host'])
        self.assertEqual('587', env['Email__Smtp__Port'])
        self.assertEqual('true', env['Email__Smtp__UseSsl'])
        self.assertIn('DemoUsers__DefaultPassword', env)
        self.assertNotIn('environment', self.config['services']['frontend'])

    def test_blank_example_cannot_start_anything(self):
        env = {k: v for k, v in self.env.items() if not k.startswith('PROD_')}
        result = subprocess.run(self.args + ['config', '--quiet'], env=env, capture_output=True, text=True, check=False)
        self.assertNotEqual(0, result.returncode)


if __name__ == '__main__':
    unittest.main()
