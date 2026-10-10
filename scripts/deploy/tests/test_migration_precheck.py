"""Offline only: fabricated SQL/ZIPs, mocked GitHub/SSH/sqlcmd, no real credentials."""

import copy
from datetime import datetime, timedelta, timezone
import hashlib
import io
import json
import os
from pathlib import Path
import stat
import subprocess
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest import mock
import warnings
import zipfile

SCRIPTS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SCRIPTS))
import migration_package as package
import verify_migration_artifact as verify
import precheck_migrations as precheck
import project1_migration_status as status
from test_manual_cd import SHA, RUN_ID, WORKFLOW_ID, run_metadata, zip_payload

IDS = ["20260930150622_AddEmailAttachments", "20261009164508_AddProductExampleAttr"]


def files_fixture():
    sql = "\n".join("IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory])\nBEGIN\n"
                      "    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])\n"
                      f"    VALUES (N'{item}', N'10.0.10');\nEND;\nGO\n" for item in IDS)
    info = "\n".join([
        f"Repository: {verify.REPOSITORY}", f"Commit SHA: {SHA}", f"Checked-out commit: {SHA}",
        f"CI run ID: {RUN_ID}", "CI run attempt: 1", "Event: push", "Ref: refs/heads/main",
        f"Run URL: https://github.com/{verify.REPOSITORY}/actions/runs/{RUN_ID}",
        "Migration range: 0 to latest migration in this commit; idempotent SQL Server script.",
        "REVIEW ONLY: CI tests this SQL only in disposable databases. CD never executes it.",
        "Review, test against the target schema and back up before manual production use.", ""])
    files = {"migrations.sql": sql.encode(), "build-info.txt": info.encode(),
             "migration-manifest.json": json.dumps({"schema": 1, "migrations": IDS}).encode()}
    return rehash(files)


def rehash(files):
    files["SHA256SUMS"] = "".join(hashlib.sha256(files[name]).hexdigest() + "  " + name + "\n"
                                for name in ("migrations.sql", "build-info.txt", "migration-manifest.json")).encode()
    return files


def metadata(payload):
    return {"id": 3, "name": verify.PREFIX + SHA, "size_in_bytes": len(payload),
            "digest": "sha256:" + hashlib.sha256(payload).hexdigest(), "expired": False,
            "expires_at": (datetime.now(timezone.utc) + timedelta(days=7)).isoformat(),
            "workflow_run": {"id": RUN_ID, "repository_id": verify.REPOSITORY_ID,
                             "head_repository_id": verify.REPOSITORY_ID, "head_branch": "main", "head_sha": SHA}}


class FakeClient:
    def __init__(self):
        self.payload = zip_payload(files_fixture())
        self.item = metadata(self.payload)
        self.run = run_metadata()
        self.after_download = None

    def get(self, path):
        if path.endswith("/workflows/ci.yml"):
            return {"id": WORKFLOW_ID, "path": verify.WORKFLOW_PATH, "name": "Project1 CI"}
        return copy.deepcopy(self.run)

    def artifacts(self, run_id):
        return [copy.deepcopy(self.item)]

    def download(self, identifier, destination, digest):
        from verify_ci_artifacts import copy_archive
        copy_archive(io.BytesIO(self.payload), destination, digest)
        if self.after_download:
            self.after_download(self)


def response(ids=None):
    return json.dumps({"schema": 1, "server": "homelab-server", "database": "Project1Db",
                       "login": "project1_migrate", "checked_at": "2026-10-10T06:00:00+00:00",
                       "migrations": IDS if ids is None else ids})


class TempCase(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.directory = self.root / "verified"

    def verified(self):
        return verify.verify(FakeClient(), RUN_ID, SHA, self.directory, 1)


class ManifestTests(unittest.TestCase):
    def test_duplicate_json_keys_are_rejected(self):
        with self.assertRaises(verify.VerificationError):
            package.manifest_ids(b'{"schema": 1, "schema": 1, "migrations": []}')

    def test_prefixed_json_ignores_diagnostics(self):
        data = "info:    Ignore host output\ndata:    [\ndata:      " + json.dumps({"id": IDS[0]}) + "\ndata:    ]\n"
        self.assertEqual(package.manifest_ids(package.generate_manifest(data)), IDS[:1])

    def test_invalid_identifiers_and_schemas(self):
        for ids in ([], IDS[::-1], IDS + IDS[-1:], ["../path"], ["::error::bad"], [True], IDS * 1001):
            with self.subTest(ids=str(ids)[:50]), self.assertRaises(verify.VerificationError):
                package.migration_ids(ids)
        for value in ({"schema": True, "migrations": IDS}, {"schema": 2, "migrations": IDS},
                      {"schema": 1, "migrations": IDS, "sql": "bad"}, []):
            with self.subTest(value=value), self.assertRaises(verify.VerificationError):
                package.manifest_ids(json.dumps(value).encode())

    def test_unprefixed_or_empty_ef_output_is_refused(self):
        for data in ("", "[]", "data:    {}", "data:    []", "data:    [true]", "x" * (1024 * 1024 + 1)):
            with self.subTest(data=data[:20]), self.assertRaises(Exception):
                package.generate_manifest(data)


class PackageTests(TempCase):
    def test_generation_cli_creates_manifest_once_without_database_access(self):
        directory = self.root / "generated"
        directory.mkdir()
        (directory / "migrations.sql").write_bytes(files_fixture()["migrations.sql"])
        ef_list = self.root / "ef-list.txt"
        ef_list.write_text("data:    " + json.dumps([{"id": item} for item in IDS]), encoding="utf-8")
        with mock.patch("sys.stdout", io.StringIO()), mock.patch.object(subprocess, "run") as run:
            self.assertEqual(package.main(["--ef-list", str(ef_list), "--directory", str(directory)]), 0)
            self.assertEqual(package.main(["--ef-list", str(ef_list), "--directory", str(directory)]), 1)
            run.assert_not_called()
        self.assertEqual(package.manifest_ids((directory / "migration-manifest.json").read_bytes()), IDS)

    def read(self, files):
        path = self.root / "fixture.zip"
        path.write_bytes(zip_payload(files))
        return package.read_package(path, RUN_ID, SHA, 1)

    def test_good_package_and_sql_indentation(self):
        self.assertEqual(self.read(files_fixture())["migrations"], IDS)

    def test_missing_extra_traversal_files(self):
        for name in ("../migrations.sql", "/migrations.sql", "folder/migrations.sql", "extra.txt"):
            files = files_fixture()
            files[name] = b"bad"
            with self.subTest(name=name), self.assertRaises(verify.VerificationError):
                self.read(files)
        files = files_fixture()
        del files["migration-manifest.json"]
        with self.assertRaises(verify.VerificationError):
            self.read(files)

    def test_corrupted_checksum_and_rehashed_wrong_provenance(self):
        files = files_fixture()
        files["migrations.sql"] += b"tampered"
        with self.assertRaises(verify.VerificationError):
            self.read(files)
        for old, new in ((SHA, "b" * 40), ("Event: push", "Event: pull_request"),
                         ("CI run attempt: 1", "CI run attempt: 2"), ("refs/heads/main", "refs/heads/other")):
            files = files_fixture()
            files["build-info.txt"] = files["build-info.txt"].replace(old.encode(), new.encode())
            with self.subTest(old=old), self.assertRaises(verify.VerificationError):
                self.read(rehash(files))

    def test_sql_and_manifest_must_agree_even_with_valid_hashes(self):
        files = files_fixture()
        files["migration-manifest.json"] = json.dumps({"schema": 1, "migrations": IDS[:1]}).encode()
        with self.assertRaises(verify.VerificationError):
            self.read(rehash(files))

    def test_duplicate_symlink_and_oversize_members(self):
        for kind in ("duplicate", "symlink", "oversize"):
            path = self.root / (kind + ".zip")
            files = files_fixture()
            with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as archive:
                for name, body in files.items():
                    if kind == "symlink" and name == "migrations.sql":
                        entry = zipfile.ZipInfo(name)
                        entry.create_system = 3
                        entry.external_attr = (stat.S_IFLNK | 0o777) << 16
                        archive.writestr(entry, b"/etc/shadow")
                    else:
                        if kind == "oversize" and name == "build-info.txt":
                            body = b"x" * (package.LIMITS[name] + 1)
                        archive.writestr(name, body)
                if kind == "duplicate":
                    with warnings.catch_warnings():
                        warnings.simplefilter("ignore", UserWarning)
                        archive.writestr("migrations.sql", files["migrations.sql"])
            with self.subTest(kind=kind), self.assertRaises(verify.VerificationError):
                package.read_package(path, RUN_ID, SHA, 1)


class VerificationTests(TempCase):
    def test_cli_failure_does_not_print_token_or_http_body(self):
        output = io.StringIO()
        with mock.patch.dict(os.environ, {"GITHUB_TOKEN": "fake-sensitive-token"}, clear=True), \
                mock.patch.object(verify, "verify", side_effect=ValueError("sensitive-http-body")), \
                mock.patch("sys.stdout", output):
            self.assertEqual(verify.main(["--run-id", str(RUN_ID), "--commit", SHA,
                                        "--expected-attempt", "1", "--directory", str(self.directory)]), 1)
        self.assertNotIn("fake-sensitive-token", output.getvalue())
        self.assertNotIn("sensitive-http-body", output.getvalue())

    def test_good_verification_and_reload(self):
        manifest = self.verified()
        self.assertEqual(verify.load_verified(self.directory), manifest)
        self.assertEqual(manifest["migrations"], IDS)

    def test_metadata_rejections(self):
        payload = FakeClient().payload
        for field, value in (("name", "other"), ("expired", True), ("digest", None),
                             ("size_in_bytes", 0), ("size_in_bytes", True),
                             ("expires_at", "invalid"), ("expires_at", "2000-01-01T00:00:00+00:00")):
            item = metadata(payload)
            item[field] = value
            with self.subTest(field=field, value=value), self.assertRaises(verify.VerificationError):
                verify.select_artifact([item], RUN_ID, SHA, datetime.now(timezone.utc))
        item = metadata(payload)
        for field, value in (("head_branch", "other"), ("head_sha", "b" * 40), ("repository_id", 1)):
            bad = copy.deepcopy(item)
            bad["workflow_run"][field] = value
            with self.subTest(field=field), self.assertRaises(verify.VerificationError):
                verify.select_artifact([bad], RUN_ID, SHA, datetime.now(timezone.utc))
        for items in ([], [item, item]):
            with self.assertRaises(verify.VerificationError):
                verify.select_artifact(items, RUN_ID, SHA, datetime.now(timezone.utc))

    def test_attempt_digest_and_mid_download_rerun_refused(self):
        for attempt, digest in ((2, None), (1, "b" * 64)):
            with self.assertRaises(verify.VerificationError):
                verify.verify(FakeClient(), RUN_ID, SHA, self.directory, attempt, digest)
            self.assertFalse(self.directory.exists())
        client = FakeClient()
        client.after_download = lambda value: value.run.update(run_attempt=2)
        with self.assertRaises(verify.VerificationError):
            verify.verify(client, RUN_ID, SHA, self.directory, 1)
        self.assertFalse((self.directory / "verified-migration.json").exists())

    def test_changed_archive_is_refused_before_ssh(self):
        self.verified()
        with (self.directory / "migrations.zip").open("ab") as file:
            file.write(b"changed")
        with mock.patch.object(precheck, "run_command") as command, self.assertRaises(verify.VerificationError):
            precheck.precheck(self.directory, "fake-key", "fake-hosts", self.root)
        command.assert_not_called()


class HistoryTests(TempCase):
    def test_equal_prefix_and_empty_histories(self):
        manifest = self.verified()
        for applied in (IDS, IDS[:1], []):
            result = precheck.compare_history(manifest, response(applied))
            self.assertEqual(result["pending"], IDS[len(applied):])
            self.assertFalse(result["sql_executed"])

    def test_unknown_gapped_newer_duplicate_or_reordered_history(self):
        manifest = self.verified()
        for ids in ([IDS[1]], IDS[::-1], IDS + IDS[-1:], ["20260101000000_Unknown"],
                    IDS + ["20271010120000_Newer"], ["::error::unsafe"]):
            with self.subTest(ids=ids), self.assertRaises(verify.VerificationError):
                precheck.compare_history(manifest, response(ids))

    def test_wrong_identity_timestamp_extra_fields_and_size(self):
        manifest = self.verified()
        for field, value in (("schema", True), ("server", "other"), ("database", "master"),
                             ("login", "sa"), ("checked_at", "2026-10-10T00:00:00")):
            bad = json.loads(response())
            bad[field] = value
            with self.subTest(field=field), self.assertRaises(verify.VerificationError):
                precheck.compare_history(manifest, json.dumps(bad))
        with self.assertRaises(verify.VerificationError):
            precheck.compare_history(manifest, "x" * 180001)

    def test_transport_uses_only_fixed_read_command_and_no_upload(self):
        self.verified()
        commands = []
        def run(arguments, timeout=45):
            commands.append(arguments)
            if arguments[-1] == "whoami":
                return "project1_deploy"
            return response() if "project1-migration-status" in arguments[-1] else "ok"
        with mock.patch.object(precheck, "run_command", side_effect=run):
            result = precheck.precheck(self.directory, "fake-key", "fake-hosts", self.root)
        self.assertEqual(result["pending"], [])
        ssh_commands = [args[-1] for args in commands if args[0] == "/usr/bin/ssh"]
        self.assertEqual(ssh_commands, ["whoami", "sudo -n -- /usr/local/sbin/project1-migration-status"])
        self.assertFalse(any(args[0].endswith("scp") for args in commands))

    def test_remote_errors_and_credentials_are_not_printed(self):
        environment = {"GITHUB_REPOSITORY": verify.REPOSITORY, "GITHUB_REF": "refs/heads/main",
                       "GITHUB_EVENT_NAME": "workflow_dispatch", "MIGRATION_PRECHECK_CONFIRMED": "true",
                       "RUNNER_TEMP": str(self.root), "DEPLOY_SSH_PRIVATE_KEY": "private-key-do-not-print"}
        output = io.StringIO()
        with mock.patch.dict(os.environ, environment, clear=True), mock.patch.object(sys, "platform", "linux"), \
                mock.patch.object(precheck, "precheck", side_effect=ValueError("secret-body")), \
                mock.patch("sys.stdout", output):
            self.assertEqual(precheck.main(["--directory", str(self.directory)]), 1)
        self.assertNotIn("private-key-do-not-print", output.getvalue())
        self.assertNotIn("secret-body", output.getvalue())

    def test_cli_requires_explicit_main_manual_no_deploy_confirmation(self):
        good = {"GITHUB_REPOSITORY": verify.REPOSITORY, "GITHUB_REF": "refs/heads/main",
                "GITHUB_EVENT_NAME": "workflow_dispatch", "MIGRATION_PRECHECK_CONFIRMED": "true"}
        for field, value in (("GITHUB_REPOSITORY", "other/repo"), ("GITHUB_REF", "refs/heads/other"),
                             ("GITHUB_EVENT_NAME", "push"), ("MIGRATION_PRECHECK_CONFIRMED", "false"),
                             ("DEPLOY_CONFIRMED", "true")):
            environment = {**good, field: value}
            with self.subTest(field=field), mock.patch.dict(os.environ, environment, clear=True), \
                    mock.patch.object(sys, "platform", "linux"), mock.patch.object(precheck, "precheck") as run, \
                    mock.patch("sys.stdout", io.StringIO()):
                self.assertEqual(precheck.main(["--directory", str(self.directory)]), 1)
                run.assert_not_called()


class StatusTests(unittest.TestCase):
    @unittest.skipUnless(sys.platform == "linux", "Requires actual Linux O_NOFOLLOW file handling")
    def test_password_reads_only_bounded_regular_file(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "password"
            path.write_text("P1!" + "a" * 64 + "\n", encoding="ascii")
            path.chmod(0o600)
            real_fstat = os.fstat
            def root_info(fd):
                info = real_fstat(fd)
                return SimpleNamespace(st_mode=info.st_mode, st_uid=0, st_nlink=info.st_nlink)
            with mock.patch.object(status, "PASSWORD", path), mock.patch.object(status, "trusted_path"), \
                    mock.patch.object(os, "fstat", side_effect=root_info):
                self.assertEqual(status.read_password(), "P1!" + "a" * 64)
                path.write_text("bad password\n", encoding="ascii")
                with self.assertRaises(status.StatusError):
                    status.read_password()

    @unittest.skipUnless(sys.platform == "linux", "Requires actual Linux symlink and FIFO handling")
    def test_symlink_and_fifo_never_supply_password(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            target = root / "target"
            target.write_text("not-a-real-secret", encoding="ascii")
            link, fifo = root / "link", root / "fifo"
            link.symlink_to(target)
            os.mkfifo(fifo, 0o600)
            for path in (link, fifo):
                with self.subTest(path=path), mock.patch.object(status, "PASSWORD", path), \
                        mock.patch.object(status, "trusted_path"), self.assertRaises((OSError, status.StatusError)):
                    status.read_password()

    def test_valid_empty_and_populated_output(self):
        self.assertEqual(status.parse_output(status.MARKER + "\n"), [])
        self.assertEqual(status.parse_output(status.MARKER + "\n" + "\n".join(IDS)), IDS)

    def test_bad_marker_ids_duplicates_size_and_overflow(self):
        for output in ("", "wrong\n", status.MARKER + "\n::error::bad", "x" * 180001,
                       status.MARKER + "\n" + "\n".join(IDS[::-1]),
                       status.MARKER + "\n" + "\n".join(IDS + IDS[-1:]),
                       status.MARKER + "\n" + "\n".join([IDS[0]] * 1001)):
            with self.subTest(output=output[:40]), self.assertRaises(status.StatusError):
                status.parse_output(output)

    def test_no_arguments_and_no_non_linux_execution(self):
        with mock.patch.object(status, "read_status") as run, mock.patch("sys.stderr", io.StringIO()):
            self.assertEqual(status.main(["--sql", "bad.sql"]), 1)
            with mock.patch.object(sys, "platform", "win32"):
                self.assertEqual(status.main([]), 1)
        run.assert_not_called()

    def test_fixed_query_and_minimal_environment_with_no_password_argv(self):
        result = subprocess.CompletedProcess([], 0, status.MARKER + "\n" + "\n".join(IDS), "")
        seen = []
        def execute(arguments, **options):
            seen.append(dict(options["env"]))
            return result
        with mock.patch.object(status, "trusted_path"), mock.patch.object(os, "access", return_value=True), \
                mock.patch.object(status, "read_password", return_value="fake-sensitive-password"), \
                mock.patch.object(subprocess, "run", side_effect=execute) as run:
            value = status.read_status()
            arguments = run.call_args.args[0]
            environment = dict(run.call_args.kwargs["env"])
        self.assertEqual(value["migrations"], IDS)
        self.assertNotIn("fake-sensitive-password", " ".join(arguments))
        self.assertNotIn("-P", arguments)
        self.assertNotIn("-i", arguments)
        self.assertEqual(arguments[-1], status.QUERY)
        # The passed dict is cleared immediately after the subprocess completes.
        self.assertNotIn("SQLCMDPASSWORD", environment)
        self.assertEqual(set(environment), {"PATH", "HOME", "LANG"})
        self.assertEqual(seen[0]["SQLCMDPASSWORD"], "fake-sensitive-password")
        self.assertEqual(set(seen[0]), {"PATH", "HOME", "LANG", "SQLCMDPASSWORD"})
        self.assertEqual(run.call_args.kwargs["stdin"], subprocess.DEVNULL)
        self.assertNotRegex(status.QUERY, r"(?i)\b(INSERT|UPDATE|DELETE|ALTER|DROP|CREATE|EXEC)\s+")

    def test_sqlcmd_failure_does_not_reveal_output(self):
        result = subprocess.CompletedProcess([], 1, "sensitive stdout", "sensitive stderr")
        with mock.patch.object(status, "trusted_path"), mock.patch.object(os, "access", return_value=True), \
                mock.patch.object(status, "read_password", return_value="fake"), \
                mock.patch.object(subprocess, "run", return_value=result), self.assertRaises(status.StatusError) as error:
            status.read_status()
        self.assertNotIn("sensitive", str(error.exception))

    def test_untrusted_path_owners_modes_symlinks_hardlinks(self):
        def fake_info(mode=stat.S_IFREG | 0o644, uid=0, gid=0, links=1):
            return SimpleNamespace(st_mode=mode, st_uid=uid, st_gid=gid, st_nlink=links)
        for bad in (fake_info(uid=1001), fake_info(mode=stat.S_IFLNK | 0o777),
                    fake_info(mode=stat.S_IFREG | 0o666), fake_info(mode=stat.S_IFREG | 0o664, gid=1001),
                    fake_info(links=2)):
            def lstat(path):
                return bad if path == Path("/test/file") else fake_info(stat.S_IFDIR | 0o755)
            with self.subTest(bad=bad), mock.patch.object(Path, "lstat", lstat), self.assertRaises(status.StatusError):
                status.trusted_path(Path("/test/file"))


class WorkflowTests(unittest.TestCase):
    def test_precheck_default_mutual_exclusion_reverification_and_no_upload(self):
        workflow = (SCRIPTS.parents[1] / ".github/workflows/deploy-manual.yml").read_text(encoding="utf-8")
        before, job = workflow.split("  migration-precheck:\n")
        job = job.split("  migration-execute:\n")[0]
        self.assertIn("      check_migrations:\n", before)
        self.assertIn("Choose at most one server mode", before)
        self.assertIn("sum(modes) > 1", before)
        self.assertIn("inputs.check_migrations && !inputs.deploy", job)
        self.assertIn("needs: validate", job)
        self.assertIn("--expected-digest", job)
        self.assertLess(job.index("--expected-digest"), job.index("uses: tailscale/"))
        self.assertNotIn("deploy_verified.py", job)
        self.assertNotIn("scp", job)
        self.assertNotIn("    environment:", workflow)
        self.assertNotIn("SQLCMDPASSWORD", workflow)
        self.assertIn("group: project1-manual-deployment", workflow)
        self.assertIn("cancel-in-progress: false", workflow)

    def test_sudoers_is_separate_and_argument_free(self):
        text = (SCRIPTS / "project1-migration-status.sudoers").read_text(encoding="utf-8")
        lines = [line for line in text.splitlines() if line and not line.startswith("#")]
        self.assertEqual(lines, ['project1_deploy ALL=(root) NOPASSWD: NOSETENV: /usr/local/sbin/project1-migration-status ""'])


if __name__ == "__main__":
    unittest.main()
