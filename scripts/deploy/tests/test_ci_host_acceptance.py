"""Portable guard/fixture checks. Never install anything or open SQL locally."""
import copy
import ast
import errno
import io
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
        umask.assert_not_called()
        copy_tree.assert_not_called()
        copyfile.assert_not_called()

    def test_strict_file_check_and_global_check_remain_in_order_before_database_setup(self):
        source = Path(acceptance.__file__).read_text(encoding="utf-8").split("def acceptance(", 1)[1]
        self.assertLess(source.index('label="visudo-baseline"'), source.index("copy_tree("))
        self.assertLess(source.index("verify_sudoers_metadata(sudoers)"), source.index('label="visudo-file"'))
        self.assertLess(source.index('label="visudo-file"'), source.index('label="visudo-installed"'))
        self.assertLess(source.index('label="visudo-installed"'), source.index("CONFIG.mkdir"))
        self.assertEqual(acceptance.VISUDO_CHECKS["visudo-file"],
                         ("/usr/sbin/visudo", "-c", "-O", "-P", "-f", str(acceptance.SUDOERS)))


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
