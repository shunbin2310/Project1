"""Offline safety/contract tests. Never connect to a database or read private env files."""
import contextlib
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

SCRIPT = Path(__file__).resolve().parents[1] / 'ubuntu_practice.py'
REPO = SCRIPT.parents[2]
SPEC = importlib.util.spec_from_file_location('ubuntu_practice', SCRIPT)
practice = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(practice)
TAG = 'practice-20261019t010203z-01234567'


def manifest():
    return {'format': 1, 'tag': TAG, 'archive_sha256': hashlib.sha256(b'archive').hexdigest(),
            'migration_ids': ['20261011094217_RemoveProductCicdPracticeNote'],
            'files_sha256': {name: hashlib.sha256(b'public-test-file').hexdigest()
                             for name in ('compose.ubuntu-practice.yml', 'ubuntu_practice.py', '.env.docker.ubuntu-practice.example')},
            'images': {name: {'reference': f'project1-ubuntu-practice-{name}:{TAG}', 'id': 'sha256:' + 'a' * 64}
                       for name in ('api', 'migrate', 'frontend')}}


def runtime():
    # Public, fake test values only; not a deployable credential file.
    return {'PRACTICE_TAG': TAG, 'PRACTICE_SQL_PASSWORD': 'Practice!A7' + '0' * 64,
            'PRACTICE_JWT_KEY': '0' * 64, 'PRACTICE_DEMO_PASSWORD': 'Demo!A7' + '0' * 32,
            'PRACTICE_SMTP_HOST': '127.0.0.1', 'PRACTICE_SMTP_PORT': '1', 'PRACTICE_SMTP_SSL': 'false',
            'PRACTICE_SMTP_USERNAME': '', 'PRACTICE_SMTP_PASSWORD': '',
            'PRACTICE_SMTP_FROM': 'purchasing@project1.invalid'}


class HelperTests(unittest.TestCase):
    def test_mutations_require_apply_before_any_host_or_command_access(self):
        for command in ('init', 'migrate', 'gmail'):
            with self.subTest(command=command), patch.object(practice, 'check_host') as host, \
                    patch.object(practice, 'process') as process, contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(0, practice.main([command]))
                host.assert_not_called()
                process.assert_not_called()

    def test_secret_environment_is_not_inherited(self):
        with patch.dict(os.environ, {'SQLCMDPASSWORD': 'do-not-inherit', 'PRACTICE_TAG': 'bad',
                                     'ANTHROPIC_API_KEY': 'do-not-inherit'}):
            self.assertEqual({'PATH', 'HOME', 'LANG'}, set(practice.environment()))

    def test_sql_has_fixed_local_server_and_no_password_argument_or_sqlcmd_expansion(self):
        with patch.object(practice, 'process', return_value='ok') as process:
            practice.sql('SELECT 1;', practice.RUNTIME_LOGIN, 'fake-test-password')
        args, kwargs = process.call_args
        self.assertNotIn('fake-test-password', args[0])
        self.assertIn('-x', args[0])
        self.assertIn('tcp:127.0.0.1,1433', args[0])
        self.assertEqual('fake-test-password', kwargs['env']['SQLCMDPASSWORD'])
        self.assertIn(practice.DATABASE, args[0])

    def test_sql_refuses_original_or_arbitrary_database_and_login(self):
        for database in ('Project1Db', 'other', 'master;DROP DATABASE x'):
            with self.subTest(database=database), patch.object(practice, 'process') as process:
                with self.assertRaises(practice.Stop):
                    practice.sql('SELECT 1;', practice.RUNTIME_LOGIN, 'test', database)
                process.assert_not_called()
        with self.assertRaises(practice.Stop):
            practice.sql('SELECT 1;', 'unexpected-admin', 'test')

    def test_runtime_settings_reject_wrong_tag_weak_keys_and_injection(self):
        practice.validate_runtime(runtime(), manifest())
        for key, value in [('PRACTICE_TAG', 'latest'), ('PRACTICE_SQL_PASSWORD', "x';DROP DATABASE x;--"),
                           ('PRACTICE_JWT_KEY', ''), ('PRACTICE_DEMO_PASSWORD', 'password'),
                           ('PRACTICE_SMTP_HOST', 'unreviewed-relay')]:
            values = runtime()
            values[key] = value
            with self.subTest(key=key), self.assertRaises(practice.Stop):
                practice.validate_runtime(values, manifest())

    def test_gmail_requires_matching_sender_tls_and_app_password(self):
        values = runtime()
        values.update(PRACTICE_SMTP_HOST='smtp.gmail.com', PRACTICE_SMTP_PORT='587',
                      PRACTICE_SMTP_SSL='true', PRACTICE_SMTP_USERNAME='test@gmail.com',
                      PRACTICE_SMTP_PASSWORD='abcdefghijklmnop', PRACTICE_SMTP_FROM='test@gmail.com')
        practice.validate_runtime(values, manifest())
        for key, value in [('PRACTICE_SMTP_SSL', 'false'), ('PRACTICE_SMTP_FROM', 'other@gmail.com'),
                           ('PRACTICE_SMTP_PASSWORD', 'ordinary-account-password')]:
            invalid = dict(values)
            invalid[key] = value
            with self.subTest(key=key), self.assertRaises(practice.Stop):
                practice.validate_runtime(invalid, manifest())

    def test_env_parser_rejects_duplicate_unexpected_keys_and_shell_expansion(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(practice, 'regular_file'):
            file = Path(directory) / 'public-test.env'
            file.write_text(practice.dump_env(runtime()), encoding='utf-8')
            self.assertEqual(runtime(), practice.read_env(file, practice.ENV_KEYS))
            for text in ('OTHER=1\n', practice.dump_env(runtime()) + 'PRACTICE_TAG=bad\n',
                         practice.dump_env(runtime()).replace('PRACTICE_SMTP_USERNAME=', 'PRACTICE_SMTP_USERNAME=$SECRET')):
                file.write_text(text, encoding='utf-8')
                with self.assertRaises(practice.Stop):
                    practice.read_env(file, practice.ENV_KEYS)

    def test_manifest_rejects_external_images_unsorted_duplicate_or_empty_migrations(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(practice, 'ROOT', Path(directory)), \
                patch.object(practice, 'regular_file'):
            file = Path(directory) / 'release.json'
            for name in manifest()['files_sha256']:
                (Path(directory) / name).write_bytes(b'public-test-file')
            file.write_text(json.dumps(manifest()), encoding='utf-8')
            self.assertEqual(manifest(), practice.load_manifest())
            invalids = []
            for value in ([], ['not-a-migration'], manifest()['migration_ids'] * 2):
                data = manifest()
                data['migration_ids'] = value
                invalids.append(data)
            data = manifest()
            data['images']['api']['reference'] = 'untrusted/api:latest'
            invalids.append(data)
            for data in invalids:
                file.write_text(json.dumps(data), encoding='utf-8')
                with self.assertRaises(practice.Stop):
                    practice.load_manifest()

    def test_changed_release_config_or_helper_refuses_setup(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(practice, 'ROOT', Path(directory)), \
                patch.object(practice, 'regular_file'):
            (Path(directory) / 'release.json').write_text(json.dumps(manifest()), encoding='utf-8')
            for name in manifest()['files_sha256']:
                (Path(directory) / name).write_bytes(b'public-test-file')
            (Path(directory) / practice.COMPOSE).write_bytes(b'accidentally-edited-database-target')
            with self.assertRaises(practice.Stop):
                practice.load_manifest()

    def test_image_check_rejects_wrong_digest_or_platform(self):
        for actual in ('sha256:' + 'b' * 64 + '|linux|amd64', 'sha256:' + 'a' * 64 + '|linux|arm64'):
            with self.subTest(actual=actual), patch.object(practice, 'docker', return_value=actual), self.assertRaises(practice.Stop):
                practice.verify_images(manifest())

    def test_archive_mismatch_stops_before_load(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(practice, 'ROOT', Path(directory)), \
                patch.object(practice, 'regular_file'):
            archive = Path(directory) / 'images.tar'
            archive.write_bytes(b'archive')
            practice.verify_archive(manifest())
            archive.write_bytes(b'corrupt')
            with self.assertRaises(practice.Stop):
                practice.verify_archive(manifest())

    def test_init_refuses_existing_private_state_before_password_or_sql(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(practice, 'ROOT', Path(directory)), \
                patch.object(practice, 'sql') as sql, patch.object(practice.getpass, 'getpass') as password:
            (Path(directory) / practice.STATE_FILE).write_text('{}', encoding='utf-8')
            with self.assertRaises(practice.Stop):
                practice.initialize(manifest())
            sql.assert_not_called()
            password.assert_not_called()

    def test_successful_init_only_creates_new_scoped_resources_and_hides_passwords(self):
        output = io.StringIO()
        with tempfile.TemporaryDirectory() as directory, patch.object(practice, 'ROOT', Path(directory)), \
                patch.object(practice, 'verify_archive'), patch.object(practice, 'verify_images'), \
                patch.object(practice.sys.stdin, 'isatty', return_value=True), \
                patch.object(practice.getpass, 'getpass', return_value='fake-admin-password'), \
                patch.object(practice, 'sql', return_value='') as sql, \
                patch.object(practice, 'write_new') as write, patch.object(practice, 'replace_private') as replace, \
                contextlib.redirect_stdout(output):
            practice.initialize(manifest())
        batches = '\n'.join(call.args[0] for call in sql.call_args_list)
        self.assertIn('CREATE DATABASE [Project1DockerUbuntuPracticeDb]', batches)
        self.assertNotIn('DROP ', batches)
        self.assertNotIn('USE [Project1Db]', batches)
        self.assertNotIn('ALTER DATABASE [Project1Db]', batches)
        self.assertIn('ALTER ROLE db_datawriter ADD MEMBER [project1_ubuntu_practice]', batches)
        self.assertIn('ALTER ROLE db_owner ADD MEMBER [project1_ubuntu_practice_migrator]', batches)
        self.assertNotIn('ALTER ROLE db_owner ADD MEMBER [project1_ubuntu_practice];', batches)
        self.assertEqual(3, write.call_count)
        self.assertEqual(1, replace.call_count)
        self.assertNotIn('fake-admin-password', output.getvalue())
        for call in write.call_args_list:
            if call.args[0].name == practice.RUNTIME_FILE:
                for line in call.args[1].splitlines():
                    key, _, value = line.partition('=')
                    if 'PASSWORD' in key or 'JWT_KEY' in key:
                        if value:
                            self.assertNotIn(value, output.getvalue())

    def test_admin_precheck_failure_writes_nothing(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(practice, 'ROOT', Path(directory)), \
                patch.object(practice, 'verify_archive'), patch.object(practice, 'verify_images'), \
                patch.object(practice.sys.stdin, 'isatty', return_value=True), \
                patch.object(practice.getpass, 'getpass', return_value='fake-admin-password'), \
                patch.object(practice, 'sql', side_effect=practice.Stop('precheck failed')), \
                patch.object(practice, 'write_new') as write:
            with self.assertRaises(practice.Stop):
                practice.initialize(manifest())
            write.assert_not_called()

    def test_partial_setup_failure_preserves_private_state_and_never_drops_resources(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(practice, 'ROOT', Path(directory)), \
                patch.object(practice, 'verify_archive'), patch.object(practice, 'verify_images'), \
                patch.object(practice.sys.stdin, 'isatty', return_value=True), \
                patch.object(practice.getpass, 'getpass', return_value='fake-admin-password'), \
                patch.object(practice, 'sql', side_effect=['', '', practice.Stop('create login failed')]) as sql, \
                patch.object(practice, 'write_new') as write, patch.object(practice, 'replace_private') as replace:
            with self.assertRaises(practice.Stop):
                practice.initialize(manifest())
            self.assertEqual(3, write.call_count)
            replace.assert_not_called()
            self.assertNotIn('DROP ', '\n'.join(call.args[0] for call in sql.call_args_list))

    def test_migrate_refuses_running_stack_before_any_sql(self):
        with patch.object(practice, 'check_host'), patch.object(practice, 'load_manifest', return_value=manifest()), \
                patch.object(practice, 'read_env', return_value=runtime()), patch.object(practice, 'verify_images'), \
                patch.object(practice, 'compose', side_effect=['', 'api']) as compose, \
                patch.object(practice, 'sql') as sql, contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(1, practice.main(['migrate', '--apply']))
            sql.assert_not_called()
            self.assertEqual(2, compose.call_count)

    def test_migrate_empty_guard_failure_never_starts_migration_container(self):
        with patch.object(practice, 'check_host'), patch.object(practice, 'load_manifest', return_value=manifest()), \
                patch.object(practice, 'read_env', return_value=runtime()), patch.object(practice, 'verify_images'), \
                patch.object(practice, 'compose', return_value='') as compose, \
                patch.object(practice, 'inspect_schema', side_effect=practice.Stop('nonempty DB')), \
                contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(1, practice.main(['migrate', '--apply']))
            self.assertFalse(any('run' in call.args for call in compose.call_args_list))

    def test_gmail_queue_failure_never_prompts_or_rewrites_credentials(self):
        with patch.object(practice, 'compose', return_value=''), \
                patch.object(practice, 'inspect_schema', side_effect=practice.Stop('Pending mail')), \
                patch('builtins.input') as prompt, patch.object(practice, 'replace_private') as replace:
            with self.assertRaises(practice.Stop):
                practice.enable_gmail(runtime(), manifest())
            prompt.assert_not_called()
            replace.assert_not_called()

    def test_unsupported_host_is_rejected_before_any_file_access(self):
        with patch.object(practice.sys, 'platform', 'win32'), patch.object(practice, 'regular_file') as file:
            with self.assertRaises(practice.Stop):
                practice.check_host()
            file.assert_not_called()

    def test_initial_migration_checks_empty_database(self):
        with patch.object(practice, 'sql', return_value='') as sql:
            practice.inspect_schema(runtime(), manifest(), empty=True)
        self.assertIn('sys.tables', sql.call_args_list[-1].args[0])
        self.assertIn('empty database', sql.call_args_list[-1].args[0])

    def test_applied_migration_mismatch_stops_before_mail_queue_check(self):
        with patch.object(practice, 'sql', side_effect=['', 'old_migration']) as sql:
            with self.assertRaises(practice.Stop):
                practice.inspect_schema(runtime(), manifest())
            self.assertEqual(2, sql.call_count)

    def test_exact_migrations_and_pending_queue_are_verified(self):
        with patch.object(practice, 'sql', side_effect=['', manifest()['migration_ids'][0], '']) as sql:
            practice.inspect_schema(runtime(), manifest())
        self.assertIn('Pending', sql.call_args_list[-1].args[0])

    def test_runtime_checks_original_db_access_and_no_ddl(self):
        query = practice.runtime_query()
        for text in ('HAS_DBACCESS', 'Project1Db', 'db_owner', 'db_ddladmin', 'db_datareader', 'db_datawriter', 'sysadmin'):
            self.assertIn(text, query)
        self.assertNotIn('USE [Project1Db]', query)

    def test_gmail_refuses_running_stack_without_prompting(self):
        with patch.object(practice, 'compose', return_value='api'), patch('builtins.input') as prompt:
            with self.assertRaises(practice.Stop):
                practice.enable_gmail(runtime(), manifest())
            prompt.assert_not_called()

    def test_process_error_does_not_expose_password_bearing_stderr(self):
        result = subprocess.CompletedProcess([], 1, 'private-output', 'password=very-secret')
        with patch.object(practice.subprocess, 'run', return_value=result):
            with self.assertRaises(practice.Stop) as error:
                practice.process(['fake'])
            self.assertNotIn('very-secret', str(error.exception))
            self.assertNotIn('private-output', str(error.exception))


class ComposeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.config_file = REPO / 'compose.ubuntu-practice.yml'
        cls.env_file = REPO / '.env.docker.ubuntu-practice.example'
        # Explicit PUBLIC example plus public dummy variables, never a populated env file.
        cls.env = dict(os.environ)
        for key in list(cls.env):
            if key.startswith(('COMPOSE_', 'PRACTICE_')):
                del cls.env[key]
        cls.env.update(runtime())
        command = ['docker', 'compose', '--env-file', str(cls.env_file), '-f', str(cls.config_file),
                   '--profile', 'tools', 'config', '--format', 'json']
        result = subprocess.run(command, capture_output=True, text=True, env=cls.env, check=False)
        if result.returncode:
            raise AssertionError('Compose plugin/config unavailable; no Docker daemon is required for this validation.')
        cls.config = json.loads(result.stdout)

    def test_only_two_application_services_and_explicit_one_off_migration(self):
        self.assertEqual({'api', 'frontend', 'migrate'}, set(self.config['services']))
        self.assertEqual(['tools'], self.config['services']['migrate']['profiles'])
        self.assertNotIn('volumes', self.config)
        self.assertNotIn('ports', self.config['services']['api'])
        self.assertNotIn('build', self.config['services']['api'])

    def test_web_binds_only_loopback_8083(self):
        port = self.config['services']['frontend']['ports'][0]
        self.assertEqual('127.0.0.1', port['host_ip'])
        self.assertEqual('8083', str(port['published']))
        self.assertEqual(8080, port['target'])

    def test_fixed_practice_db_and_separate_login_no_original_file_mounts(self):
        for name, login in [('api', practice.RUNTIME_LOGIN), ('migrate', practice.MIGRATION_LOGIN)]:
            service = self.config['services'][name]
            connection = service['environment']['ConnectionStrings__DefaultConnection']
            self.assertIn('Database=' + practice.DATABASE + ';', connection)
            self.assertIn('User ID=' + login + ';', connection)
            self.assertNotIn('User ID=sa;', connection)
            self.assertNotIn('volumes', service)
            self.assertIn('host.docker.internal=host-gateway', service['extra_hosts'])
            self.assertEqual('never', service['pull_policy'])

    def test_runtime_does_not_receive_migration_password(self):
        for service in ('api', 'frontend'):
            text = json.dumps(self.config['services'][service])
            self.assertNotIn('project1_ubuntu_practice_migrator', text)
            self.assertNotIn('PRACTICE_MIGRATION_PASSWORD', text)

    def test_migration_does_not_receive_gmail_demo_or_runtime_credentials(self):
        env = self.config['services']['migrate']['environment']
        self.assertEqual({'ASPNETCORE_ENVIRONMENT', 'ConnectionStrings__DefaultConnection', 'Jwt__SigningKey'}, set(env))
        self.assertIn('UbuntuPracticeMigration', env['ASPNETCORE_ENVIRONMENT'])
        # Its password remains empty unless the separate privileged env is explicitly provided.
        self.assertIn('Password=;', env['ConnectionStrings__DefaultConnection'])

    def test_initial_email_is_fail_closed(self):
        env = self.config['services']['api']['environment']
        self.assertEqual('127.0.0.1', env['Email__Smtp__Host'])
        self.assertEqual('1', env['Email__Smtp__Port'])
        self.assertEqual('', env['Email__Smtp__Password'])

    def test_no_host_network_privileged_mode_or_original_ports(self):
        for service in self.config['services'].values():
            self.assertNotIn('network_mode', service)
            self.assertFalse(service.get('privileged', False))
            self.assertNotIn('docker.sock', json.dumps(service))

    def test_blank_example_cannot_accidentally_start_stack(self):
        env = {k: v for k, v in self.env.items() if not k.startswith('PRACTICE_')}
        result = subprocess.run(['docker', 'compose', '--env-file', str(self.env_file), '-f', str(self.config_file),
                                 'config', '--quiet'], env=env, capture_output=True, text=True, check=False)
        self.assertNotEqual(0, result.returncode)


if __name__ == '__main__':
    unittest.main()
