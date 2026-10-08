"""No real sudo, systemctl, HTTP, production files, or private keys are used.

Portable tests exercise archive handling and transaction rollback with a fake
runtime. Linux-only tests exercise actual no-follow opens, permissions and flock.
"""

import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import stat
import sys
import tempfile
import time
from types import SimpleNamespace
import unittest
from unittest import mock
import warnings
import zipfile


SCRIPT = Path(__file__).resolve().parents[1] / "project1_deploy.py"
SPEC = importlib.util.spec_from_file_location("project1_deploy", SCRIPT)
deploy = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = deploy
SPEC.loader.exec_module(deploy)
COMMIT = "a" * 40
API_FILES = {"Project1.Api.dll": b"MZ-test-assembly", "Project1.Api.deps.json": b"{}",
             "Project1.Api.runtimeconfig.json": b'{"runtimeOptions":{"tfm":"net10.0"}}'}
FRONTEND_FILES = {"index.html": b"<html>new</html>", "assets/app.js": b"console.log('test')"}


def zip_bytes(files):
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for name, content in files.items():
            archive.writestr(name, content)
    return stream.getvalue()


def metadata(mode=stat.S_IFREG | 0o600, uid=1001, links=1, size=10):
    return SimpleNamespace(st_mode=mode, st_uid=uid, st_nlink=links, st_size=size)


class TempCase(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.addCleanup(self.temporary.cleanup)


class ArgumentTests(unittest.TestCase):
    def test_valid_request(self):
        deploy.Request(COMMIT, "b" * 64, "c" * 64).validate()

    def test_reject_commit_path_command_and_short_sha(self):
        for commit in ("main", "a" * 7, "../etc", "/tmp/foo", "A" * 40, COMMIT + "; reboot", ""):
            with self.subTest(commit=commit), self.assertRaises(deploy.DeploymentError):
                deploy.Request(commit, "b" * 64, "c" * 64).validate()

    def test_reject_bad_digests(self):
        for digest in ("B" * 64, "b" * 63, "sha256:" + "b" * 64, "../foo", ""):
            for request in (deploy.Request(COMMIT, digest, "c" * 64),
                            deploy.Request(COMMIT, "b" * 64, digest)):
                with self.subTest(digest=digest), self.assertRaises(deploy.DeploymentError):
                    request.validate()

    def test_cli_help_has_no_privileged_effects(self):
        with mock.patch.object(deploy, "deployment_lock") as lock, mock.patch("sys.stdout", new_callable=io.StringIO):
            self.assertEqual(deploy.main(["--help"]), 0)
            lock.assert_not_called()

    def test_cli_has_no_path_or_command_override(self):
        with mock.patch("sys.stderr", new_callable=io.StringIO):
            self.assertEqual(deploy.main([COMMIT, "b" * 64, "c" * 64, "--root", "/tmp"]), 2)

    def test_cli_refuses_non_linux_before_production_access(self):
        with mock.patch.object(deploy.sys, "platform", "win32"), mock.patch.object(deploy, "require_root_directory") as check, mock.patch("sys.stderr", new_callable=io.StringIO):
            self.assertEqual(deploy.main([COMMIT, "b" * 64, "c" * 64]), 1)
            check.assert_not_called()


class SnapshotTests(TempCase):
    def test_snapshot_hash_and_bytes(self):
        content = b"a ZIP fixture, not a secret"
        target = self.root / "snapshot.zip"
        deploy.copy_and_verify(io.BytesIO(content), target, hashlib.sha256(content).hexdigest())
        self.assertEqual(target.read_bytes(), content)

    def test_snapshot_rejects_mismatch_and_empty(self):
        for number, content in enumerate((b"different", b"")):
            with self.assertRaises(deploy.DeploymentError):
                deploy.copy_and_verify(io.BytesIO(content), self.root / str(number), "b" * 64)

    def test_source_growth_is_bounded(self):
        with mock.patch.object(deploy, "MAX_ARCHIVE_BYTES", 4), self.assertRaises(deploy.DeploymentError):
            deploy.copy_and_verify(io.BytesIO(b"12345"), self.root / "oversized", "b" * 64)

    def test_existing_snapshot_is_never_overwritten(self):
        target = self.root / "existing"
        target.write_bytes(b"preserve")
        with self.assertRaises(FileExistsError):
            deploy.copy_and_verify(io.BytesIO(b"new"), target, "b" * 64)
        self.assertEqual(target.read_bytes(), b"preserve")

    def test_upload_metadata_rejects_links_fifo_wrong_owner_and_sizes(self):
        invalid = [metadata(mode=stat.S_IFLNK | 0o777), metadata(mode=stat.S_IFIFO | 0o600),
                   metadata(uid=0), metadata(links=2), metadata(size=0),
                   metadata(size=deploy.MAX_ARCHIVE_BYTES + 1)]
        for value in invalid:
            with self.subTest(value=value), self.assertRaises(deploy.DeploymentError):
                deploy.validate_upload_metadata(value, 1001)
        deploy.validate_upload_metadata(metadata(), 1001)

    def test_every_upload_component_uses_no_follow(self):
        path = self.root / "uploads" / COMMIT / "api.zip"
        directory_count = len(path.parent.parts) - 1
        stats = [metadata(mode=stat.S_IFDIR | 0o755, uid=0)] * directory_count + [metadata()]
        class FakeReader(io.BytesIO):
            def fileno(self):
                return 99
        with mock.patch.object(deploy.os, "O_DIRECTORY", 0x10000, create=True), mock.patch.object(deploy.os, "O_NOFOLLOW", 0x20000, create=True), mock.patch.object(deploy.os, "O_NONBLOCK", 0x40000, create=True), mock.patch.object(deploy.os, "open", return_value=99) as opened, mock.patch.object(deploy.os, "close"), mock.patch.object(deploy.os, "fstat", side_effect=stats), mock.patch.object(deploy.os, "fdopen", return_value=FakeReader(b"fixture")):
            with deploy.open_upload(path, 1001) as source:
                self.assertEqual(source.read(), b"fixture")
            self.assertEqual(opened.call_count, directory_count + 2)
            for call in opened.call_args_list:
                self.assertTrue(call.args[1] & 0x20000)
                self.assertFalse(call.args[1] & os.O_CREAT)
            self.assertTrue(opened.call_args_list[-1].args[1] & 0x40000)

    def test_root_guard_rejects_unsafe_directory_metadata(self):
        for value in (metadata(mode=stat.S_IFLNK | 0o777, uid=0),
                      metadata(mode=stat.S_IFDIR | 0o775, uid=0),
                      metadata(mode=stat.S_IFDIR | 0o755, uid=1001)):
            with mock.patch.object(Path, "lstat", return_value=value), self.assertRaises(deploy.DeploymentError):
                deploy.require_root_directory(self.root)

    def test_root_guard_checks_ancestors(self):
        calls = []
        def inspect(path):
            calls.append(path)
            return metadata(mode=stat.S_IFDIR | 0o755, uid=0)
        with mock.patch.object(Path, "lstat", inspect):
            deploy.require_root_directory(self.root)
        self.assertEqual(calls, [*reversed(self.root.parents), self.root])

    def test_root_guard_rejects_writable_symlink_hardlink_files(self):
        for value in (metadata(mode=stat.S_IFREG | 0o660, uid=0),
                      metadata(mode=stat.S_IFLNK | 0o777, uid=0), metadata(uid=0, links=2)):
            with mock.patch.object(deploy, "require_root_directory"), mock.patch.object(Path, "lstat", return_value=value), self.assertRaises(deploy.DeploymentError):
                deploy.require_root_file(self.root / "file")


class ArchiveTests(TempCase):
    def validate(self, files, kind="api"):
        with zipfile.ZipFile(io.BytesIO(zip_bytes(files))) as archive:
            return deploy.validate_members(archive, kind)

    def extract(self, files, kind="api"):
        snapshot = self.root / "snapshot.zip"
        snapshot.write_bytes(zip_bytes(files))
        target = self.root / "candidate"
        deploy.extract_archive(snapshot, target, kind)
        return target

    def test_valid_api_and_frontend_root_layouts(self):
        self.validate(API_FILES)
        self.validate(FRONTEND_FILES, "frontend")

    def test_valid_extraction(self):
        target = self.extract(API_FILES)
        self.assertEqual((target / "Project1.Api.dll").read_bytes(), API_FILES["Project1.Api.dll"])

    def test_valid_frontend_extraction(self):
        target = self.extract(FRONTEND_FILES, "frontend")
        self.assertEqual((target / "assets/app.js").read_bytes(), FRONTEND_FILES["assets/app.js"])

    def test_reject_traversal_absolute_ambiguous_and_control_paths(self):
        names = ("../escape", "/etc/passwd", "C:/evil", "a//b", "a/./b", "a/../b",
                 "a/", "trail. ", "trail.", "bad\nname")
        for name in names:
            files = {**API_FILES, name: b"x"}
            with self.subTest(name=name), self.assertRaises(deploy.DeploymentError):
                self.validate(files)

    def test_raw_backslash_and_nul_metadata_is_rejected(self):
        # zipfile's writer normalizes backslashes on Windows and truncates at NUL.
        # Inject the raw reader metadata to exercise the helper's own validation.
        for name in ("a\\b", "nul\0suffix"):
            with zipfile.ZipFile(io.BytesIO(zip_bytes(API_FILES))) as archive:
                archive.filelist[0].orig_filename = name
                with self.assertRaises(deploy.DeploymentError):
                    deploy.validate_members(archive, "api")

    def test_reject_secret_or_server_specific_config(self):
        for name in (".env", ".env.production", ".ssh/authorized_keys", "secret.pem", "nested/key.pfx",
                     "appsettings.Development.json", "appsettings.Production.json", "project1_github_deploy"):
            with self.subTest(name=name), self.assertRaises(deploy.DeploymentError):
                self.validate({**API_FILES, name: b"placeholder"})

    def test_reject_duplicate_and_case_colliding_names(self):
        stream = io.BytesIO()
        with warnings.catch_warnings(), zipfile.ZipFile(stream, "w") as archive:
            warnings.simplefilter("ignore", UserWarning)
            for name, content in API_FILES.items():
                archive.writestr(name, content)
            archive.writestr("Project1.Api.dll", b"duplicate")
        with zipfile.ZipFile(io.BytesIO(stream.getvalue())) as archive, self.assertRaises(deploy.DeploymentError):
            deploy.validate_members(archive, "api")
        with self.assertRaises(deploy.DeploymentError):
            self.validate({**API_FILES, "project1.api.DLL": b"duplicate"})

    def test_reject_file_directory_conflicts_in_either_order(self):
        for extra in ({"a": b"file", "a/b": b"child"}, {"a/b": b"child", "a": b"file"}):
            with self.assertRaises(deploy.DeploymentError):
                self.validate({**API_FILES, **extra})

    def test_reject_symlink_special_files_and_privileged_mode(self):
        for mode in (stat.S_IFLNK | 0o777, stat.S_IFIFO | 0o600, stat.S_IFREG | 0o4755):
            stream = io.BytesIO()
            with zipfile.ZipFile(stream, "w") as archive:
                for name, content in API_FILES.items():
                    archive.writestr(name, content)
                info = zipfile.ZipInfo("evil")
                info.create_system = 3
                info.external_attr = mode << 16
                archive.writestr(info, b"target")
            with zipfile.ZipFile(io.BytesIO(stream.getvalue())) as archive, self.assertRaises(deploy.DeploymentError):
                deploy.validate_members(archive, "api")

    def test_reject_encryption_and_unsupported_compression_metadata(self):
        for attribute, value in (("flag_bits", 1), ("compress_type", zipfile.ZIP_BZIP2)):
            with zipfile.ZipFile(io.BytesIO(zip_bytes(API_FILES))) as archive:
                setattr(archive.filelist[0], attribute, value)
                with self.assertRaises(deploy.DeploymentError):
                    deploy.validate_members(archive, "api")

    def test_reject_archive_file_and_expanded_size_limits(self):
        for setting, value in (("MAX_MEMBERS", 2), ("MAX_FILE_BYTES", 1), ("MAX_EXPANDED_BYTES", 1)):
            with mock.patch.object(deploy, setting, value), self.assertRaises(deploy.DeploymentError):
                self.validate(API_FILES)

    def test_reject_empty_missing_wrapped_and_empty_required_files(self):
        cases = [{}, {"api/" + name: value for name, value in API_FILES.items()},
                 {name: value for name, value in API_FILES.items() if name != "Project1.Api.dll"},
                 {**API_FILES, "Project1.Api.dll": b""}]
        for files in cases:
            with self.assertRaises(deploy.DeploymentError):
                self.validate(files)
        for files in ({"index.html": b"x"}, {"dist/" + name: value for name, value in FRONTEND_FILES.items()}):
            with self.assertRaises(deploy.DeploymentError):
                self.validate(files, "frontend")

    def test_reject_invalid_pe_and_runtime_config(self):
        cases = [{**API_FILES, "Project1.Api.dll": b"not-an-assembly"},
                 {**API_FILES, "Project1.Api.runtimeconfig.json": b"invalid JSON"},
                 {**API_FILES, "Project1.Api.runtimeconfig.json": b'{"runtimeOptions":[]}'},
                 {**API_FILES, "Project1.Api.runtimeconfig.json": b'{"runtimeOptions":{"tfm":"net9.0"}}'}]
        for index, files in enumerate(cases):
            snapshot = self.root / f"invalid-{index}.zip"
            snapshot.write_bytes(zip_bytes(files))
            with self.assertRaises(deploy.DeploymentError):
                deploy.extract_archive(snapshot, self.root / f"candidate-{index}", "api")

    def test_reject_corrupt_zip_and_crc(self):
        target = self.root / "corrupt.zip"
        target.write_bytes(b"not a ZIP")
        with self.assertRaises(deploy.DeploymentError):
            deploy.extract_archive(target, self.root / "candidate", "api")
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, "w", compression=zipfile.ZIP_STORED) as archive:
            for name, content in API_FILES.items():
                archive.writestr(name, content)
        target.write_bytes(stream.getvalue().replace(b"MZ-test-assembly", b"MZ-bad!-assembly"))
        with self.assertRaises(deploy.DeploymentError):
            deploy.extract_archive(target, self.root / "crc-candidate", "api")

    def test_never_reuse_existing_candidate_directory(self):
        target = self.root / "candidate"
        target.mkdir()
        (target / "keep").write_text("untouched")
        with self.assertRaises(FileExistsError):
            self.extract(API_FILES)
        self.assertEqual((target / "keep").read_text(), "untouched")

    def test_permissions_are_fixed_not_taken_from_archive(self):
        target = self.extract(FRONTEND_FILES, "frontend")
        with mock.patch.object(deploy.os, "chown", create=True) as owner, mock.patch.object(deploy.os, "chmod") as permissions:
            deploy.set_runtime_permissions(target, 33)
            self.assertTrue(all(call.args[1:] == (0, 33) for call in owner.call_args_list))
            for call in permissions.call_args_list:
                expected = 0o750 if Path(call.args[0]).is_dir() else 0o640
                self.assertEqual(call.args[1], expected)


class FakeRuntime:
    upload_uid = 1001
    api_gid = 42
    frontend_gid = 33

    def __init__(self, health_fail=(), start_fail=(), stop_fail=(), preflight_fail=False):
        self.events = []
        self.health_fail = set(health_fail)
        self.start_fail = set(start_fail)
        self.stop_fail = set(stop_fail)
        self.preflight_fail = preflight_fail
        self.counts = {"health": 0, "start": 0, "stop": 0}

    def preflight(self, layout):
        self.events.append("preflight")
        if self.preflight_fail:
            raise deploy.DeploymentError("fake preflight failure")

    def event(self, name, failures):
        self.events.append(name)
        self.counts[name] += 1
        if self.counts[name] in failures:
            raise deploy.DeploymentError(f"fake {name} failure")

    def wait_healthy(self, digest):
        self.event("health", self.health_fail)

    def stop(self):
        self.event("stop", self.stop_fail)

    def start(self):
        self.event("start", self.start_fail)


class TransactionTests(TempCase):
    def setUp(self):
        super().setUp()
        self.layout = deploy.Layout(self.root / "opt/api", self.root / "www/site",
                                    self.root / "uploads", self.root / "state")
        for directory in (self.layout.api, self.layout.frontend, self.layout.uploads / COMMIT, self.layout.state):
            directory.mkdir(parents=True)
        (self.layout.api / "old.dll").write_bytes(b"old API")
        (self.layout.frontend / "index.html").write_bytes(b"old frontend")
        self.settings = self.root / "etc/project1/project1.env"
        self.settings.parent.mkdir(parents=True)
        self.settings.write_bytes(b"FAKE SETTINGS MUST REMAIN UNCHANGED")
        self.database = self.root / "database.bin"
        self.database.write_bytes(b"FAKE DB MUST REMAIN UNCHANGED")
        self.request = self.packages()
        def sandbox_snapshot(source, target, digest, uid):
            with source.open("rb") as stream:
                deploy.copy_and_verify(stream, target, digest)
        self.patchers = [mock.patch.object(deploy, "check_layout"),
                         mock.patch.object(deploy, "snapshot_upload", side_effect=sandbox_snapshot),
                         mock.patch.object(deploy, "set_runtime_permissions")]
        for patcher in self.patchers:
            patcher.start()
            self.addCleanup(patcher.stop)

    def packages(self, api=None, frontend=None):
        digests = []
        for kind, files in (("api", API_FILES if api is None else api),
                            ("frontend", FRONTEND_FILES if frontend is None else frontend)):
            content = zip_bytes(files)
            (self.layout.uploads / COMMIT / f"{kind}.zip").write_bytes(content)
            digests.append(hashlib.sha256(content).hexdigest())
        return deploy.Request(COMMIT, *digests)

    def assert_previous(self):
        self.assertEqual((self.layout.api / "old.dll").read_bytes(), b"old API")
        self.assertEqual((self.layout.frontend / "index.html").read_bytes(), b"old frontend")
        self.assertEqual(self.settings.read_bytes(), b"FAKE SETTINGS MUST REMAIN UNCHANGED")
        self.assertEqual(self.database.read_bytes(), b"FAKE DB MUST REMAIN UNCHANGED")

    def records(self):
        return [json.loads(path.read_text()) for path in self.layout.state.glob("*/transaction.json")]

    def test_success_keeps_old_versions_and_settings_database(self):
        runtime = FakeRuntime()
        attempt = deploy.deploy(self.request, self.layout, runtime)
        self.assertEqual(runtime.events, ["preflight", "health", "stop", "start", "health"])
        self.assertEqual((self.layout.api / "Project1.Api.dll").read_bytes(), API_FILES["Project1.Api.dll"])
        self.assertEqual((self.layout.frontend / "index.html").read_bytes(), FRONTEND_FILES["index.html"])
        self.assertEqual((self.layout.api.parent / f".api-backup-{attempt}/old.dll").read_bytes(), b"old API")
        self.assertEqual((self.layout.frontend.parent / f".site-backup-{attempt}/index.html").read_bytes(), b"old frontend")
        self.assertFalse((self.layout.state / "pending.json").exists())
        self.assertEqual(self.records()[0]["phase"], "committed")
        self.assertEqual(self.settings.read_bytes(), b"FAKE SETTINGS MUST REMAIN UNCHANGED")
        self.assertEqual(self.database.read_bytes(), b"FAKE DB MUST REMAIN UNCHANGED")

    def test_bad_digest_does_not_stop_or_modify_live_directories(self):
        runtime = FakeRuntime()
        with self.assertRaises(deploy.DeploymentError):
            deploy.deploy(deploy.Request(COMMIT, "b" * 64, self.request.frontend_digest), self.layout, runtime)
        self.assertNotIn("stop", runtime.events)
        self.assert_previous()

    def test_invalid_second_package_does_not_stop_or_modify_live_directories(self):
        request = self.packages(frontend={"index.html": b"missing assets"})
        runtime = FakeRuntime()
        with self.assertRaises(deploy.DeploymentError):
            deploy.deploy(request, self.layout, runtime)
        self.assertNotIn("stop", runtime.events)
        self.assert_previous()

    def test_service_configuration_failure_blocks_deployment(self):
        runtime = FakeRuntime(preflight_fail=True)
        with self.assertRaises(deploy.DeploymentError):
            deploy.deploy(self.request, self.layout, runtime)
        self.assertEqual(runtime.events, ["preflight"])
        self.assert_previous()

    def test_unhealthy_existing_application_blocks_deployment(self):
        runtime = FakeRuntime(health_fail={1})
        with self.assertRaises(deploy.DeploymentError):
            deploy.deploy(self.request, self.layout, runtime)
        self.assertNotIn("stop", runtime.events)
        self.assert_previous()

    def test_health_failure_restores_both_versions(self):
        with self.assertRaisesRegex(deploy.DeploymentError, "previous code was restored"):
            deploy.deploy(self.request, self.layout, FakeRuntime(health_fail={2}))
        self.assert_previous()
        self.assertFalse((self.layout.state / "pending.json").exists())
        self.assertEqual(self.records()[0]["phase"], "rolled_back")
        self.assertTrue(list(self.layout.api.parent.glob(".api-failed-*")))

    def test_new_service_start_failure_restores_old_code(self):
        with self.assertRaisesRegex(deploy.DeploymentError, "previous code was restored"):
            deploy.deploy(self.request, self.layout, FakeRuntime(start_fail={1}))
        self.assert_previous()

    def test_stop_failure_never_switches_live_directories(self):
        with self.assertRaisesRegex(deploy.DeploymentError, "previous code was restored"):
            deploy.deploy(self.request, self.layout, FakeRuntime(stop_fail={1}))
        self.assert_previous()
        self.assertFalse(list(self.layout.api.parent.glob(".api-backup-*")))

    def test_each_partial_rename_failure_restores_previous_layout(self):
        for failure_call in (1, 2, 3, 4):
            with self.subTest(failure_call=failure_call):
                original = deploy.rename_directory
                count = 0
                def interrupted(source, destination):
                    nonlocal count
                    count += 1
                    if count == failure_call:
                        raise OSError("simulated rename failure")
                    return original(source, destination)
                with mock.patch.object(deploy, "rename_directory", side_effect=interrupted), self.assertRaisesRegex(deploy.DeploymentError, "previous code was restored"):
                    deploy.deploy(self.request, self.layout, FakeRuntime())
                self.assert_previous()
                self.assertFalse((self.layout.state / "pending.json").exists())

    def test_failed_rollback_retains_pending_and_blocks_retry(self):
        with self.assertRaisesRegex(deploy.DeploymentError, "Administrator recovery"):
            deploy.deploy(self.request, self.layout, FakeRuntime(health_fail={2, 3}))
        self.assert_previous()
        self.assertEqual(json.loads((self.layout.state / "pending.json").read_text())["phase"], "recovery_required")
        runtime = FakeRuntime()
        with self.assertRaisesRegex(deploy.DeploymentError, "administrator recovery"):
            deploy.deploy(self.request, self.layout, runtime)
        self.assertEqual(runtime.events, [])

    def test_stop_during_rollback_failure_retains_backups_without_more_renames(self):
        with self.assertRaisesRegex(deploy.DeploymentError, "Administrator recovery"):
            deploy.deploy(self.request, self.layout, FakeRuntime(health_fail={2}, stop_fail={2}))
        self.assertTrue((self.layout.state / "pending.json").exists())
        self.assertTrue(list(self.layout.api.parent.glob(".api-backup-*")))
        self.assertTrue((self.layout.api / "Project1.Api.dll").exists())

    def test_forced_interruption_retains_journal_even_when_live_directory_is_missing(self):
        original = deploy.rename_directory
        count = 0
        def interrupted(source, destination):
            nonlocal count
            count += 1
            if count == 2:
                raise SystemExit("simulated hard interruption")
            return original(source, destination)
        with mock.patch.object(deploy, "rename_directory", side_effect=interrupted), self.assertRaises(SystemExit):
            deploy.deploy(self.request, self.layout, FakeRuntime())
        self.assertTrue((self.layout.state / "pending.json").exists())
        self.assertFalse(self.layout.api.exists())
        with self.assertRaisesRegex(deploy.DeploymentError, "administrator recovery"):
            deploy.deploy(self.request, self.layout, FakeRuntime())

    def test_rename_never_overwrites_an_existing_destination(self):
        source, target = self.root / "source", self.root / "target"
        source.mkdir()
        target.mkdir()
        with self.assertRaises(deploy.DeploymentError):
            deploy.rename_directory(source, target)
        self.assertTrue(source.exists())
        self.assertTrue(target.exists())

    def test_local_persistent_settings_fail_closed(self):
        # Call the real preflight checker, but mock only root metadata/disk checks.
        (self.layout.api / "appsettings.Production.json").write_bytes(b"DO NOT DISPLAY")
        with mock.patch.object(deploy, "validate_live_tree"), mock.patch.object(deploy, "require_root_directory"), mock.patch.object(deploy.shutil, "disk_usage", return_value=SimpleNamespace(free=10 * 1024**3)), mock.patch.object(Path, "stat", return_value=SimpleNamespace(st_mode=0o700)):
            with self.assertRaises(deploy.DeploymentError):
                REAL_CHECK_LAYOUT(self.layout)


REAL_CHECK_LAYOUT = deploy.check_layout


class RuntimeTests(unittest.TestCase):
    def runtime(self):
        return object.__new__(deploy.ServiceRuntime)

    def test_service_control_has_fixed_binary_no_shell_and_sanitized_environment(self):
        runtime = self.runtime()
        with mock.patch.object(deploy.subprocess, "run", return_value=SimpleNamespace(stdout="")) as run:
            runtime.stop()
            runtime.start()
        self.assertEqual(run.call_args_list[0].args[0], ["/usr/bin/systemctl", "stop", deploy.SERVICE])
        self.assertEqual(run.call_args_list[1].args[0], ["/usr/bin/systemctl", "start", deploy.SERVICE])
        for call in run.call_args_list:
            self.assertNotIn("shell", call.kwargs)
            self.assertEqual(call.kwargs["env"], deploy.SAFE_ENV)
            self.assertEqual(call.kwargs["timeout"], 45)

    def test_service_failure_does_not_expose_subprocess_output(self):
        with mock.patch.object(deploy.subprocess, "run", side_effect=deploy.subprocess.CalledProcessError(1, "fake", stderr="PRIVATE FIXTURE")), self.assertRaises(deploy.DeploymentError) as failure:
            self.runtime().start()
        self.assertNotIn("PRIVATE FIXTURE", str(failure.exception))

    def test_health_checks_api_proxy_and_exact_frontend_digest(self):
        runtime = self.runtime()
        index = b"frontend"
        with mock.patch.object(runtime, "read_http", side_effect=[b'{"status":"ok"}', b'{"status":"ok"}', index]) as http, mock.patch.object(runtime, "systemctl", return_value="active\n"):
            runtime.wait_healthy(hashlib.sha256(index).hexdigest())
        self.assertEqual([call.args[0] for call in http.call_args_list], [
            "http://127.0.0.1:5000/api/health", "http://127.0.0.1/api/health", "http://127.0.0.1/index.html"])

    def test_wrong_json_or_frontend_body_fails_without_logging_body(self):
        for responses in ([b'{"status":"bad","secret":"PRIVATE FIXTURE"}'],
                          [b"[]"], [b'{"status":"ok"}', b'{"status":"ok"}', b"wrong frontend"]):
            runtime = self.runtime()
            with mock.patch.object(runtime, "read_http", side_effect=responses), mock.patch.object(deploy.time, "monotonic", side_effect=[0, 61]), self.assertRaises(deploy.DeploymentError) as failure:
                runtime.wait_healthy("b" * 64)
            self.assertNotIn("PRIVATE FIXTURE", str(failure.exception))

    def test_health_redirect_is_rejected(self):
        with self.assertRaises(deploy.DeploymentError):
            deploy.NoRedirect().redirect_request(None, None, 302, "redirect", {}, "http://example.com")

    def test_service_configuration_is_checked_before_start_stop(self):
        runtime = self.runtime()
        layout = deploy.Layout()
        good = (f"User=project1\nGroup=project1\nWorkingDirectory={layout.api}\n"
                "NoNewPrivileges=yes\nFragmentPath=/etc/systemd/system/project1-api.service\n"
                "DropInPaths=\nActiveState=active\n")
        with mock.patch.object(deploy, "require_root_file"), mock.patch.object(runtime, "systemctl", return_value=good):
            runtime.preflight(layout)
        for original, changed in (("User=project1", "User=root"), ("Group=project1", "Group=root"),
                                  ("NoNewPrivileges=yes", "NoNewPrivileges=no"),
                                  ("DropInPaths=", "DropInPaths=/tmp/override.conf"),
                                  ("ActiveState=active", "ActiveState=failed")):
            with mock.patch.object(deploy, "require_root_file"), mock.patch.object(runtime, "systemctl", return_value=good.replace(original, changed)), self.assertRaises(deploy.DeploymentError):
                runtime.preflight(layout)

    def test_runtime_and_deployment_accounts_and_groups_must_not_be_root(self):
        for target in ("project1_deploy", "project1", "api-group", "frontend-group"):
            users = SimpleNamespace(getpwnam=lambda name: SimpleNamespace(pw_uid=0 if name == target else 1001))
            groups = SimpleNamespace(getgrnam=lambda name: SimpleNamespace(
                gr_gid=0 if ((name == "project1" and target == "api-group")
                             or (name == "www-data" and target == "frontend-group")) else 33))
            with mock.patch.dict(sys.modules, {"pwd": users, "grp": groups}), self.assertRaises(deploy.DeploymentError):
                deploy.ServiceRuntime()

    def test_http_status_and_response_size_are_checked(self):
        from contextlib import nullcontext

        for status, body in ((500, b"error"), (200, b"oversized")):
            runtime = self.runtime()
            response = mock.MagicMock()
            response.__enter__.return_value = response
            response.status = status
            response.read.return_value = body
            runtime.http = SimpleNamespace(open=mock.Mock(return_value=response))
            with mock.patch.object(deploy, "http_deadline", return_value=nullcontext()), self.assertRaises(deploy.DeploymentError):
                runtime.read_http("http://127.0.0.1/api/health", 4)


@unittest.skipUnless(sys.platform == "linux", "Actual no-follow opens / chmod / flock require Linux")
class LinuxFilesystemTests(TempCase):
    def setUp(self):
        # /tmp is deliberately world-writable and would fail the upload ancestry
        # guard. A private directory in the user's home models the real upload path.
        self.temporary = tempfile.TemporaryDirectory(dir=Path.home())
        self.root = Path(self.temporary.name)
        self.addCleanup(self.temporary.cleanup)

    def test_real_snapshot_and_link_rejection(self):
        source = self.root / "api.zip"
        source.write_bytes(b"fixture")
        source.chmod(0o600)
        with deploy.open_upload(source, os.getuid()) as stream:
            self.assertEqual(stream.read(), b"fixture")
        link = self.root / "link.zip"
        link.symlink_to(source)
        with self.assertRaises(OSError):
            with deploy.open_upload(link, os.getuid()):
                self.fail("symlink was opened")
        os.link(source, self.root / "hardlink.zip")
        with self.assertRaises(deploy.DeploymentError):
            with deploy.open_upload(source, os.getuid()):
                self.fail("hardlink was accepted")

    def test_fifo_is_rejected_without_blocking(self):
        fifo = self.root / "fifo"
        os.mkfifo(fifo, 0o600)
        with self.assertRaises(deploy.DeploymentError):
            with deploy.open_upload(fifo, os.getuid()):
                self.fail("FIFO was accepted")

    def test_actual_output_modes(self):
        target = self.root / "output"
        target.mkdir()
        (target / "file").write_bytes(b"fixture")
        with mock.patch.object(deploy.os, "chown"):
            deploy.set_runtime_permissions(target, os.getgid())
        self.assertEqual(stat.S_IMODE(target.stat().st_mode), 0o750)
        self.assertEqual(stat.S_IMODE((target / "file").stat().st_mode), 0o640)

    def test_actual_lock_rejects_second_holder(self):
        # flock's ownership invariant is mocked only when tests run as a non-root user.
        original = deploy.os.fstat
        def root_metadata(descriptor):
            actual = original(descriptor)
            return metadata(mode=actual.st_mode, uid=0, links=actual.st_nlink, size=actual.st_size)
        with mock.patch.object(deploy.os, "fstat", side_effect=root_metadata):
            with deploy.deployment_lock(self.root):
                with self.assertRaisesRegex(deploy.DeploymentError, "already running"):
                    with deploy.deployment_lock(self.root):
                        self.fail("second lock was acquired")

    def test_health_deadline_interrupts_a_slow_operation_and_resets_timer(self):
        import signal

        previous = signal.getsignal(signal.SIGALRM)
        with self.assertRaises(TimeoutError):
            with deploy.http_deadline(0.02):
                time.sleep(1)
        self.assertEqual(signal.getsignal(signal.SIGALRM), previous)
        self.assertEqual(signal.getitimer(signal.ITIMER_REAL)[0], 0)


if __name__ == "__main__":
    unittest.main()
