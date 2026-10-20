"""Offline contracts; root-only Linux cases use temporary files and fake Docker/HTTP."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[3]
SCRIPT = ROOT / 'scripts/deploy/auto-deploy.sh'
SHA = 'a' * 40
API = 'sha256:' + 'b' * 64
FRONTEND = 'sha256:' + 'c' * 64


class ConfigurationTests(unittest.TestCase):
    def test_only_one_workflow_remains_and_actions_are_pinned(self):
        self.assertEqual(sorted(p.name for p in (ROOT / '.github/workflows').glob('*.yml')), ['ci.yml'])
        workflow = (ROOT / '.github/workflows/ci.yml').read_text(encoding='utf-8')
        for line in workflow.splitlines():
            if 'uses:' in line:
                self.assertRegex(line, r'uses: [A-Za-z0-9_./-]+@[0-9a-f]{40}')
        self.assertIn('cancel-in-progress: false', workflow)

    def test_registry_and_deploy_require_main_push_and_setup_switch(self):
        workflow = (ROOT / '.github/workflows/ci.yml').read_text(encoding='utf-8')
        for job in ('images:', 'deploy:'):
            block = workflow.split('  ' + job, 1)[1]
            condition = next(line for line in block.splitlines() if line.strip().startswith('if:'))
            self.assertIn("github.event_name == 'push'", condition)
            self.assertIn("github.ref == 'refs/heads/main'", condition)
        self.assertIn("vars.DOCKER_DEPLOY_ENABLED == 'true'", workflow)
        self.assertIn('needs: [backend, frontend]', workflow)
        self.assertIn('needs: images', workflow)

    def test_remote_command_uses_digests_and_pinned_ssh(self):
        workflow = (ROOT / '.github/workflows/ci.yml').read_text(encoding='utf-8')
        self.assertIn('StrictHostKeyChecking=yes', workflow)
        self.assertIn('steps.api.outputs.digest', workflow)
        self.assertIn('steps.frontend.outputs.digest', workflow)
        self.assertIn('git/ref/heads/main', workflow)
        self.assertIn('project1-auto-deploy $GITHUB_SHA $API_DIGEST $FRONTEND_DIGEST', workflow)
        self.assertNotIn('scp ', workflow)
        self.assertNotIn('images.tar', workflow)

    def test_compose_preserves_host_database_and_disables_migration_retry(self):
        compose = (ROOT / 'compose.ubuntu.yml').read_text(encoding='utf-8')
        self.assertIn('Database=Project1Db;', compose)
        self.assertIn('Database__MigrateOnStartup: "true"', compose)
        self.assertIn('Database__ExpectedDatabase: Project1Db', compose)
        self.assertIn('restart: "no"', compose.split('  frontend:', 1)[0])
        self.assertIn('127.0.0.1:8084:8080', compose)
        self.assertNotIn('  sqlserver:', compose)
        self.assertNotIn('  migrate:', compose)

    def test_sudo_allows_only_fixed_script(self):
        rules = (ROOT / 'scripts/deploy/project1-auto-deploy.sudoers').read_text()
        active = [s for s in rules.splitlines() if s and not s.startswith('#')]
        self.assertEqual(active, ['project1_deploy ALL=(root) NOPASSWD: NOSETENV: /usr/local/sbin/project1-auto-deploy'])

    def test_old_release_tools_are_retired_but_migrations_are_preserved(self):
        for name in ('project1_deploy.py', 'project1_docker_update.py', 'docker_release.py',
                     'install_docker_cd_tools.sh', 'migration_package.py', 'startup-migration-baseline.json'):
            self.assertFalse((ROOT / 'scripts/deploy' / name).exists(), name)
        self.assertTrue((ROOT / 'backend/Project1.Api/Data/StartupDatabaseMigration.cs').is_file())
        self.assertTrue((ROOT / 'backend/Project1.Api/Migrations/AppDbContextModelSnapshot.cs').is_file())
        self.assertTrue((ROOT / 'compose.host-sql.yml').is_file())
        self.assertTrue((ROOT / 'compose.local.yml').is_file())

    def test_script_does_not_migrate_restore_or_prune_directly(self):
        script = SCRIPT.read_text()
        for command in ('dotnet ef', 'database update', 'DROP DATABASE', 'system prune', 'systemctl ', 'nginx '):
            self.assertNotIn(command, script)
        self.assertIn('ghcr.io/shunbin2310/project1-api@$2', script)
        self.assertIn('needs-attention', script)


@unittest.skipUnless(os.name == 'posix' and hasattr(os, 'geteuid') and os.geteuid() == 0,
                     'Temporary root-owned fixture tests run in disposable Linux CI, never on Ubuntu production.')
class ShellDeploymentTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix='project1-auto-tests-')
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.bin = self.root / 'bin'
        self.bin.mkdir()
        # Model the production parent too: GitHub runners make their real /opt
        # writable. Never depend on or chmod that shared runner directory.
        self.opt = self.root / 'opt'
        self.opt.mkdir(mode=0o755)
        self.app = self.opt / 'project1-auto'
        self.app.mkdir(mode=0o700)
        (self.app / '.env').write_text('PUBLIC_FIXTURE_ONLY=yes\n')
        (self.app / '.env').chmod(0o600)
        (self.app / 'compose.yml').write_text('services: {}\n')
        self.log = self.root / 'commands.log'
        self.log.touch()
        self.env = dict(os.environ, TEST_LOG=str(self.log),
                        PATH=str(self.bin) + ':/usr/sbin:/usr/bin:/sbin:/bin')
        self.write_command('hostname', '#!/bin/bash\necho homelab-server\n')
        self.write_command('docker', '''#!/bin/bash
echo "$*" >> "$TEST_LOG"
if [[ $1 == ps ]]; then
    if [[ ${OLD_RUNNING:-0} == 1 ]]; then echo old-api; fi
elif [[ $1 == update ]]; then
    [[ ${FAIL_STAGE:-} != update ]] || exit 12
elif [[ $1 == compose ]]; then
    [[ ! ( "$*" == *" pull" && ${FAIL_STAGE:-} == pull ) ]] || exit 12
    [[ ! ( "$*" == *" up "* && ${FAIL_STAGE:-} == up ) ]] || exit 12
    if [[ "$*" == *" ps -q api" ]]; then echo fixture-api; fi
else
    exit 99
fi
''')
        self.write_command('curl', '#!/bin/bash\necho curl >> "$TEST_LOG"\n[[ ${FAIL_STAGE:-} != curl ]]\n')
        body = SCRIPT.read_text().replace('APP=/opt/project1-auto', 'APP=' + str(self.app))
        parent_guard = 'for directory in / /opt "$APP"; do'
        self.assertEqual(body.count(parent_guard), 1, 'Update the isolated parent fixture when the production guard changes.')
        body = body.replace(parent_guard, f'for directory in / "{self.opt}" "$APP"; do')
        body = body.replace('export PATH=/usr/sbin:/usr/bin:/sbin:/bin', 'export PATH=' + str(self.bin) + ':/usr/sbin:/usr/bin:/sbin:/bin')
        self.script = self.root / 'deploy.sh'
        self.script.write_text(body)

    def write_command(self, name, body):
        path = self.bin / name
        path.write_text(body)
        path.chmod(0o755)

    def run_deploy(self, *arguments):
        return subprocess.run(['/bin/bash', str(self.script), *(arguments or (SHA, API, FRONTEND))],
                              env=self.env, text=True, capture_output=True, timeout=15)

    def test_success_pulls_before_stopping_and_records_exact_images(self):
        result = self.run_deploy()
        self.assertEqual(result.returncode, 0, result.stderr)
        log = self.log.read_text()
        self.assertLess(log.index(' pull'), log.index(' stop api'))
        self.assertLess(log.index(' stop api'), log.index(' up '))
        self.assertIn(' up -d --force-recreate --no-build', log)
        self.assertLess(log.index('curl'), log.index('update --restart unless-stopped'))
        self.assertFalse((self.app / 'needs-attention').exists())
        release = (self.app / 'release.env').read_text()
        self.assertIn('COMMIT_SHA=' + SHA, release)
        self.assertIn('project1-api@' + API, release)

    def test_next_success_retains_previous_version(self):
        self.assertEqual(self.run_deploy().returncode, 0)
        previous = (self.app / 'release.env').read_bytes()
        self.assertEqual(self.run_deploy('d' * 40, API, FRONTEND).returncode, 0)
        self.assertEqual((self.app / 'previous-release.env').read_bytes(), previous)

    def test_pull_failure_does_not_stop_or_mark_existing_app(self):
        self.env['FAIL_STAGE'] = 'pull'
        self.assertNotEqual(self.run_deploy().returncode, 0)
        self.assertIn(' pull', self.log.read_text())
        self.assertNotIn(' stop ', self.log.read_text())
        self.assertFalse((self.app / 'needs-attention').exists())

    def test_start_failure_stops_candidate_retains_marker_and_refuses_retry(self):
        self.env['FAIL_STAGE'] = 'up'
        self.assertNotEqual(self.run_deploy().returncode, 0)
        self.assertTrue((self.app / 'needs-attention').exists())
        self.assertEqual(self.log.read_text().count(' stop api'), 2)
        before = self.log.read_bytes()
        self.assertNotEqual(self.run_deploy().returncode, 0)
        self.assertEqual(self.log.read_bytes(), before)

    def test_failed_http_check_never_enables_restart(self):
        self.env['FAIL_STAGE'] = 'curl'
        self.assertNotEqual(self.run_deploy().returncode, 0)
        self.assertTrue((self.app / 'needs-attention').exists())
        self.assertNotIn('update --restart', self.log.read_text())

    def test_old_deployment_is_refused_before_pull_or_migration(self):
        self.env['OLD_RUNNING'] = '1'
        result = self.run_deploy()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('Old Docker deployment is running', result.stderr)
        self.assertNotIn(' pull', self.log.read_text())
        self.assertFalse((self.app / 'needs-attention').exists())

    def test_invalid_digest_is_rejected_without_docker_calls(self):
        self.assertEqual(self.run_deploy(SHA, API + '; echo bad', FRONTEND).returncode, 2)
        self.assertEqual(self.log.read_text(), '')

    def test_readable_secret_file_is_rejected_before_docker_calls(self):
        (self.app / '.env').chmod(0o644)
        self.assertNotEqual(self.run_deploy().returncode, 0)
        self.assertEqual(self.log.read_text(), '')

    def test_writable_parent_is_rejected_before_docker_calls(self):
        for mode in (0o775, 0o777):
            with self.subTest(mode=oct(mode)):
                self.opt.chmod(mode)
                try:
                    self.assertNotEqual(self.run_deploy().returncode, 0)
                    self.assertEqual(self.log.read_text(), '')
                    self.assertFalse((self.app / 'needs-attention').exists())
                finally:
                    self.opt.chmod(0o755)

    def test_symlink_secret_file_is_rejected(self):
        path = self.app / '.env'
        path.rename(self.app / 'real.env')
        path.symlink_to(self.app / 'real.env')
        self.assertNotEqual(self.run_deploy().returncode, 0)
        self.assertEqual(self.log.read_text(), '')

    def test_concurrent_deploy_is_refused_without_docker_calls(self):
        import fcntl
        with (self.app / 'deploy.lock').open('w') as lock:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            self.assertNotEqual(self.run_deploy().returncode, 0)
        self.assertEqual(self.log.read_text(), '')


if __name__ == '__main__':
    unittest.main()
