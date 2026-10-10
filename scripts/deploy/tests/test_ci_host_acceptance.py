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
