"""Portable guard/fixture checks. Never install anything or open SQL locally."""
import copy
import ast
from contextlib import contextmanager, ExitStack
import errno
import io
import inspect
import os
from pathlib import Path
import sys
import tempfile
import subprocess
import stat
from types import SimpleNamespace
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import ci_host_acceptance as acceptance
import production_migration as production
from verify_migration_artifact import verify
from test_migration_precheck import files_fixture, IDS


class AcceptanceGuardTests(unittest.TestCase):
    def environment(self):
        return {"GITHUB_ACTIONS": "true", "PROJECT1_HOST_ACCEPTANCE": acceptance.MARKER,
                "GITHUB_REPOSITORY": "shunbin2310/Project1", "GITHUB_JOB": "migration-host-acceptance",
                "RUNNER_ENVIRONMENT": "github-hosted", "GITHUB_RUN_ID": "42"}

    def test_only_explicit_root_github_hosted_job_can_pass(self):
        environment = self.environment()
        acceptance.require_ci(environment, "linux", 0)
        for field in environment:
            changed = copy.deepcopy(environment)
            changed.pop(field)
            with self.subTest(field=field), self.assertRaises(RuntimeError):
                acceptance.require_ci(changed, "linux", 0)
        for platform, uid in (("win32", 0), ("linux", 1000)):
            with self.assertRaises(RuntimeError):
                acceptance.require_ci(environment, platform, uid)
        environment["RUNNER_ENVIRONMENT"] = "self-hosted"
        with self.assertRaises(RuntimeError):
            acceptance.require_ci(environment, "linux", 0)

    def test_refused_cli_has_no_install_docker_or_sql_side_effects(self):
        with mock.patch.dict(os.environ, {}, clear=True), mock.patch.object(acceptance, "acceptance") as install, \
                mock.patch.object(acceptance.subprocess, "run") as child, mock.patch("sys.stderr"), mock.patch("sys.stdout"):
            self.assertEqual(acceptance.main(["a", "b", "c", "d"]), 1)
        install.assert_not_called()
        child.assert_not_called()

    def test_fixture_provenance_is_explicit_but_verified_bytes_still_checked(self):
        client = acceptance.FixtureClient(files_fixture())
        with tempfile.TemporaryDirectory() as temporary:
            manifest = verify(client, acceptance.RUN, acceptance.SHA, Path(temporary) / "package", 1)
        self.assertEqual(manifest["migrations"], IDS)
        client.item["digest"] = "sha256:" + "e" * 64
        with tempfile.TemporaryDirectory() as temporary, self.assertRaises(Exception):
            verify(client, acceptance.RUN, acceptance.SHA, Path(temporary) / "bad", 1)

    def test_workflow_is_separate_ephemeral_vm_without_production_channels(self):
        workflow = (Path(__file__).resolve().parents[3] / ".github/workflows/ci.yml").read_text(encoding="utf-8")
        backend = workflow.split("  backend:\n", 1)[1].split("  migration-host-acceptance:\n", 1)[0]
        self.assertIn("needs: migration-host-acceptance", backend)
        self.assertIn("if: ${{ always() }}", backend)
        self.assertIn('run: test "$ACCEPTANCE_RESULT" = success', backend)
        self.assertLess(backend.index("Require isolated host acceptance"), backend.index("Check out code"))
        job = workflow.split("  migration-host-acceptance:\n", 1)[1].split("  frontend:\n", 1)[0]
        self.assertIn("runs-on: ubuntu-24.04", job)
        self.assertIn("persist-credentials: false", job)
        self.assertIn("/usr/bin/env -i", job)
        self.assertIn('RUNNER_ENVIRONMENT="$RUNNER_ENVIRONMENT"', job)
        for forbidden in ("secrets.", "github.token", "uses: tailscale/", "self-hosted", "ssh -", "scp "):
            # Comments explicitly warn against self-hosted runners; remove comments.
            executable = "\n".join(line for line in job.splitlines() if not line.lstrip().startswith("#"))
            self.assertNotIn(forbidden, executable)
        self.assertNotIn("ci_host_acceptance", (Path(__file__).resolve().parents[3] / ".github/workflows/deploy-manual.yml").read_text(encoding="utf-8"))


class AcceptanceSudoersTests(unittest.TestCase):
    @contextmanager
    def runner_file(self, *, before=None, after=None, parents=None, current=None):
        before = before or SimpleNamespace(st_uid=0, st_gid=0, st_mode=stat.S_IFREG | 0o644,
                                          st_nlink=1, st_dev=1, st_ino=2)
        after = after or SimpleNamespace(**{**vars(before), "st_mode": stat.S_IFREG | 0o440})
        directory = SimpleNamespace(st_uid=0, st_gid=0, st_mode=stat.S_IFDIR | 0o755)
        parents = parents or [directory] * 3
        with ExitStack() as stack:
            stack.enter_context(mock.patch.dict(os.environ, AcceptanceGuardTests().environment(), clear=True))
            stack.enter_context(mock.patch.object(acceptance.sys, "platform", "linux"))
            stack.enter_context(mock.patch.object(acceptance.os, "geteuid", return_value=0, create=True))
            # POSIX-only constants/functions are mocked for portable Windows tests.
            for flag, fallback in (("O_NOFOLLOW", 0x100000), ("O_NONBLOCK", 0x200000), ("O_CLOEXEC", 0x400000)):
                stack.enter_context(mock.patch.object(acceptance.os, flag, getattr(os, flag, fallback), create=True))
            calls = {}
            for name in ("open", "fstat", "fchmod", "close"):
                calls[name] = stack.enter_context(mock.patch.object(acceptance.os, name, create=True))
            calls["open"].return_value = 23
            calls["fstat"].side_effect = [before, after]
            calls["lstat"] = stack.enter_context(mock.patch.object(acceptance.Path, "lstat",
                                                                  side_effect=[*parents, current or after]))
            calls["output"] = stack.enter_context(mock.patch("sys.stdout", io.StringIO()))
            yield SimpleNamespace(**calls)

    def test_runner_mode_fix_uses_exact_path_safe_descriptor_and_no_content_write(self):
        with self.runner_file() as calls:
            acceptance.normalize_runner_sudoers()
            calls.open.assert_called_once_with("/etc/sudoers.d/runner",
                os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK | os.O_CLOEXEC)
            calls.fchmod.assert_called_once_with(23, 0o440)
            calls.close.assert_called_once_with(23)
            self.assertIn("before_mode=0644", calls.output.getvalue())
            self.assertIn("after_mode=0440; rules unchanged", calls.output.getvalue())

    def test_already_correct_runner_mode_is_not_modified(self):
        before = SimpleNamespace(st_uid=0, st_gid=0, st_mode=stat.S_IFREG | 0o440,
                                 st_nlink=1, st_dev=1, st_ino=2)
        with self.runner_file(before=before) as calls:
            acceptance.normalize_runner_sudoers()
            calls.fchmod.assert_not_called()
            calls.close.assert_called_once_with(23)

    def test_runner_fix_rechecks_ci_boundary_before_any_filesystem_access(self):
        for invalid in ({}, {**AcceptanceGuardTests().environment(), "RUNNER_ENVIRONMENT": "self-hosted"}):
            with self.runner_file() as calls, mock.patch.dict(os.environ, invalid, clear=True), \
                    self.assertRaises(RuntimeError):
                acceptance.normalize_runner_sudoers()
            calls.lstat.assert_not_called()
            calls.open.assert_not_called()
            calls.fchmod.assert_not_called()
        with self.runner_file() as calls, mock.patch.object(acceptance.os, "geteuid", return_value=1000), \
                self.assertRaises(RuntimeError):
            acceptance.normalize_runner_sudoers()
        calls.lstat.assert_not_called()
        calls.open.assert_not_called()

    def test_untrusted_runner_parent_is_rejected_before_open(self):
        for field, value in (("st_uid", 1000), ("st_gid", 1000),
                             ("st_mode", stat.S_IFLNK | 0o755),
                             ("st_mode", stat.S_IFDIR | 0o775)):
            parent = dict(st_uid=0, st_gid=0, st_mode=stat.S_IFDIR | 0o755)
            parent[field] = value
            with self.subTest(field=field), self.runner_file(parents=[SimpleNamespace(**parent)] * 3) as calls, \
                    self.assertRaises(RuntimeError):
                acceptance.normalize_runner_sudoers()
            calls.open.assert_not_called()
            calls.fchmod.assert_not_called()

    def test_untrusted_runner_file_is_closed_without_chmod(self):
        for field, value in (("st_uid", 1000), ("st_gid", 1000), ("st_nlink", 2),
                             ("st_mode", stat.S_IFLNK | 0o644),
                             ("st_mode", stat.S_IFDIR | 0o644), ("st_mode", stat.S_IFIFO | 0o644)):
            metadata = dict(st_uid=0, st_gid=0, st_mode=stat.S_IFREG | 0o644,
                            st_nlink=1, st_dev=1, st_ino=2)
            metadata[field] = value
            with self.subTest(field=field), self.runner_file(before=SimpleNamespace(**metadata)) as calls, \
                    self.assertRaises(RuntimeError):
                acceptance.normalize_runner_sudoers()
            calls.fchmod.assert_not_called()
            calls.close.assert_called_once_with(23)

    def test_open_failure_or_chmod_failure_aborts_without_success_log(self):
        with self.runner_file() as calls:
            calls.open.side_effect = OSError(errno.ELOOP, "private path")
            with self.assertRaises(OSError):
                acceptance.normalize_runner_sudoers()
            calls.fchmod.assert_not_called()
            calls.close.assert_not_called()
            self.assertEqual(calls.output.getvalue(), "")
        with self.runner_file() as calls:
            calls.fchmod.side_effect = OSError(errno.EPERM, "private path")
            with self.assertRaises(OSError):
                acceptance.normalize_runner_sudoers()
            calls.close.assert_called_once_with(23)
            self.assertNotIn("after_mode", calls.output.getvalue())
            self.assertNotIn("private", calls.output.getvalue())

    def test_post_chmod_metadata_and_path_identity_are_verified(self):
        expected = dict(st_uid=0, st_gid=0, st_mode=stat.S_IFREG | 0o440,
                        st_nlink=1, st_dev=1, st_ino=2)
        for field, value in (("st_uid", 1000), ("st_gid", 1000), ("st_nlink", 2),
                             ("st_mode", stat.S_IFREG | 0o644)):
            changed = SimpleNamespace(**{**expected, field: value})
            with self.subTest(field=field), self.runner_file(after=changed) as calls, self.assertRaises(RuntimeError):
                acceptance.normalize_runner_sudoers()
            calls.close.assert_called_once_with(23)
            self.assertNotIn("after_mode", calls.output.getvalue())
        for field, value in (("st_ino", 99), ("st_dev", 99), ("st_mode", stat.S_IFLNK | 0o440)):
            changed = SimpleNamespace(**{**expected, field: value})
            with self.subTest(field=field), self.runner_file(current=changed) as calls, self.assertRaises(RuntimeError):
                acceptance.normalize_runner_sudoers()
            calls.close.assert_called_once_with(23)
            self.assertNotIn("after_mode", calls.output.getvalue())

    def test_classifies_owner_permissions_syntax_and_missing_file_without_raw_text(self):
        messages = (
            b"/etc/sudoers: bad permissions, should be mode 0440\n"
            b"/etc/sudoers.d/runner: wrong owner (uid, gid) should be (0, 0)\n",
            b"/etc/sudoers.d/project1-migration-execute:3:18: syntax error\n"
            b"visudo: unable to open /etc/sudoers.d/90-cloud-init-users: No such file or directory\n",
        )
        self.assertEqual(acceptance.classify_visudo_output(*messages), [
            ("cloud-init", "file-unavailable"), ("main", "bad-permissions"),
            ("migration-execute", "syntax-error"), ("runner", "wrong-owner"),
        ])

    def test_unknown_filenames_duplicate_lines_and_injection_cannot_leak(self):
        secret = b"private_password"
        line = b"/etc/sudoers.d/" + secret + b": bad permissions, should be mode 0440\n"
        stderr = line + line + b"::error::secret\nraw SQL\n/etc/sudoers:1: syntax error EXTRA secret\n"
        output = io.StringIO()
        with mock.patch("sys.stderr", output):
            acceptance.report_visudo_failure("visudo-installed", b"credential raw stdout", stderr)
        self.assertEqual(output.getvalue(),
                         "VISUDO command=visudo-installed file=other-include category=bad-permissions\n")
        for forbidden in (secret.decode(), "raw SQL", "::error::", "/etc/sudoers", "credential"):
            self.assertNotIn(forbidden, output.getvalue())

    def test_unknown_localized_oversized_or_nonbyte_output_is_unclassified(self):
        for body in (b"unknown secret message", "raw string", None,
                     b"x" * 131073, b"\xff secret", b"visudo: localized error"):
            with self.subTest(body_type=type(body).__name__):
                output = io.StringIO()
                with mock.patch("sys.stderr", output):
                    acceptance.report_visudo_failure("visudo-baseline", body, body)
                self.assertEqual(output.getvalue(),
                                 "VISUDO command=visudo-baseline file=unknown category=unclassified\n")

    def test_only_exact_visudo_checks_are_classified_and_failure_still_raised(self):
        child = subprocess.CompletedProcess([], 1, b"secret stdout",
                    b"/etc/sudoers: bad permissions, should be mode 0440\nsecret stderr")
        for label, arguments in acceptance.VISUDO_CHECKS.items():
            with self.subTest(label=label), mock.patch.object(acceptance.subprocess, "run", return_value=child), \
                    mock.patch("sys.stderr", io.StringIO()) as output, \
                    self.assertRaises(acceptance.CommandFailure) as caught:
                acceptance.run(list(arguments), label=label)
            self.assertEqual(caught.exception.exit_code, 1)
            self.assertIn("file=main category=bad-permissions", output.getvalue())
            self.assertNotIn("secret", output.getvalue())
        for arguments, options in ((["private SQL child"], {}),
                                   (["/usr/sbin/visudo", "-c"], {"body": b"secret stdin"}),
                                   (["/usr/sbin/visudo", "-c"], {"environment": {"SECRET": "private"}})):
            with mock.patch.object(acceptance.subprocess, "run", return_value=child), \
                    mock.patch("sys.stderr", io.StringIO()) as output, \
                    self.assertRaises(acceptance.CommandFailure):
                acceptance.run(arguments, label="visudo-installed", **options)
            self.assertEqual(output.getvalue(), "")
        with self.assertRaises(ValueError):
            acceptance.report_visudo_failure("secret\n::error::", b"", b"")

    def test_success_and_expected_denial_do_not_emit_visudo_diagnostics(self):
        for code, succeeds in ((0, True), (1, False)):
            with mock.patch.object(acceptance.subprocess, "run", return_value=
                    subprocess.CompletedProcess([], code, b"unchanged", b"secret")), \
                    mock.patch("sys.stderr", io.StringIO()) as output:
                self.assertEqual(acceptance.run(["/usr/sbin/visudo", "-c"],
                                 label="visudo-installed", succeeds=succeeds), b"unchanged")
            self.assertEqual(output.getvalue(), "")

    def test_installed_metadata_requires_root_regular_single_link_and_exact_mode(self):
        expected = dict(st_uid=0, st_gid=0, st_mode=stat.S_IFREG | 0o440, st_nlink=1)
        path = mock.Mock()
        path.lstat.return_value = SimpleNamespace(**expected)
        with mock.patch("sys.stdout", io.StringIO()) as output:
            acceptance.verify_sudoers_metadata(path)
        self.assertIn("uid=0 gid=0 mode=0440 links=1 regular=true", output.getvalue())
        for field, value in (("st_uid", 1000), ("st_gid", 1000), ("st_nlink", 2),
                             ("st_mode", stat.S_IFREG | 0o640),
                             ("st_mode", stat.S_IFLNK | 0o440), ("st_mode", stat.S_IFDIR | 0o440)):
            changed = dict(expected, **{field: value})
            path.lstat.return_value = SimpleNamespace(**changed)
            with self.subTest(field=field, value=value), mock.patch("sys.stdout", io.StringIO()), \
                    self.assertRaises(RuntimeError):
                acceptance.verify_sudoers_metadata(path)

    def test_baseline_failure_happens_before_any_installation_or_runtime_copy(self):
        absent = mock.Mock(side_effect=KeyError)
        modules = {"grp": SimpleNamespace(getgrnam=absent), "pwd": SimpleNamespace(getpwnam=absent)}
        with mock.patch.dict(sys.modules, modules), mock.patch.object(acceptance, "require_ci"), \
                mock.patch.object(acceptance.os, "geteuid", return_value=0, create=True), \
                mock.patch.object(acceptance.os.path, "lexists", return_value=False), \
                mock.patch.object(acceptance, "normalize_runner_sudoers") as normalize, \
                mock.patch.object(acceptance.os, "umask") as umask, \
                mock.patch.object(acceptance, "copy_tree") as copy_tree, \
                mock.patch.object(acceptance.shutil, "copyfile") as copyfile, \
                mock.patch.object(acceptance, "run", side_effect=acceptance.CommandFailure(
                    "visudo-baseline", "exit-status", exit_code=1)) as child, \
                mock.patch.object(acceptance, "_CURRENT_STAGE", "S00"), \
                mock.patch("sys.stdout", io.StringIO()), self.assertRaises(acceptance.CommandFailure):
            acceptance.acceptance(*(Path("unused") for _ in range(4)))
            self.fail("Baseline rejection must abort.")
        child.assert_called_once_with(["/usr/sbin/visudo", "-c"], label="visudo-baseline")
        normalize.assert_called_once_with()
        umask.assert_not_called()
        copy_tree.assert_not_called()
        copyfile.assert_not_called()

    def test_strict_file_check_and_global_check_remain_in_order_before_database_setup(self):
        source = Path(acceptance.__file__).read_text(encoding="utf-8").split("def acceptance(", 1)[1]
        self.assertLess(source.index("require_ci("), source.index("normalize_runner_sudoers()"))
        self.assertLess(source.index("normalize_runner_sudoers()"), source.index('label="visudo-baseline"'))
        self.assertLess(source.index('label="visudo-baseline"'), source.index("copy_tree("))
        self.assertLess(source.index("verify_sudoers_metadata(sudoers)"), source.index('label="visudo-file"'))
        self.assertLess(source.index('label="visudo-file"'), source.index('label="visudo-installed"'))
        self.assertLess(source.index('label="visudo-installed"'), source.index("CONFIG.mkdir"))
        self.assertEqual(acceptance.VISUDO_CHECKS["visudo-file"],
                         ("/usr/sbin/visudo", "-c", "-O", "-P", "-f", str(acceptance.SUDOERS)))


class AcceptanceInstallationTests(unittest.TestCase):
    @contextmanager
    def installation(self):
        production = SimpleNamespace(trusted=mock.Mock(), require_installation=mock.Mock())
        members = [acceptance.HOST / "Project1.MigrationHost.dll",
                   acceptance.HOST / "private_dependency.dll", acceptance.HOST / "runtimes"]
        runtime = acceptance.RUNTIME / "dotnet"
        with ExitStack() as stack:
            stack.enter_context(mock.patch.dict(os.environ, AcceptanceGuardTests().environment(), clear=True))
            stack.enter_context(mock.patch.object(acceptance.sys, "platform", "linux"))
            stack.enter_context(mock.patch.object(acceptance.os, "geteuid", return_value=0, create=True))
            stack.enter_context(mock.patch.object(acceptance, "_CURRENT_STAGE", "S17"))
            stack.enter_context(mock.patch.object(acceptance.Path, "rglob", return_value=members))
            stack.enter_context(mock.patch.object(acceptance.Path, "is_dir", autospec=True,
                                                 side_effect=lambda path: path == members[-1]))
            stack.enter_context(mock.patch.object(acceptance.Path, "resolve", return_value=runtime))
            metadata = stack.enter_context(mock.patch.object(acceptance.Path, "lstat", autospec=True,
                return_value=SimpleNamespace(st_uid=0, st_gid=0, st_mode=stat.S_IFDIR | 0o755, st_nlink=2)))
            output = stack.enter_context(mock.patch("sys.stdout", io.StringIO()))
            errors = stack.enter_context(mock.patch("sys.stderr", io.StringIO()))
            yield SimpleNamespace(production=production, members=members, runtime=runtime,
                                  metadata=metadata, output=output, errors=errors)

    def test_validation_calls_real_trust_interface_then_original_authoritative_gate(self):
        with self.installation() as fixture:
            acceptance.validate_fixed_installation(fixture.production)
            expected = [mock.call(acceptance.CONFIG, 0o700, directory=True),
                        mock.call(Path("/root"), None, directory=True),
                        mock.call(acceptance.HOST, 0o755, directory=True)]
            expected += [mock.call(path, None, directory=path == fixture.members[-1])
                         for path in sorted(fixture.members)]
            expected += [mock.call(fixture.members[0], 0o644, directory=False),
                         mock.call(fixture.runtime, None, directory=False),
                         mock.call(acceptance.LEDGER, 0o700, directory=True)]
            self.assertEqual(fixture.production.trusted.call_args_list, expected)
            fixture.production.require_installation.assert_called_once_with(enabled=False)
            self.assertEqual(acceptance._CURRENT_STAGE, "S18")
            self.assertIn("host_dependencies=3", fixture.output.getvalue())
            # Successful checks do not dump dependency metadata/content.
            fixture.metadata.assert_not_called()
            self.assertEqual(fixture.errors.getvalue(), "")

    def test_each_trust_failure_keeps_specific_stage_and_original_error(self):
        for call_number, stage in ((1, "S18C"), (2, "S18R"), (3, "S18H"),
                                   (5, "S18F"), (7, "S18D"), (8, "S18T"), (9, "S18L")):
            with self.subTest(stage=stage), self.installation() as fixture:
                original = RuntimeError("private password; raw SQL; ::error::injected")
                fixture.production.trusted.side_effect = [None] * (call_number - 1) + [original]
                with self.assertRaises(RuntimeError) as caught:
                    acceptance.validate_fixed_installation(fixture.production)
                self.assertIs(caught.exception, original)
                self.assertEqual(acceptance._CURRENT_STAGE, stage)
                fixture.production.require_installation.assert_not_called()
                acceptance.report_failure(caught.exception)
                combined = fixture.output.getvalue() + fixture.errors.getvalue()
                self.assertIn("INSTALL-METADATA", combined)
                self.assertIn(f"ERROR [{stage}]", combined)
                for forbidden in ("private password", "raw SQL", "::error::", "private_dependency"):
                    self.assertNotIn(forbidden, combined)
                if stage == "S18F":
                    self.assertIn("member=2", combined)

    def test_final_production_gate_cannot_be_bypassed_by_successful_diagnostics(self):
        with self.installation() as fixture:
            fixture.production.require_installation.side_effect = RuntimeError("secret final rejection")
            with self.assertRaises(RuntimeError):
                acceptance.validate_fixed_installation(fixture.production)
            self.assertEqual(acceptance._CURRENT_STAGE, "S18")
            fixture.production.require_installation.assert_called_once_with(enabled=False)

    def test_installation_guard_refuses_before_trust_checks_or_filesystem(self):
        with self.installation() as fixture, mock.patch.dict(os.environ, {}, clear=True):
            with self.assertRaises(RuntimeError):
                acceptance.validate_fixed_installation(fixture.production)
            fixture.production.trusted.assert_not_called()
            fixture.production.require_installation.assert_not_called()
            fixture.metadata.assert_not_called()
            self.assertEqual(acceptance._CURRENT_STAGE, "S17")

    def test_safe_metadata_identifies_fixed_ancestor_without_exposing_unknown_name(self):
        path = acceptance.HOST / "private_dependency.dll"
        with self.installation() as fixture:
            fixture.metadata.return_value = SimpleNamespace(st_uid=1001, st_gid=1001,
                                                           st_mode=stat.S_IFREG | 0o664, st_nlink=2)
            acceptance.report_installation_metadata(path, member=2)
            output = fixture.errors.getvalue()
            for expected in ("node=usr-local", "node=libexec", "node=host-root", "node=dependency",
                             "uid=1001 gid=1001 mode=0664 type=file links=2", "member=2"):
                self.assertIn(expected, output)
            self.assertNotIn("private_dependency", output)
            self.assertNotIn(str(path), output)

    def test_metadata_read_failures_do_not_mask_primary_failure_or_leak_errors(self):
        with self.installation() as fixture:
            fixture.metadata.side_effect = OSError(errno.EACCES, "private error", "private path")
            original = RuntimeError("private trust failure")
            fixture.production.trusted.side_effect = original
            with self.assertRaises(RuntimeError) as caught:
                acceptance.validate_fixed_installation(fixture.production)
            self.assertIs(caught.exception, original)
            output = fixture.errors.getvalue()
            self.assertIn("metadata=unavailable errno=13", output)
            self.assertNotIn("private", output)

    def test_invalid_metadata_or_dependency_index_cannot_inject_logs(self):
        with self.installation() as fixture:
            fixture.metadata.return_value = SimpleNamespace(st_uid="secret\n::error::injection", st_gid=0,
                                                           st_mode=stat.S_IFREG | 0o644, st_nlink=1)
            acceptance.report_installation_metadata(acceptance.HOST)
            self.assertIn("metadata=invalid", fixture.errors.getvalue())
            self.assertNotIn("secret", fixture.errors.getvalue())
            for member in ("secret", 0, -1, True):
                with self.assertRaises(ValueError):
                    acceptance.report_installation_metadata(acceptance.HOST, member=member)

    def test_empty_host_still_requires_main_dll_and_final_gate(self):
        with self.installation() as fixture, mock.patch.object(acceptance.Path, "rglob", return_value=[]):
            acceptance.validate_fixed_installation(fixture.production)
            fixture.production.trusted.assert_any_call(acceptance.HOST / "Project1.MigrationHost.dll",
                                                       0o644, directory=False)
            fixture.production.require_installation.assert_called_once_with(enabled=False)
            self.assertIn("host_dependencies=0", fixture.output.getvalue())

    def test_runtime_diagnostics_identify_root_home_and_new_runtime(self):
        with self.installation() as fixture:
            acceptance.report_installation_metadata(fixture.runtime)
            output = fixture.errors.getvalue()
            for node in ("filesystem-root", "root-home", "runtime-root", "runtime-launcher"):
                self.assertIn(f"node={node}", output)
            self.assertNotIn("node=opt", output)
            self.assertNotIn(str(fixture.runtime), output)

    def test_real_trust_accepts_root_runtime_but_still_rejects_writable_ancestors(self):
        # Simulate POSIX metadata only; exercise the unmodified production check
        # on Windows too, without installing a runtime or changing permissions.
        launcher = acceptance.RUNTIME / "dotnet"
        old_launcher = Path("/opt/project1-host-acceptance-dotnet/dotnet")
        self.assertEqual(acceptance.RUNTIME.parent, Path("/root"))
        def metadata(path):
            if path in (launcher, old_launcher):
                return SimpleNamespace(st_uid=0, st_mode=stat.S_IFREG | 0o755, st_nlink=1)
            mode = 0o777 if path == Path("/opt") else 0o700 if path == Path("/root") else 0o755
            return SimpleNamespace(st_uid=0, st_mode=stat.S_IFDIR | mode, st_nlink=2)
        with mock.patch.object(Path, "is_absolute", return_value=True), \
                mock.patch.object(Path, "lstat", autospec=True, side_effect=metadata) as read:
            production.trusted(launcher)
            self.assertEqual([call.args[0] for call in read.call_args_list],
                             [Path("/"), Path("/root"), acceptance.RUNTIME, launcher])
            with self.assertRaisesRegex(production.ProductionError, "exclusively root-managed"):
                production.trusted(old_launcher)
            read.assert_called_with(Path("/opt"))
            with mock.patch.object(Path, "lstat", return_value=SimpleNamespace(
                    st_uid=0, st_mode=stat.S_IFDIR | 0o777, st_nlink=2)), \
                    self.assertRaises(production.ProductionError):
                production.trusted(launcher)

    def test_existing_runtime_file_directory_or_symlink_refused_before_mutations(self):
        # lexists includes dangling symlinks, not just existing directories.
        modules = {"grp": SimpleNamespace(), "pwd": SimpleNamespace()}
        with mock.patch.dict(sys.modules, modules), \
                mock.patch.object(acceptance, "require_ci"), \
                mock.patch.object(acceptance.os, "geteuid", return_value=0, create=True), \
                mock.patch.object(acceptance.os.path, "lexists",
                                  side_effect=lambda path: path == acceptance.RUNTIME), \
                mock.patch.object(acceptance, "normalize_runner_sudoers") as normalize, \
                mock.patch.object(acceptance, "copy_tree") as copy_tree, \
                mock.patch.object(acceptance.os, "umask") as umask, \
                mock.patch.object(acceptance, "run") as child, \
                mock.patch.object(acceptance, "_CURRENT_STAGE", "S00"), \
                mock.patch("sys.stdout", io.StringIO()):
            with self.assertRaisesRegex(RuntimeError, "Never overwrite"):
                acceptance.acceptance(*(Path("unused") for _ in range(4)))
            normalize.assert_not_called()
            copy_tree.assert_not_called()
            umask.assert_not_called()
            child.assert_not_called()

    def test_existing_ledger_refused_before_any_installation_or_initialization(self):
        modules = {"grp": SimpleNamespace(), "pwd": SimpleNamespace()}
        with mock.patch.dict(sys.modules, modules), \
                mock.patch.object(acceptance, "require_ci"), \
                mock.patch.object(acceptance.os, "geteuid", return_value=0, create=True), \
                mock.patch.object(acceptance.os.path, "lexists",
                                  side_effect=lambda path: path == acceptance.LEDGER), \
                mock.patch.object(acceptance.Path, "mkdir") as mkdir, \
                mock.patch.object(acceptance, "normalize_runner_sudoers") as normalize, \
                mock.patch.object(acceptance, "copy_tree") as copies, \
                mock.patch.object(acceptance, "run") as child, \
                mock.patch.object(acceptance, "_CURRENT_STAGE", "S00"), \
                mock.patch("sys.stdout", io.StringIO()):
            with self.assertRaisesRegex(RuntimeError, "Never overwrite"):
                acceptance.acceptance(*(Path("unused") for _ in range(4)))
            self.assertEqual(acceptance._CURRENT_STAGE, "S01")
            mkdir.assert_not_called()
            normalize.assert_not_called()
            copies.assert_not_called()
            child.assert_not_called()

    def test_missing_or_untrusted_ledger_fails_preflight_with_safe_diagnostics(self):
        # Run the actual production trust function against simulated metadata.
        # Missing/unsafe directories must fail before the real admin wrapper.
        for problem in ("missing", "owner", "mode", "symlink", "ancestor"):
            with self.subTest(problem=problem), self.installation() as fixture, \
                    mock.patch.object(Path, "is_absolute", return_value=True):
                def metadata(path):
                    uid, mode = 0, stat.S_IFDIR | 0o755
                    if path == acceptance.LEDGER:
                        mode = stat.S_IFDIR | 0o700
                        if problem == "missing":
                            raise FileNotFoundError(errno.ENOENT, "private detail", "private path")
                        if problem == "owner":
                            uid = 10001
                        if problem == "mode":
                            mode = stat.S_IFDIR | 0o755
                        if problem == "symlink":
                            mode = stat.S_IFLNK | 0o700
                    if problem == "ancestor" and path == Path("/var/lib"):
                        mode = stat.S_IFDIR | 0o777
                    return SimpleNamespace(st_uid=uid, st_gid=0, st_mode=mode, st_nlink=2)
                fixture.metadata.side_effect = metadata
                def trusted(path, mode=None, *, directory=False):
                    if path == acceptance.LEDGER:
                        production.trusted(path, mode, directory=directory)
                fixture.production.trusted.side_effect = trusted
                with self.assertRaises((FileNotFoundError, production.ProductionError)):
                    acceptance.validate_fixed_installation(fixture.production)
                self.assertEqual(acceptance._CURRENT_STAGE, "S18L")
                fixture.production.require_installation.assert_not_called()
                output = fixture.errors.getvalue()
                for node in ("var", "var-lib", "ledger-root"):
                    self.assertIn(f"node={node}", output)
                self.assertNotIn("private", output)
                if problem == "missing":
                    self.assertIn("metadata=unavailable errno=2", output)

    def test_ledger_preflight_and_real_admin_initialization_remain_in_order(self):
        source = inspect.getsource(acceptance.acceptance)
        self.assertLess(source.index("LEDGER.mkdir(mode=0o700)"), source.index('label="docker-start"'))
        self.assertLess(source.index("validate_fixed_installation(production)"),
                        source.index('label="ledger-initialize"'))
        self.assertIn('run(["/usr/local/sbin/project1-migration-admin", "initialize"], '
                      'label="ledger-initialize")', source)
        self.assertNotIn("ledger.initialize()", source)

    def test_ci_directory_modes_are_explicit_after_restrictive_umask(self):
        # Exercise the actual setup sequence with simulated POSIX umask masking;
        # intercept all filesystem/process mutations. Never install locally.
        modes, events = {}, []
        def mkdir(path, mode=0o777, **options):
            events.append(("mkdir", path, mode, options))
            modes[path] = mode & ~0o077
        def chmod(path, mode):
            events.append(("chmod", path, mode))
            if path in modes:
                modes[path] = mode
        def chown(path, uid, gid):
            events.append(("chown", path, uid, gid))
        def child(arguments, **options):
            if options["label"] == "docker-start":
                self.assertEqual(modes[acceptance.LEDGER], 0o700)
                raise acceptance.CommandFailure("docker-start", "exit-status", exit_code=1)
            return b""
        absent = mock.Mock(side_effect=KeyError)
        modules = {"grp": SimpleNamespace(getgrnam=absent), "pwd": SimpleNamespace(getpwnam=absent)}
        with ExitStack() as stack:
            stack.enter_context(mock.patch.dict(sys.modules, modules))
            stack.enter_context(mock.patch.dict(os.environ, AcceptanceGuardTests().environment(), clear=True))
            stack.enter_context(mock.patch.object(acceptance, "require_ci"))
            stack.enter_context(mock.patch.object(acceptance.os, "geteuid", return_value=0, create=True))
            stack.enter_context(mock.patch.object(acceptance.os.path, "lexists", return_value=False))
            stack.enter_context(mock.patch.object(acceptance.os, "umask"))
            stack.enter_context(mock.patch.object(acceptance.os, "chown", side_effect=chown, create=True))
            stack.enter_context(mock.patch.object(acceptance.Path, "mkdir", autospec=True, side_effect=mkdir))
            stack.enter_context(mock.patch.object(acceptance.Path, "chmod", autospec=True, side_effect=chmod))
            stack.enter_context(mock.patch.object(acceptance.Path, "resolve", return_value=Path("/unused/dotnet")))
            link = stack.enter_context(mock.patch.object(acceptance.Path, "symlink_to", autospec=True))
            copies = stack.enter_context(mock.patch.object(acceptance, "copy_tree"))
            for name in ("normalize_runner_sudoers", "verify_sudoers_metadata", "write_private"):
                stack.enter_context(mock.patch.object(acceptance, name))
            stack.enter_context(mock.patch.object(acceptance.shutil, "copyfile"))
            stack.enter_context(mock.patch.object(acceptance, "run", side_effect=child))
            stack.enter_context(mock.patch.object(acceptance, "_CURRENT_STAGE", "S00"))
            stack.enter_context(mock.patch("sys.stdout", io.StringIO()))
            with self.assertRaises(acceptance.CommandFailure):
                acceptance.acceptance(*(Path("unused") for _ in range(4)))
        self.assertEqual(copies.call_args_list[0], mock.call(Path("/unused"), acceptance.RUNTIME))
        link.assert_called_once_with(Path("/usr/bin/dotnet"), acceptance.RUNTIME / "dotnet")
        self.assertFalse(any(event[1] == Path("/opt") for event in events))
        self.assertEqual(modes, {acceptance.ROOT: 0o755, acceptance.CONFIG: 0o700,
                                acceptance.CONFIG / "approvals": 0o700,
                                acceptance.LEDGER: 0o700, acceptance.BACKUPS: 0o750,
                                acceptance.STAGING: 0o700})
        self.assertIn(("mkdir", acceptance.LEDGER, 0o700, {}), events)
        self.assertLess(events.index(("chown", acceptance.LEDGER, 0, 0)),
                        events.index(("chmod", acceptance.LEDGER, 0o700)))
        self.assertLess(events.index(("chown", acceptance.BACKUPS, 0, 10001)),
                        events.index(("chmod", acceptance.BACKUPS, 0o750)))


class AcceptanceDiagnosticTests(unittest.TestCase):
    def setUp(self):
        self.patch = mock.patch.object(acceptance, "_CURRENT_STAGE", "S09")
        self.patch.start()
        self.addCleanup(self.patch.stop)

    def report(self, error):
        output = io.StringIO()
        with mock.patch("sys.stderr", output):
            acceptance.report_failure(error)
        return output.getvalue()

    def test_nonzero_exit_reports_only_fixed_label_and_numeric_code(self):
        secret = b"P1!private-password; raw SQL; private argument and stderr"
        child = subprocess.CompletedProcess([secret.decode()], 7, secret, secret)
        with mock.patch.object(acceptance.subprocess, "run", return_value=child), \
                self.assertRaises(acceptance.CommandFailure) as caught:
            acceptance.run([secret.decode()], label="visudo-template", body=secret, environment={"PASSWORD": secret.decode()})
        output = self.report(caught.exception)
        self.assertIn("ERROR [S09] validate-sudoers-template", output)
        self.assertIn("command=visudo-template reason=exit-status exit_code=7", output)
        self.assertNotIn(secret.decode(), output)
        self.assertNotIn(secret.decode(), str(caught.exception))

    def test_unexpected_success_of_a_denial_test_also_fails(self):
        child = subprocess.CompletedProcess([], 0, b"secret", b"secret")
        with mock.patch.object(acceptance.subprocess, "run", return_value=child), \
                self.assertRaises(acceptance.CommandFailure) as caught:
            acceptance.run(["anything"], label="sudo-invalid-arguments", succeeds=False)
        self.assertIn("exit_code=0", self.report(caught.exception))

    def test_expected_denial_remains_accepted_and_success_bytes_unchanged(self):
        for code, expected in ((0, True), (1, False), (-9, False)):
            child = subprocess.CompletedProcess([], code, b"exact stdout", b"ignored stderr")
            with self.subTest(code=code), mock.patch.object(acceptance.subprocess, "run", return_value=child):
                self.assertEqual(acceptance.run([], label="sudo-describe", succeeds=expected), b"exact stdout")

    def test_timeout_and_launch_errors_never_log_child_context(self):
        failures = (
            (subprocess.TimeoutExpired(["secret argv"], 5, output=b"secret output", stderr=b"secret stderr"), "reason=timeout"),
            (FileNotFoundError(errno.ENOENT, "secret exception", "secret path"), "reason=launch-error errno=2"),
        )
        for error, expected in failures:
            with self.subTest(expected=expected), mock.patch.object(acceptance.subprocess, "run", side_effect=error), \
                    self.assertRaises(acceptance.CommandFailure) as caught:
                acceptance.run(["secret argv"], label="docker-start")
            output = self.report(caught.exception)
            self.assertIn(expected, output)
            self.assertNotIn("secret", output)

    def test_filesystem_errors_only_report_errno_and_generic_errors_no_message(self):
        self.assertIn("filesystem-error errno=28", self.report(OSError(errno.ENOSPC, "secret filename")))
        for error in (RuntimeError("secret detail"), OSError(errno.ENOSPC, "secret filename")):
            self.assertNotIn("secret", self.report(error))

    def test_modified_diagnostic_fields_cannot_inject_log_content(self):
        for field in ("label", "reason", "exit_code", "errno"):
            error = acceptance.CommandFailure("docker-start", "exit-status", exit_code=1)
            setattr(error, field, "secret\n::error::injected")
            with self.subTest(field=field):
                output = self.report(error)
                self.assertIn("operation-failed", output)
                self.assertNotIn("secret", output)

    def test_unknown_labels_or_stage_injection_are_rejected_before_children(self):
        for label in ("secret\n::error::injected", "unknown", "visudo-template"):
            with mock.patch.object(acceptance.subprocess, "run") as child:
                if label == "visudo-template":
                    with self.assertRaises(ValueError):
                        acceptance.CommandFailure(label, "secret reason")
                else:
                    with self.assertRaises(ValueError):
                        acceptance.run([], label=label)
                child.assert_not_called()
        with self.assertRaises(ValueError):
            acceptance.set_stage("secret\n::error::injected")
        self.assertEqual(acceptance._CURRENT_STAGE, "S09")

    def test_every_real_child_call_has_an_allowlisted_literal_label(self):
        tree = ast.parse(Path(acceptance.__file__).read_text(encoding="utf-8"))
        calls = [node for node in ast.walk(tree) if isinstance(node, ast.Call)
                 and isinstance(node.func, ast.Name) and node.func.id == "run"]
        self.assertGreater(len(calls), 15)
        for call in calls:
            labels = [keyword.value for keyword in call.keywords if keyword.arg == "label"]
            self.assertEqual(len(labels), 1)
            self.assertIsInstance(labels[0], ast.Constant)
            self.assertIn(labels[0].value, acceptance.COMMAND_LABELS)

    def test_cleanup_failure_preserves_original_stage_and_primary_error(self):
        output = io.StringIO()
        failure = acceptance.CommandFailure("docker-cleanup", "exit-status", exit_code=1)
        with mock.patch.object(acceptance, "run", side_effect=failure) as child, \
                mock.patch("sys.stdout", io.StringIO()), mock.patch("sys.stderr", output):
            acceptance.cleanup_container("a" * 64, already_failing=True)
        self.assertEqual(acceptance._CURRENT_STAGE, "S09")
        self.assertIn("ERROR [S99]", output.getvalue())
        child.assert_called_once_with(["/usr/bin/docker", "rm", "--force", "a" * 64], label="docker-cleanup")
        self.assertIn("ERROR [S09]", self.report(RuntimeError("original secret failure")))

    def test_cleanup_failure_after_success_still_fails_and_invalid_id_never_deleted(self):
        failure = acceptance.CommandFailure("docker-cleanup", "exit-status", exit_code=1)
        with mock.patch.object(acceptance, "run", side_effect=failure), mock.patch("sys.stdout", io.StringIO()), \
                self.assertRaises(acceptance.CommandFailure):
            acceptance.cleanup_container("a" * 64, already_failing=False)
        self.assertEqual(acceptance._CURRENT_STAGE, "S99")
        with mock.patch.object(acceptance, "run") as child:
            for identifier in (None, "", "../anything", "a" * 64 + " extra"):
                acceptance.cleanup_container(identifier, already_failing=True)
        child.assert_not_called()

    def test_cli_retains_stage_and_safe_command_status_without_exception_message(self):
        def failure(*args):
            acceptance.set_stage("S12")
            raise acceptance.CommandFailure("docker-start", "exit-status", exit_code=125)
        output = io.StringIO()
        with mock.patch.dict(os.environ, AcceptanceGuardTests().environment(), clear=True), \
                mock.patch.object(acceptance.sys, "platform", "linux"), \
                mock.patch.object(acceptance.os, "geteuid", return_value=0, create=True), \
                mock.patch.object(acceptance.Path, "resolve", return_value=Path("/fixture")), \
                mock.patch.object(acceptance, "acceptance", side_effect=failure), \
                mock.patch("sys.stdout", io.StringIO()), mock.patch("sys.stderr", output):
            self.assertEqual(acceptance.main(["a", "b", "c", "d"]), 1)
        self.assertIn("ERROR [S12] start-disposable-sql-container", output.getvalue())
        self.assertIn("command=docker-start reason=exit-status exit_code=125", output.getvalue())


if __name__ == "__main__":
    unittest.main()
