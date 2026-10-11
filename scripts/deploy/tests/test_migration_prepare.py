"""Offline preparation tests: fabricated packages, mocked SSH/systemctl/SQL only."""

from contextlib import ExitStack, nullcontext
import hashlib
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import prepare_migrations as transport
import project1_migration_prepare as server
import install_migration_prepare as installer
from test_migration_precheck import TempCase, IDS
from test_manual_cd import SHA, RUN_ID

TOKEN = "b" * 32


def report(manifest):
    return {"schema": 1, "commit": SHA, "run_id": RUN_ID, "run_attempt": 1, "token": TOKEN,
            "server": "homelab-server", "database": "Project1Db", "sql_sha256": manifest["sql_sha256"],
            "sql_path": f"{transport.STATE}/{TOKEN}/migrations.sql",
            "backup_path": f"{transport.BACKUPS}/project1-prepare-{TOKEN}/Project1Db.bak",
            "backup_sha256": "c" * 64, "backup_size": 100, "pending": IDS[-1:],
            "prepared_at": "2026-10-11T01:00:00+00:00", "sql_executed": False,
            "api_stopped": True, "backup_verified": True}


class TransportTests(TempCase):
    def setUp(self):
        super().setUp()
        output = mock.patch("sys.stdout", new_callable=io.StringIO)
        output.start()
        self.addCleanup(output.stop)

    def fake_commands(self, result, fail=None):
        def command(arguments, timeout=45):
            last = arguments[-1]
            if last == "whoami":
                return transport.USER
            if last.endswith("--help"):
                return transport.USAGE
            if last.endswith("--check-stopped"):
                if fail == "active":
                    raise transport.VerificationError("API active")
                return "API_STOPPED"
            if arguments[0] == "/usr/bin/scp" and fail == "transfer":
                raise transport.VerificationError("transfer failed")
            if last.startswith("sudo -n -- " + transport.HELPER + " " + SHA):
                if fail == "backup":
                    raise transport.VerificationError("backup failed")
                return json.dumps(result)
            return ""
        return command

    def test_good_transport_uses_verified_zip_and_reports_manual_next_steps(self):
        manifest = self.verified()
        expected = report(manifest)
        with mock.patch.object(transport.uuid, "uuid4", return_value=mock.Mock(hex=TOKEN)), \
                mock.patch.object(transport, "run_command", side_effect=self.fake_commands(expected)) as commands:
            self.assertEqual(transport.prepare(self.directory, "key", "hosts", self.root), expected)
        calls = [call.args[0] for call in commands.call_args_list]
        uploads = [args for args in calls if args[0] == "/usr/bin/scp"]
        self.assertEqual(len(uploads), 1)
        self.assertEqual(uploads[0][-2], str(self.directory / "migrations.zip"))
        self.assertIn(TOKEN, uploads[0][-1])
        check = next(i for i, args in enumerate(calls) if args[-1].endswith("--check-stopped"))
        self.assertLess(check, calls.index(uploads[0]))
        self.assertIn("API remains stopped", transport.report_text(expected))
        self.assertNotIn("project1-deploy ", "\n".join(args[-1] for args in calls))

    def test_active_api_transfer_backup_failures_stop_without_retry(self):
        manifest = self.verified()
        for failure in ("active", "transfer", "backup"):
            with self.subTest(failure=failure), \
                    mock.patch.object(transport.uuid, "uuid4", return_value=mock.Mock(hex=TOKEN)), \
                    mock.patch.object(transport, "run_command", side_effect=self.fake_commands(report(manifest), failure)) as commands:
                with self.assertRaises(transport.VerificationError):
                    transport.prepare(self.directory, "key", "hosts", self.root)
                calls = [call.args[0] for call in commands.call_args_list]
                invocations = [args for args in calls if args[-1].startswith("sudo -n -- " + transport.HELPER + " " + SHA)]
                self.assertEqual(len(invocations), 1 if failure == "backup" else 0)
                if failure == "active":
                    self.assertFalse(any(args[0] == "/usr/bin/scp" for args in calls))

    def test_tampered_package_or_missing_credentials_never_connects(self):
        self.verified()
        with mock.patch.object(transport, "run_command") as commands:
            with self.assertRaises(transport.VerificationError):
                transport.prepare(self.directory, "", "hosts", self.root)
            (self.directory / "migrations.zip").write_bytes(b"corrupt")
            with self.assertRaises(Exception):
                transport.prepare(self.directory, "key", "hosts", self.root)
            commands.assert_not_called()

    def test_report_rejects_forged_paths_identity_flags_time_and_pending(self):
        manifest = self.verified()
        for key, value in (("sql_path", "/tmp/evil.sql"), ("database", "master"),
                           ("backup_path", "/etc/passwd"), ("backup_size", True),
                           ("schema", True), ("sql_executed", True), ("api_stopped", False),
                           ("backup_verified", False), ("backup_sha256", "wrong"),
                           ("prepared_at", "2026-10-11"), ("pending", IDS[:1]),
                           ("run_attempt", 2), ("pending", [])):
            value_report = {**report(manifest), key: value}
            with self.subTest(key=key, value=value), self.assertRaises(Exception):
                transport.validate_report(json.dumps(value_report), manifest, TOKEN)

    def test_cli_guards_and_error_messages_do_not_leak_credentials(self):
        baseline = {"GITHUB_EVENT_NAME": "workflow_dispatch", "GITHUB_REF": "refs/heads/main",
                    "GITHUB_REPOSITORY": "shunbin2310/Project1", "MIGRATION_PREPARE_CONFIRMED": "true",
                    "RUNNER_TEMP": str(self.root)}
        for key, value in (("GITHUB_EVENT_NAME", "push"), ("GITHUB_REF", "refs/heads/pr"),
                           ("MIGRATION_PREPARE_CONFIRMED", "false"), ("DEPLOY_CONFIRMED", "true"),
                           ("MIGRATION_PRECHECK_CONFIRMED", "true")):
            with mock.patch.dict(os.environ, {**baseline, key: value}, clear=True), \
                    mock.patch.object(sys, "platform", "linux"), mock.patch.object(transport, "prepare") as prepare, \
                    mock.patch("sys.stdout", new_callable=io.StringIO):
                self.assertEqual(transport.main(["--directory", str(self.directory)]), 1)
                prepare.assert_not_called()
        with mock.patch.dict(os.environ, baseline, clear=True), mock.patch.object(sys, "platform", "linux"), \
                mock.patch.object(transport, "prepare", side_effect=RuntimeError("secret-password")), \
                mock.patch("sys.stdout", new_callable=io.StringIO) as output:
            self.assertEqual(transport.main(["--directory", str(self.directory)]), 1)
            self.assertNotIn("secret-password", output.getvalue())


class ServerTests(TempCase):
    def test_backup_parent_rejects_nonroot_or_writable_before_creating_child(self):
        import types
        for uid, mode in ((999, 0o40755), (0, 0o40775), (0, 0o40777)):
            with self.subTest(uid=uid, mode=mode), \
                    mock.patch.object(server, "backup_directory", return_value=nullcontext(42)), \
                    mock.patch.object(server.os, "fstat", return_value=types.SimpleNamespace(st_uid=uid, st_mode=mode)), \
                    mock.patch.object(server.os, "mkdir") as mkdir:
                with self.assertRaises(server.PreparationError):
                    server.create_backup_directory(TOKEN, 999, 988)
                mkdir.assert_not_called()

    def test_backup_child_creation_uses_pinned_parent_and_descriptor_ownership(self):
        import types
        with ExitStack() as stack:
            stack.enter_context(mock.patch.object(server, "backup_directory", return_value=nullcontext(42)))
            stack.enter_context(mock.patch.object(server.os, "fstat", return_value=types.SimpleNamespace(st_uid=0, st_mode=0o40755)))
            stack.enter_context(mock.patch.object(server.os, "O_DIRECTORY", 0, create=True))
            stack.enter_context(mock.patch.object(server.os, "O_NOFOLLOW", 0, create=True))
            mkdir = stack.enter_context(mock.patch.object(server.os, "mkdir"))
            opening = stack.enter_context(mock.patch.object(server.os, "open", return_value=43))
            ownership = stack.enter_context(mock.patch.object(server.os, "fchown", create=True))
            synced = stack.enter_context(mock.patch.object(server.os, "fsync"))
            closed = stack.enter_context(mock.patch.object(server.os, "close"))
            result = server.create_backup_directory(TOKEN, 999, 988)
        name = "project1-prepare-" + TOKEN
        self.assertEqual(result, server.BACKUPS / name)
        mkdir.assert_called_once_with(name, mode=0o700, dir_fd=42)
        self.assertEqual(opening.call_args.kwargs["dir_fd"], 42)
        ownership.assert_called_once_with(43, 999, 988)
        synced.assert_called_once_with(42)
        closed.assert_called_once_with(43)

    def test_request_rejects_shell_paths_overflow_and_extra_arguments(self):
        valid = [SHA, str(RUN_ID), "1", TOKEN, "a" * 64]
        self.assertEqual(server.Request.parse(valid).token, TOKEN)
        for index, value in ((0, "x;id"), (1, "1;id"), (1, "9" * 21), (2, "0"),
                             (3, "../path"), (3, TOKEN.upper()), (4, "x")):
            args = valid.copy()
            args[index] = value
            with self.subTest(index=index, value=value), self.assertRaises(Exception):
                server.Request.parse(args)
        with self.assertRaises(Exception):
            server.Request.parse(valid + ["extra"])

    def test_service_check_is_read_only_and_requires_complete_stop(self):
        state = {"LoadState": "loaded", "ActiveState": "inactive", "SubState": "dead", "MainPID": "0",
                 "FragmentPath": "/etc/systemd/system/project1-api.service"}
        for key, value in ((None, None), ("MainPID", "42"), ("ActiveState", "active"),
                           ("LoadState", "not-found"), ("FragmentPath", "/tmp/service")):
            values = {**state, **({key: value} if key else {})}
            completed = subprocess.CompletedProcess([], 0, "\n".join(f"{k}={v}" for k, v in values.items()), "")
            with mock.patch.object(server.subprocess, "run", return_value=completed) as run:
                if key:
                    with self.assertRaises(server.PreparationError):
                        server.require_api_stopped()
                else:
                    server.require_api_stopped()
                self.assertEqual(run.call_args.args[0][1], "show")
                self.assertFalse(run.call_args.kwargs.get("shell", False))

    def test_snapshot_is_private_and_rejects_bad_hash_and_package(self):
        manifest = self.verified()
        operation = self.root / "operation"
        operation.mkdir()
        request = server.Request(SHA, RUN_ID, 1, TOKEN, manifest["artifact"]["sha256"])
        with mock.patch.object(server, "open_upload", side_effect=lambda *args: (self.directory / "migrations.zip").open("rb")):
            content = server.snapshot(request, operation, 1000)
            self.assertEqual(content["sql_sha256"], manifest["sql_sha256"])
            (self.directory / "migrations.zip").write_bytes(b"changed")
            self.assertEqual(hashlib.sha256((operation / "migrations.sql").read_bytes()).hexdigest(), manifest["sql_sha256"])
            other = self.root / "other"
            other.mkdir()
            with self.assertRaises(server.PreparationError):
                server.snapshot(request, other, 1000)

    def test_history_refuses_unknown_newer_or_already_current(self):
        content = {"migrations": IDS}
        baseline = {"server": "homelab-server", "database": "Project1Db", "login": "project1_migrate"}
        for applied in (IDS, [IDS[-1]], IDS + ["20261011120000_Newer"]):
            with mock.patch.object(server, "read_status", return_value={**baseline, "migrations": applied}), \
                    self.assertRaises(server.PreparationError):
                server.pending_history(content)

    def test_backup_query_only_accepts_token_and_never_reads_uploaded_sql(self):
        self.assertEqual(server.backup_query(TOKEN),
                         f"SET NOCOUNT ON; EXEC master.dbo.project1_prepare_backup @Token = N'{TOKEN}';")
        for token in ("' ; DROP TABLE Products;--", "../backup", TOKEN.upper(), ""):
            with self.assertRaises(server.PreparationError):
                server.backup_query(token)

    def test_preparation_journal_and_fail_closed_boundaries(self):
        manifest = self.verified()
        state, shared, backups = self.root / "state", self.root / "shared", self.root / "backups"
        for path in (state, shared, backups):
            path.mkdir()
        request = server.Request(SHA, RUN_ID, 1, TOKEN, manifest["artifact"]["sha256"])
        with ExitStack() as stack:
            for name, value in (("STATE", state), ("SHARED_STATE", shared), ("BACKUPS", backups)):
                stack.enter_context(mock.patch.object(server, name, value))
            for name in ("require_root_directory", "require_root_file", "require_backup_ancestry"):
                stack.enter_context(mock.patch.object(server, name))
            stack.enter_context(mock.patch.object(server, "deployment_lock", return_value=nullcontext()))
            stack.enter_context(mock.patch.object(server, "write_record", side_effect=lambda path, value: path.write_text(json.dumps(value), encoding="utf-8")))
            stopped = stack.enter_context(mock.patch.object(server, "require_api_stopped"))
            snapshot = stack.enter_context(mock.patch.object(server, "snapshot", return_value={"migrations": IDS, "sql_sha256": manifest["sql_sha256"]}))
            stack.enter_context(mock.patch.object(server, "pending_history", return_value=(IDS[:1], IDS[-1:])))
            stack.enter_context(mock.patch.object(server, "create_backup_directory", return_value=backups / ("project1-prepare-" + TOKEN)))
            backup = stack.enter_context(mock.patch.object(server, "run_backup", side_effect=RuntimeError("secret")))
            with self.assertRaises(RuntimeError):
                server.prepare(request, 1000, 999, 988)
            self.assertEqual(json.loads((state / TOKEN / "status.json").read_text())["phase"], "backup-uncertain")
            backup.assert_called_once_with(TOKEN)
            snapshot.assert_called_once()
            # An uncertain backup blocks a new request as well; no automatic retry/cleanup.
            other = server.Request(SHA, RUN_ID, 1, "d" * 32, request.digest)
            with self.assertRaises(server.PreparationError):
                server.prepare(other, 1000, 999, 988)
            self.assertEqual(backup.call_count, 1)
            self.assertGreaterEqual(stopped.call_count, 3)

    def test_running_api_prevents_snapshot_and_backup(self):
        with mock.patch.object(server, "require_root_directory"), \
                mock.patch.object(server, "require_api_stopped", side_effect=server.PreparationError("active")), \
                mock.patch.object(server, "snapshot") as snapshot, mock.patch.object(server, "run_backup") as backup:
            with self.assertRaises(server.PreparationError):
                server.prepare(server.Request(SHA, RUN_ID, 1, TOKEN, "a" * 64), 1000, 999, 988)
            snapshot.assert_not_called()
            backup.assert_not_called()

    def test_unresolved_app_deployment_blocks_preparation(self):
        shared = self.root / "shared"
        shared.mkdir()
        (shared / "pending.json").write_text("{}")
        with mock.patch.object(server, "SHARED_STATE", shared), \
                mock.patch.object(server, "require_root_directory"), mock.patch.object(server, "require_api_stopped"), \
                mock.patch.object(server, "deployment_lock", return_value=nullcontext()), \
                mock.patch.object(server, "snapshot") as snapshot, mock.patch.object(server, "run_backup") as backup:
            with self.assertRaises(server.PreparationError):
                server.prepare(server.Request(SHA, RUN_ID, 1, TOKEN, "a" * 64), 1000, 999, 988)
            snapshot.assert_not_called()
            backup.assert_not_called()

    def test_success_retains_report_and_refuses_replay(self):
        manifest = self.verified()
        state, shared, backups = self.root / "state", self.root / "shared", self.root / "backups"
        for path in (state, shared, backups):
            path.mkdir()
        request = server.Request(SHA, RUN_ID, 1, TOKEN, manifest["artifact"]["sha256"])
        with ExitStack() as stack:
            for name, value in (("STATE", state), ("SHARED_STATE", shared), ("BACKUPS", backups)):
                stack.enter_context(mock.patch.object(server, name, value))
            for name in ("require_root_directory", "require_root_file", "require_backup_ancestry", "require_api_stopped"):
                stack.enter_context(mock.patch.object(server, name))
            stack.enter_context(mock.patch.object(server, "deployment_lock", side_effect=lambda *args: nullcontext()))
            stack.enter_context(mock.patch.object(server, "write_record", side_effect=lambda path, value: path.write_text(json.dumps(value), encoding="utf-8")))
            stack.enter_context(mock.patch.object(server, "snapshot", return_value={"migrations": IDS, "sql_sha256": manifest["sql_sha256"]}))
            stack.enter_context(mock.patch.object(server, "pending_history", return_value=(IDS[:1], IDS[-1:])))
            stack.enter_context(mock.patch.object(server, "create_backup_directory", return_value=backups / ("project1-prepare-" + TOKEN)))
            backup = stack.enter_context(mock.patch.object(server, "run_backup"))
            stack.enter_context(mock.patch.object(server, "backup_digest", return_value=("c" * 64, 100)))
            result = server.prepare(request, 1000, 999, 988)
            self.assertIs(result["sql_executed"], False)
            self.assertIs(result["backup_verified"], True)
            record = json.loads((state / TOKEN / "status.json").read_text())
            self.assertEqual(record["phase"], "prepared")
            self.assertEqual(record["report"], result)
            with self.assertRaises(FileExistsError):
                server.prepare(request, 1000, 999, 988)
            backup.assert_called_once()

    def test_no_pending_migrations_never_creates_backup(self):
        state, shared = self.root / "state", self.root / "shared"
        state.mkdir()
        shared.mkdir()
        with ExitStack() as stack:
            stack.enter_context(mock.patch.object(server, "STATE", state))
            stack.enter_context(mock.patch.object(server, "SHARED_STATE", shared))
            for name in ("require_root_directory", "require_root_file", "require_api_stopped"):
                stack.enter_context(mock.patch.object(server, name))
            stack.enter_context(mock.patch.object(server, "deployment_lock", return_value=nullcontext()))
            stack.enter_context(mock.patch.object(server, "write_record", side_effect=lambda path, value: path.write_text(json.dumps(value))))
            stack.enter_context(mock.patch.object(server, "snapshot", return_value={"migrations": IDS}))
            stack.enter_context(mock.patch.object(server, "pending_history", side_effect=server.PreparationError("No pending")))
            backup = stack.enter_context(mock.patch.object(server, "run_backup"))
            with self.assertRaises(server.PreparationError):
                server.prepare(server.Request(SHA, RUN_ID, 1, TOKEN, "a" * 64), 1000, 999, 988)
            backup.assert_not_called()
            self.assertEqual(json.loads((state / TOKEN / "status.json").read_text())["phase"], "failed")

    def test_backup_command_uses_only_server_secret_and_fixed_q_not_file_execution(self):
        import types
        password_file = self.root / "password"
        password_file.write_text("P1!" + "a" * 64 + "\n", encoding="ascii")
        captured = []
        def run(arguments, **kwargs):
            captured.append((arguments, dict(kwargs["env"])))
            return subprocess.CompletedProcess(arguments, 0, "Backup valid.\nPROJECT1_PREPARE_BACKUP_V1\n", "")
        with ExitStack() as stack:
            stack.enter_context(mock.patch.object(server, "PASSWORD", password_file))
            stack.enter_context(mock.patch.object(server, "trusted_path"))
            stack.enter_context(mock.patch.object(server.os, "O_NOFOLLOW", 0, create=True))
            stack.enter_context(mock.patch.object(server.os, "O_NONBLOCK", 0, create=True))
            stack.enter_context(mock.patch.object(server.os, "fstat", return_value=types.SimpleNamespace(
                st_mode=0o100600, st_uid=0, st_nlink=1)))
            stack.enter_context(mock.patch.object(server.subprocess, "run", side_effect=run))
            server.run_backup(TOKEN)
        arguments, environment = captured[0]
        self.assertNotIn("-i", arguments)
        self.assertNotIn("-P", arguments)
        self.assertIn("-I", arguments)
        self.assertEqual(arguments[-1], server.backup_query(TOKEN))
        self.assertEqual(environment["SQLCMDPASSWORD"], "P1!" + "a" * 64)
        self.assertNotIn("SQLCMDINI", environment)
        self.assertFalse(any("P1!" in item for item in arguments))


class PolicyTests(unittest.TestCase):
    def test_workflow_is_manual_exclusive_and_verifies_before_network(self):
        root = Path(__file__).resolve().parents[3]
        workflow = (root / ".github/workflows/deploy-manual.yml").read_text(encoding="utf-8")
        job = workflow.split("  migration-prepare:\n", 1)[1]
        self.assertIn("inputs.prepare_migrations && !inputs.deploy && !inputs.check_migrations", job)
        self.assertLess(job.index("--expected-digest"), job.index("uses: tailscale/"))
        self.assertIn("prepare_migrations.py", job)
        for forbidden in ("install_migration_prepare", "SQLCMDPASSWORD", "project1_backup", "execute_migration", "systemctl stop"):
            self.assertNotIn(forbidden, workflow)

    def test_installation_template_uses_signed_fixed_procedure_not_broad_login_grants(self):
        root = Path(__file__).resolve().parents[1]
        sql = (root / "install_migration_prepare.sql").read_text(encoding="utf-8")
        self.assertIn("GRANT EXECUTE ON dbo.project1_prepare_backup TO [project1_backup]", sql)
        self.assertIn("ADD SIGNATURE", sql)
        self.assertIn("REMOVE PRIVATE KEY", sql)
        self.assertIn("WITH COPY_ONLY, CHECKSUM, STOP_ON_ERROR", sql)
        self.assertIn("RESTORE VERIFYONLY", sql)
        self.assertNotIn("TO [project1_backup];\nGRANT CREATE", sql)
        procedure = sql.split("CREATE PROCEDURE", 1)[1].split("GO\nADD SIGNATURE", 1)[0]
        for forbidden in ("EXEC (", "RESTORE DATABASE", "ALTER TABLE", "DROP TABLE", "__EFMigrationsHistory"):
            self.assertNotIn(forbidden, procedure)

    def test_installer_non_linux_fails_without_commands(self):
        with mock.patch.object(sys, "platform", "win32"), mock.patch.object(installer, "command") as command:
            with self.assertRaises(RuntimeError):
                installer.install()
            command.assert_not_called()


if __name__ == "__main__":
    unittest.main()
