"""Offline adapter/transport tests: temporary fixtures, no production access."""
from contextlib import contextmanager
import copy
import hashlib
import io
import json
import os
from pathlib import Path
import stat
import subprocess
import sys
from types import SimpleNamespace
import unittest
from unittest import mock

from test_migration_approval_ledger import FixtureCase
from test_migration_approval import NOW
from test_migration_precheck import FakeClient
import production_migration as production
import execute_migration_verified as runner
import production_entry as entrypoint
from migration_approval_ledger import canonical, empty_state, register_record


class AdapterTests(FixtureCase):
    def setUp(self):
        super().setUp()
        status = json.loads(self.status)
        status["login"] = production.TARGET.login
        self.status = canonical(status)
        self.state = register_record(empty_state(), canonical(self.record), self.manifest,
                                    self.status, now=NOW, target=production.TARGET)
        self.store = mock.Mock(target=production.TARGET)
        self.store.inspect.return_value = self.state

    def test_online_source_checks_attempt_digest_and_run_again_after_artifact_metadata(self):
        client = FakeClient()
        production.online_check(client, self.manifest, NOW)
        for mutate in (
            lambda c: c.run.update(run_attempt=2),
            lambda c: c.run.update(event="pull_request"),
            lambda c: c.run.update(conclusion="failure"),
            lambda c: c.item.update(digest="sha256:" + "e" * 64),
            lambda c: c.item.update(expired=True),
            lambda c: c.item["workflow_run"].update(head_branch="other"),
        ):
            client = FakeClient()
            mutate(client)
            with self.subTest(mutate=mutate), self.assertRaises(Exception):
                production.online_check(client, self.manifest, NOW)
        client = FakeClient()
        original = client.artifacts
        def artifacts(identifier):
            result = original(identifier)
            client.run["run_attempt"] = 2
            return result
        client.artifacts = artifacts
        with self.assertRaises(Exception):
            production.online_check(client, self.manifest, NOW)

    def test_private_package_is_downloaded_again_and_bound_to_approved_sql(self):
        package = production.approved_package(self.record, FakeClient(), self.root / "new")
        self.assertEqual(package.manifest, self.manifest)
        changed = {**self.record, "sql_sha256": "e" * 64}
        with self.assertRaises(production.ProductionError):
            production.approved_package(changed, FakeClient(), self.root / "changed")

    def test_describe_only_reads_immutable_registered_binding_without_sql_or_network(self):
        with mock.patch.object(production, "require_installation"), mock.patch.object(production, "ApprovalLedger", return_value=self.store), \
                mock.patch.object(production, "private_read", side_effect=AssertionError("No credentials")), \
                mock.patch.object(production, "HostSession", side_effect=AssertionError("No SQL")):
            result = production.operate("describe", self.identifier)
        self.assertEqual(result["sql_sha256"], self.manifest["sql_sha256"])
        self.assertFalse(result["sql_executed"])

    def test_consumed_and_blocked_approvals_stop_before_download_or_sql(self):
        for phase in ("claimed", "failed", "interrupted", "succeeded"):
            self.store.inspect.return_value = copy.deepcopy(self.state)
            self.store.inspect.return_value["entries"][self.identifier]["phase"] = phase
            with mock.patch.object(production, "require_installation"), mock.patch.object(production, "ApprovalLedger", return_value=self.store), \
                    mock.patch.object(production, "private_read", side_effect=AssertionError("No credentials")), \
                    self.assertRaises(production.ProductionError):
                production.operate("execute", self.identifier)

    def test_service_requires_locked_session_before_independent_backup_verification(self):
        services = production.ProductionServices(None, None)
        with self.assertRaises(production.ProductionError):
            services.backup_evidence(self.record)

    def test_registration_rechecks_schema_backup_and_live_status_before_persisting(self):
        events = []
        package = production.VerifiedSql.from_directory(self.root / "verified")
        session = mock.Mock(spec=["assert_before", "status", "apply"])
        session.assert_before.side_effect = lambda: events.append("schema")
        session.status.side_effect = lambda: (events.append("status"), self.status)[1]
        evidence = {"server": production.TARGET.server, "database": production.TARGET.database,
                    "sha256": self.record["backup"]["sha256"], "completed_at": self.record["backup"]["completed_at"],
                    "checked_at": NOW.isoformat(), "checksum_valid": True, "copy_only": True, "restore_verified": True}
        @contextmanager
        def locked(_):
            events.append("locked")
            yield session
            events.append("closed")
        self.store.inspect.return_value = empty_state()
        self.store.register.side_effect = lambda *a, **k: events.append("registered")
        def reader(path, limit):
            return canonical(self.record) if path.suffix == ".json" else b"fake-token\n"
        with mock.patch.object(production, "require_installation"), mock.patch.object(production, "trusted"), \
                mock.patch.object(production, "private_read", side_effect=reader), \
                mock.patch.object(production, "ApprovalLedger", return_value=self.store), \
                mock.patch.object(production, "GitHubClient", return_value=FakeClient()), \
                mock.patch.object(production, "approved_package", return_value=package), \
                mock.patch.object(production.tempfile, "TemporaryDirectory", return_value=mock.MagicMock(__enter__=lambda _: str(self.root))), \
                mock.patch.object(production.ProductionServices, "now", return_value=NOW), \
                mock.patch.object(production.ProductionServices, "session", locked), \
                mock.patch.object(production.ProductionServices, "backup_evidence", side_effect=lambda _: (events.append("backup"), evidence)[1]):
            result = production.operate("register", self.identifier)
        self.assertFalse(result["sql_executed"])
        self.assertEqual(events, ["locked", "schema", "backup", "status", "registered", "closed"])
        session.apply.assert_not_called()

    def test_trusted_paths_reject_symlinks_group_writes_hardlinks_and_foreign_owners(self):
        def info(mode=stat.S_IFREG | 0o600, uid=0, links=1):
            return SimpleNamespace(st_mode=mode, st_uid=uid, st_nlink=links)
        for invalid in (info(uid=1001), info(stat.S_IFLNK | 0o777), info(stat.S_IFREG | 0o620), info(links=2)):
            def lstat(path):
                return invalid if path == Path("/root/test") else info(stat.S_IFDIR | 0o700)
            with mock.patch.object(Path, "lstat", lstat), self.assertRaises(production.ProductionError):
                production.trusted(Path("/root/test"), 0o600)

    def test_backup_hash_is_observed_before_and_after_sql_verification(self):
        path = self.root / (hashlib.sha256(b"backup").hexdigest() + ".bak")
        path.write_bytes(b"backup")
        initial = SimpleNamespace(st_uid=0, st_gid=7, st_nlink=1, st_mode=stat.S_IFREG | 0o640,
                                  st_size=6, st_dev=1, st_ino=2, st_mtime_ns=0, st_ctime_ns=0)
        directory = SimpleNamespace(st_gid=7)
        # Adapt only test ownership. Keep real reads, hashing and final digest checks.
        fake_grp = SimpleNamespace(getgrnam=lambda _: SimpleNamespace(gr_gid=7))
        with mock.patch.object(production, "BACKUPS", self.root), mock.patch.object(production, "trusted"), \
                mock.patch.dict("sys.modules", {"grp": fake_grp}), \
                mock.patch.object(Path, "stat", return_value=directory), mock.patch.object(Path, "lstat", return_value=initial), \
                mock.patch.object(os, "fstat", return_value=initial), mock.patch.object(os, "O_NOFOLLOW", 0, create=True), \
                mock.patch.object(os, "O_NONBLOCK", 0, create=True):
            with production.verified_backup_file(path.stem):
                pass
            with self.assertRaises(production.ProductionError):
                with production.verified_backup_file(path.stem):
                    path.write_bytes(b"broken")


class TransportTests(FixtureCase):
    def describe(self):
        return {"schema": 1, "approval_id": self.identifier, "commit": self.manifest["commit"],
                "run_id": self.manifest["run_id"], "run_attempt": self.manifest["run_attempt"],
                "artifact_sha256": self.manifest["artifact"]["sha256"], "sql_sha256": self.manifest["sql_sha256"],
                "sql_executed": False}

    def command(self, args, timeout=45):
        self.calls.append(args)
        if args[-1] == "whoami":
            return runner.USER
        if "--describe " in args[-1]:
            return json.dumps(self.binding)
        if "project1-migration-execute " in args[-1]:
            return json.dumps({**self.describe(), "phase": "succeeded", "sql_executed": True, "production_enabled": True})
        return "fixture"

    def test_matching_approval_submits_only_id_no_zip_sql_or_credentials(self):
        self.calls, self.binding = [], self.describe()
        with mock.patch.object(runner, "run_command", self.command):
            result = runner.execute_remote(self.root / "verified", self.identifier, "dummy-key", "dummy-hosts", self.root)
        self.assertTrue(result["sql_executed"])
        remote = [args[-1] for args in self.calls if args[0] == "/usr/bin/ssh"]
        self.assertEqual(remote[-1], "sudo -n -- /usr/local/sbin/project1-migration-execute " + self.identifier)
        self.assertFalse(any(args[0].endswith("scp") for args in self.calls))

    def test_wrong_selected_commit_digest_run_or_attempt_never_submits_execution(self):
        for field, changed in (("commit", "e" * 40), ("artifact_sha256", "e" * 64),
                               ("sql_sha256", "e" * 64), ("run_id", 999), ("run_attempt", 2)):
            self.calls, self.binding = [], {**self.describe(), field: changed}
            with mock.patch.object(runner, "run_command", self.command), self.assertRaises(Exception):
                runner.execute_remote(self.root / "verified", self.identifier, "dummy-key", "dummy-hosts", self.root)
            self.assertFalse(any(args[-1] == "sudo -n -- /usr/local/sbin/project1-migration-execute " + self.identifier for args in self.calls))

    def test_shell_injection_and_tampered_archive_refused_before_ssh(self):
        for identifier in (";whoami", "a" * 32 + " --sql x", "A" * 32):
            with mock.patch.object(runner, "run_command") as call, self.assertRaises(Exception):
                runner.execute_remote(self.root / "verified", identifier, "key", "hosts", self.root)
            call.assert_not_called()
        (self.root / "verified" / "migrations.zip").write_bytes(b"changed")
        with mock.patch.object(runner, "run_command") as call, self.assertRaises(Exception):
            runner.execute_remote(self.root / "verified", self.identifier, "key", "hosts", self.root)
        call.assert_not_called()

    def test_remote_failure_is_not_retried(self):
        self.calls, self.binding = [], self.describe()
        original = self.command
        def fail(args, timeout=45):
            output = original(args, timeout)
            if args[-1] == "sudo -n -- /usr/local/sbin/project1-migration-execute " + self.identifier:
                raise RuntimeError("secret raw output")
            return output
        with mock.patch.object(runner, "run_command", fail), self.assertRaises(RuntimeError):
            runner.execute_remote(self.root / "verified", self.identifier, "key", "hosts", self.root)
        self.assertEqual(sum(args[-1] == "sudo -n -- /usr/local/sbin/project1-migration-execute " + self.identifier for args in self.calls), 1)


class WorkflowTests(unittest.TestCase):
    def test_manual_off_by_default_exclusive_no_upload_no_restart(self):
        text = (Path(__file__).resolve().parents[3] / ".github/workflows/deploy-manual.yml").read_text(encoding="utf-8")
        inputs, _ = text.split("permissions: {}", 1)
        self.assertIn("execute_migrations:", inputs)
        self.assertIn("default: false", inputs.split("execute_migrations:", 1)[1])
        self.assertIn("sum(modes) > 1", text)
        job = text.split("  migration-execute:\n", 1)[1]
        self.assertIn("!inputs.deploy && !inputs.check_migrations", job)
        self.assertLess(job.index("--expected-digest"), job.index("uses: tailscale/"))
        for forbidden in ("scp", "systemctl", "SQLCMDPASSWORD", "migration_approval.py --", "project1-migration-admin"):
            self.assertNotIn(forbidden, job)
        self.assertIn("cancel-in-progress: false", text)

    def test_entrypoint_refuses_non_root_without_importing_production_adapters(self):
        with mock.patch.object(entrypoint.sys, "platform", "win32"), mock.patch("sys.stderr", io.StringIO()):
            self.assertEqual(entrypoint.main(["execute", "a" * 32]), 1)

    def test_private_host_and_sudoers_have_no_admin_or_arbitrary_command_grants(self):
        scripts = Path(__file__).resolve().parents[1]
        sudoers = (scripts / "project1-migration-execute.sudoers").read_text(encoding="utf-8")
        grants = "\n".join(line for line in sudoers.splitlines() if not line.startswith("#"))
        self.assertNotIn("project1-migration-admin", grants)
        self.assertNotIn("ALL=(root) NOPASSWD: ALL", grants)
        self.assertIn("^[0-9a-f]{32}$", grants)
        self.assertIn("NOSETENV", grants)

    def test_new_review_label_is_accepted_without_invalidating_old_checked_artifacts(self):
        from test_migration_precheck import files_fixture, rehash
        from test_manual_cd import zip_payload
        from migration_package import read_package
        with __import__("tempfile").TemporaryDirectory() as temporary:
            files = files_fixture()
            files["build-info.txt"] = files["build-info.txt"].replace(
                b"REVIEW ONLY: CI tests this SQL only in disposable databases. CD never executes it.",
                b"REVIEW REQUIRED: production execution needs a separately registered administrator approval.")
            path = Path(temporary) / "package.zip"
            path.write_bytes(zip_payload(rehash(files)))
            from test_manual_cd import RUN_ID, SHA
            self.assertTrue(read_package(path, RUN_ID, SHA, 1)["migrations"])


@unittest.skipUnless(sys.platform == "linux", "Real private pipe tests require Linux; never run a production host.")
class PipeTests(unittest.TestCase):
    def setUp(self):
        self.original = subprocess.Popen
        self.commands = []
        # Unprivileged fixture responds on private stdio, not TCP/SQL/production paths.
        self.script = """
import json, sys
for line in sys.stdin:
    request = json.loads(line)
    operation = request.get('operation')
    if operation is None:
        print(json.dumps({'ok': True}), flush=True)
    elif operation == 'status':
        print(json.dumps({'ok': True, 'value': {'schema': 1}}), flush=True)
    elif operation == 'close':
        print(json.dumps({'ok': True}), flush=True)
        break
    else:
        print(json.dumps({'ok': False}), flush=True)
        break
"""

    def spawn(self, command, **kwargs):
        self.commands.append((command, kwargs))
        kwargs.pop("cwd")
        return self.original([sys.executable, "-I", "-c", self.script], **kwargs)

    def patches(self):
        from contextlib import ExitStack
        stack = ExitStack()
        stack.enter_context(mock.patch.object(production.subprocess, "Popen", self.spawn))
        stack.enter_context(mock.patch.object(Path, "resolve", return_value=Path("/fixed/dotnet")))
        stack.enter_context(mock.patch.object(production, "credential", side_effect=lambda name: "P1!" + ("a" if name == "execution-password" else "b") * 64))
        return stack

    def test_real_pipe_bounded_response_cleanup_and_no_password_argv_env(self):
        with self.patches():
            with production.HostSession() as session:
                self.assertEqual(json.loads(session.status()), {"schema": 1})
            self.assertEqual(session.process.returncode, 0)
        command, options = self.commands[0]
        self.assertEqual(command[:2], ["/fixed/dotnet", "exec"])
        self.assertNotIn("P1!", str(command) + str(options["env"]))
        self.assertNotIn("SQLCMDPASSWORD", options["env"])
        self.assertTrue(session.process.stdin.closed)

    def test_failure_and_deadline_terminate_child_without_retry_or_raw_output(self):
        for timeout in (False, True):
            with self.patches():
                session = production.HostSession()
                with self.assertRaises(production.ProductionError):
                    with session:
                        if timeout:
                            session.deadline = 0
                            session.status()
                        else:
                            session.assert_before()
                self.assertIsNotNone(session.process.poll())
        self.assertEqual(len(self.commands), 2)


if __name__ == "__main__":
    unittest.main()
