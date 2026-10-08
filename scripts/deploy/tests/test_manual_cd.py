"""Offline tests: fake GitHub responses, fixture ZIPs and mocked SSH only."""

import copy
from datetime import datetime, timedelta, timezone
import hashlib
import io
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock
import urllib.error
import urllib.request
import zipfile

SCRIPTS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SCRIPTS))
import verify_ci_artifacts as verify
import deploy_verified as transport

SHA = "a" * 40
RUN_ID = 42
WORKFLOW_ID = 17


def zip_payload(files):
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for name, contents in files.items():
            archive.writestr(name, contents)
    return stream.getvalue()


PAYLOADS = {
    "api": zip_payload({"Project1.Api.dll": b"MZ-test",
                        "Project1.Api.deps.json": b"{}",
                        "Project1.Api.runtimeconfig.json": b'{"runtimeOptions":{"tfm":"net10.0"}}'}),
    "frontend": zip_payload({"index.html": b"<html>test</html>", "assets/app.js": b"test"}),
}


def run_metadata():
    repository = {"id": verify.REPOSITORY_ID, "full_name": verify.REPOSITORY}
    return {"id": RUN_ID, "workflow_id": WORKFLOW_ID, "path": verify.WORKFLOW_PATH,
            "repository": repository, "head_repository": repository.copy(),
            "head_sha": SHA, "head_branch": "main", "event": "push",
            "status": "completed", "conclusion": "success", "run_attempt": 1}


def artifact_metadata():
    return [{"id": index, "name": prefix + SHA, "size_in_bytes": len(PAYLOADS[kind]),
             "digest": "sha256:" + hashlib.sha256(PAYLOADS[kind]).hexdigest(), "expired": False,
             "expires_at": (datetime.now(timezone.utc) + timedelta(days=7)).isoformat(),
             "workflow_run": {"id": RUN_ID, "repository_id": verify.REPOSITORY_ID,
                              "head_repository_id": verify.REPOSITORY_ID,
                              "head_branch": "main", "head_sha": SHA}}
            for index, (kind, prefix) in enumerate((("api", "project1-api-linux-x64-"),
                                                   ("frontend", "project1-frontend-dist-")), start=1)]


class FakeClient:
    def __init__(self):
        self.run = run_metadata()
        self.workflow = {"id": WORKFLOW_ID, "name": "Project1 CI", "path": verify.WORKFLOW_PATH}
        self.items = artifact_metadata()
        self.payloads = copy.copy(PAYLOADS)
        self.after_download = None
        self.downloads = []

    def get(self, path):
        return copy.deepcopy(self.workflow if path.endswith("/workflows/ci.yml") else self.run)

    def artifacts(self, run_id):
        return copy.deepcopy(self.items)

    def download(self, identifier, path, digest):
        kind = "api" if identifier == 1 else "frontend"
        self.downloads.append(identifier)
        verify.copy_archive(io.BytesIO(self.payloads[kind]), path, digest)
        if self.after_download:
            self.after_download(self)


class TempCase(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.directory = self.root / "verified"

    def good_manifest(self):
        return verify.verify(FakeClient(), RUN_ID, SHA, self.directory)


class MetadataTests(unittest.TestCase):
    def test_positive_ids_refuse_shell_float_boolean_and_missing_values(self):
        for value in (True, False, None, "0", "-1", "1; reboot", "../42", "1.0", " 42", "1" * 21):
            with self.subTest(value=value), self.assertRaises(verify.VerificationError):
                verify.positive_id(value)
        self.assertEqual(verify.positive_id("42"), 42)

    def test_commit_must_be_exact_lowercase_sha(self):
        for value in ("main", "a" * 7, SHA.upper(), SHA + ";id", "../api", ""):
            with self.subTest(value=value), self.assertRaises(verify.VerificationError):
                verify.checked_sha(value)

    def test_trusted_run(self):
        self.assertEqual(verify.validate_run(run_metadata(), RUN_ID, SHA, WORKFLOW_ID), 1)

    def test_refuse_pr_failed_pending_other_sha_workflow_repository_and_attempt(self):
        mutations = {"id": 43, "workflow_id": 18, "path": ".github/workflows/other.yml",
                     "head_sha": "b" * 40, "head_branch": "feature", "event": "pull_request",
                     "status": "in_progress", "conclusion": "failure", "run_attempt": 0,
                     "repository": {"id": 7, "full_name": verify.REPOSITORY},
                     "head_repository": {"id": verify.REPOSITORY_ID, "full_name": "fork/Project1"}}
        for key, value in mutations.items():
            run = run_metadata()
            run[key] = value
            with self.subTest(key=key), self.assertRaises(verify.VerificationError):
                verify.validate_run(run, RUN_ID, SHA, WORKFLOW_ID)

    def test_exact_artifacts_ignore_test_reports(self):
        items = artifact_metadata() + [{"id": 9, "name": "backend-test-results-" + SHA}]
        result = verify.select_artifacts(items, RUN_ID, SHA, datetime.now(timezone.utc))
        self.assertEqual(result["frontend"]["id"], 2)

    def test_missing_or_duplicate_artifact_refused(self):
        for items in (artifact_metadata()[:1], artifact_metadata() + artifact_metadata()[:1]):
            with self.assertRaises(verify.VerificationError):
                verify.select_artifacts(items, RUN_ID, SHA, datetime.now(timezone.utc))

    def test_expiration_digest_size_and_artifact_provenance(self):
        mutations = {"expired": True, "expires_at": "2000-01-01T00:00:00Z",
                     "digest": None, "size_in_bytes": verify.MAX_ARCHIVE_BYTES + 1,
                     "id": "1;id", "workflow_run": {"id": 100}}
        for key, value in mutations.items():
            items = artifact_metadata()
            items[0][key] = value
            with self.subTest(key=key), self.assertRaises(verify.VerificationError):
                verify.select_artifacts(items, RUN_ID, SHA, datetime.now(timezone.utc))
        for key in ("id", "repository_id", "head_repository_id", "head_sha", "head_branch"):
            items = artifact_metadata()
            items[0]["workflow_run"][key] = "wrong"
            with self.subTest(key=key), self.assertRaises(verify.VerificationError):
                verify.select_artifacts(items, RUN_ID, SHA, datetime.now(timezone.utc))


class VerificationTests(TempCase):
    def test_complete_verification_and_manifest_recheck(self):
        result = self.good_manifest()
        self.assertEqual(result, verify.load_verified(self.directory))
        self.assertEqual(result["commit"], SHA)

    def test_reject_existing_directory(self):
        self.directory.mkdir()
        with self.assertRaises(FileExistsError):
            verify.verify(FakeClient(), RUN_ID, SHA, self.directory)

    def test_bad_source_never_downloads_or_creates_directory(self):
        client = FakeClient()
        client.run["event"] = "pull_request"
        with self.assertRaises(verify.VerificationError):
            verify.verify(client, RUN_ID, SHA, self.directory)
        self.assertEqual(client.downloads, [])
        self.assertFalse(self.directory.exists())

    def test_wrong_workflow_identity(self):
        client = FakeClient()
        client.workflow["path"] = ".github/workflows/evil.yml"
        with self.assertRaises(verify.VerificationError):
            verify.verify(client, RUN_ID, SHA, self.directory)

    def test_bad_digest_leaves_no_verified_manifest(self):
        client = FakeClient()
        client.payloads["frontend"] = b"changed"
        with self.assertRaises(verify.VerificationError):
            verify.verify(client, RUN_ID, SHA, self.directory)
        self.assertFalse((self.directory / "verified.json").exists())

    def test_invalid_package_layout_leaves_no_manifest(self):
        client = FakeClient()
        client.payloads["frontend"] = zip_payload({"dist/index.html": b"wrong"})
        client.items[1]["digest"] = "sha256:" + hashlib.sha256(client.payloads["frontend"]).hexdigest()
        with self.assertRaises(verify.DeploymentError):
            verify.verify(client, RUN_ID, SHA, self.directory)
        self.assertFalse((self.directory / "verified.json").exists())

    def test_rerun_during_download_refused(self):
        client = FakeClient()
        client.after_download = lambda c: c.run.update(run_attempt=2)
        with self.assertRaises(verify.VerificationError):
            verify.verify(client, RUN_ID, SHA, self.directory)
        self.assertFalse((self.directory / "verified.json").exists())

    def test_changed_artifact_metadata_during_download_refused(self):
        client = FakeClient()
        client.after_download = lambda c: c.items[0].update(id=99)
        with self.assertRaises(verify.VerificationError):
            verify.verify(client, RUN_ID, SHA, self.directory)
        self.assertFalse((self.directory / "verified.json").exists())

    def test_expected_validation_attempt_and_digests_are_pinned(self):
        artifacts = artifact_metadata()
        expected = (1, artifacts[0]["digest"][7:], artifacts[1]["digest"][7:])
        result = verify.verify(FakeClient(), RUN_ID, SHA, self.directory, expected)
        self.assertEqual(result["run_attempt"], 1)
        for index, pin in enumerate(((2, *expected[1:]), (1, "b" * 64, expected[2]),
                                     (1, expected[1], "b" * 64))):
            client = FakeClient()
            with self.assertRaises(verify.VerificationError):
                verify.verify(client, RUN_ID, SHA, self.root / f"pin-{index}", pin)
            self.assertEqual(client.downloads, [])

    def test_github_outputs_only_decimal_and_hex(self):
        manifest = self.good_manifest()
        output = self.root / "outputs"
        verify.write_outputs(manifest, output)
        lines = output.read_text(encoding="utf-8").splitlines()
        self.assertEqual(lines[0], "ci_attempt=1")
        self.assertEqual(lines[1], "api_digest=" + manifest["artifacts"]["api"]["sha256"])
        self.assertEqual(len(lines), 3)

    def test_changed_archive_refused_before_transport(self):
        self.good_manifest()
        (self.directory / "api.zip").write_bytes(b"changed")
        with mock.patch.object(transport, "run_command") as command:
            with self.assertRaises(verify.VerificationError):
                transport.deploy_verified(self.directory, "fake-key", "fake-host", self.root)
        command.assert_not_called()

    def test_manifest_shell_injection_refused(self):
        manifest = self.good_manifest()
        manifest["commit"] = SHA + "; reboot"
        (self.directory / "verified.json").write_text(json.dumps(manifest), encoding="utf-8")
        with self.assertRaises(verify.VerificationError):
            verify.load_verified(self.directory)

    def test_copy_archive_limits_and_empty_or_wrong_digest(self):
        for index, payload in enumerate((b"", b"wrong", b"too-large")):
            with mock.patch.object(verify, "MAX_ARCHIVE_BYTES", 5):
                with self.assertRaises(verify.VerificationError):
                    verify.copy_archive(io.BytesIO(payload), self.root / f"{index}.zip", "a" * 64)


class HttpTests(unittest.TestCase):
    def test_storage_redirect_whitelist(self):
        good = "https://production.blob.core.windows.net/a?sig=private"
        self.assertEqual(verify.storage_url(good), good)
        for url in ("http://production.blob.core.windows.net/a", "https://evil.example/a",
                    "https://blob.core.windows.net.evil.example/a", "https://user@a.blob.core.windows.net/a",
                    "https://a.blob.core.windows.net:444/a", "file:///tmp/a", ""):
            with self.subTest(url=url), self.assertRaises(verify.VerificationError):
                verify.storage_url(url)

    def test_api_auth_not_forwarded_to_storage(self):
        client = verify.GitHubClient("fake-token")
        redirect = urllib.error.HTTPError("https://api.github.com", 302, "redirect",
                                         {"Location": "https://store.blob.core.windows.net/a?sig=private"}, None)
        response = mock.MagicMock()
        response.__enter__.return_value = response
        response.status = 200
        response.read.side_effect = [b"package", b""]
        with tempfile.TemporaryDirectory() as directory:
            with mock.patch.object(client.opener, "open", side_effect=[redirect, response]) as opening:
                client.download(1, Path(directory) / "api.zip", hashlib.sha256(b"package").hexdigest())
            first = opening.call_args_list[0].args[0]
            second = opening.call_args_list[1].args[0]
            self.assertEqual(first.get_header("Authorization"), "Bearer fake-token")
            self.assertIsNone(second.get_header("Authorization"))

    def test_api_redirect_handler_never_follows(self):
        self.assertIsNone(verify.NoRedirect().redirect_request(None, None, 302, "", {}, "https://evil.example"))

    def test_pagination_completes_and_refuses_changing_counts(self):
        client = verify.GitHubClient("fake-token")
        with mock.patch.object(client, "get", side_effect=[{"total_count": 2, "artifacts": [1]},
                                                          {"total_count": 2, "artifacts": [2]}]):
            self.assertEqual(client.artifacts(RUN_ID), [1, 2])
        for result in ({"total_count": 1001, "artifacts": []},
                       {"total_count": 2, "artifacts": []}):
            with mock.patch.object(client, "get", return_value=result):
                with self.assertRaises(verify.VerificationError):
                    client.artifacts(RUN_ID)
        with mock.patch.object(client, "get", side_effect=[{"total_count": 2, "artifacts": [1]},
                                                          {"total_count": 3, "artifacts": [2]}]):
            with self.assertRaises(verify.VerificationError):
                client.artifacts(RUN_ID)

    def test_bounded_json_and_non_object_response(self):
        client = verify.GitHubClient("fake-token")
        for body in (b"[]", b"x" * (verify.MAX_JSON_BYTES + 1)):
            response = mock.MagicMock()
            response.__enter__.return_value = response
            response.status = 200
            response.read.return_value = body
            with mock.patch.object(client.opener, "open", return_value=response):
                with self.assertRaises(verify.VerificationError):
                    client.get(f"/repos/{verify.REPOSITORY}/actions/workflows/ci.yml")

    def test_storage_http_error_or_bad_redirect_does_not_write_package(self):
        client = verify.GitHubClient("fake-token")
        for status, location in ((403, ""), (302, "https://evil.example/a")):
            redirect = urllib.error.HTTPError("https://api.github.com", status, "error",
                                             {"Location": location}, None)
            with tempfile.TemporaryDirectory() as directory:
                path = Path(directory) / "package.zip"
                with mock.patch.object(client.opener, "open", side_effect=redirect) as opening:
                    with self.assertRaises(verify.VerificationError):
                        client.download(1, path, "a" * 64)
                self.assertEqual(opening.call_count, 1)
                self.assertFalse(path.exists())

    def test_cli_error_does_not_log_http_body_token_or_signed_url(self):
        error = urllib.error.URLError("fake-token https://storage?sig=private")
        with mock.patch.object(verify.GitHubClient, "get", side_effect=error), \
                mock.patch.dict("os.environ", {"GITHUB_TOKEN": "fake-token"}), \
                mock.patch("sys.stdout", new_callable=io.StringIO) as output:
            code = verify.main(["--run-id", "42", "--commit", SHA, "--directory", "unused"])
        self.assertEqual(code, 1)
        self.assertNotIn("fake-token", output.getvalue())
        self.assertNotIn("sig=private", output.getvalue())


class TransportTests(TempCase):
    def test_missing_secrets_never_calls_ssh(self):
        with mock.patch.object(transport, "run_command") as command:
            for key, hosts in (("", "fake-host"), ("fake-key", "")):
                with self.assertRaises(verify.VerificationError):
                    transport.deploy_verified(self.directory, key, hosts, self.root)
        command.assert_not_called()

    def test_pinned_transfer_fixed_commands_and_cleanup(self):
        self.good_manifest()
        commands = []
        private_files = []

        def command(arguments, timeout=45):
            commands.append(arguments)
            if arguments[0] == "/usr/bin/ssh-keygen":
                path = Path(arguments[-1])
                self.assertTrue(path.exists())
                private_files.append(path)
                return "fake-public-output"
            if arguments[-1] == "whoami":
                return transport.USER
            if arguments[-1].endswith("--help"):
                return "Usage: project1-deploy COMMIT_SHA API_ZIP_SHA256 FRONTEND_ZIP_SHA256"
            if arguments[-1].startswith("sudo -n -- /usr/local/sbin/project1-deploy "):
                return f"Deployed {SHA}; transaction test. API and frontend health checks passed."
            return ""

        with mock.patch.object(transport, "run_command", side_effect=command):
            transport.deploy_verified(self.directory, "fake-key\r\n", "fake-host\r\n", self.root)
        self.assertTrue(private_files)
        self.assertTrue(all(not path.exists() for path in private_files))
        transfer = next(args for args in commands if args[0] == "/usr/bin/scp")
        self.assertIn("StrictHostKeyChecking=yes", transfer)
        self.assertIn("GlobalKnownHostsFile=/dev/null", transfer)
        self.assertEqual(transfer[-1], f"{transport.DESTINATION}:/home/project1_deploy/uploads/{SHA}/")
        self.assertTrue(any(args[-1] == f"umask 077; mkdir -m 0700 -- /home/project1_deploy/uploads/{SHA}" for args in commands))
        self.assertNotIn("mkdir -p", " ".join(str(args) for args in commands))
        self.assertNotIn("fake-key", " ".join(str(args) for args in commands))

    def test_wrong_login_never_uploads_or_invokes_helper(self):
        self.good_manifest()
        def command(arguments, timeout=45):
            return "root" if arguments[-1] == "whoami" else ""
        with mock.patch.object(transport, "run_command", side_effect=command) as calls:
            with self.assertRaises(verify.VerificationError):
                transport.deploy_verified(self.directory, "fake-key", "fake-host", self.root)
        self.assertFalse(any(call.args[0][0] == "/usr/bin/scp" for call in calls.call_args_list))

    def test_transfer_failure_cleans_private_files_and_stops_before_root_deploy(self):
        self.good_manifest()
        paths = []
        def command(arguments, timeout=45):
            if arguments[0] == "/usr/bin/ssh-keygen":
                paths.append(Path(arguments[-1]))
            if arguments[-1] == "whoami":
                return transport.USER
            if arguments[-1].endswith("--help"):
                return "Usage: project1-deploy COMMIT_SHA API_ZIP_SHA256 FRONTEND_ZIP_SHA256"
            if arguments[0] == "/usr/bin/scp":
                raise verify.VerificationError("fake transfer failure")
            return ""
        with mock.patch.object(transport, "run_command", side_effect=command) as calls:
            with self.assertRaises(verify.VerificationError):
                transport.deploy_verified(self.directory, "fake-key", "fake-host", self.root)
        self.assertTrue(all(not path.exists() for path in paths))
        self.assertFalse(any(call.args[0][-1].startswith("sudo -n -- /usr/local/sbin/project1-deploy " + SHA)
                             for call in calls.call_args_list))

    def test_unknown_helper_or_existing_upload_directory_never_transfers(self):
        self.good_manifest()
        for stage in ("helper", "mkdir"):
            def command(arguments, timeout=45):
                if arguments[-1] == "whoami":
                    return transport.USER
                if arguments[-1].endswith("--help"):
                    if stage == "helper":
                        return "wrong helper"
                    return "Usage: project1-deploy COMMIT_SHA API_ZIP_SHA256 FRONTEND_ZIP_SHA256"
                if arguments[-1].startswith("umask") and stage == "mkdir":
                    raise verify.VerificationError("existing directory")
                return ""
            with mock.patch.object(transport, "run_command", side_effect=command) as calls:
                with self.assertRaises(verify.VerificationError):
                    transport.deploy_verified(self.directory, "fake-key", "fake-host", self.root)
            self.assertFalse(any(call.args[0][0] == "/usr/bin/scp" for call in calls.call_args_list))

    def test_remote_deploy_failure_or_unknown_result_is_not_retried(self):
        self.good_manifest()
        for stage in ("failure", "unknown"):
            commands = []
            def command(arguments, timeout=45):
                commands.append(arguments)
                if arguments[-1] == "whoami":
                    return transport.USER
                if arguments[-1].endswith("--help"):
                    return "Usage: project1-deploy COMMIT_SHA API_ZIP_SHA256 FRONTEND_ZIP_SHA256"
                if arguments[-1].startswith("sudo -n -- /usr/local/sbin/project1-deploy " + SHA):
                    if stage == "failure":
                        raise verify.VerificationError("failed deployment")
                    return "unknown result"
                return ""
            with mock.patch.object(transport, "run_command", side_effect=command):
                with self.assertRaises(verify.VerificationError):
                    transport.deploy_verified(self.directory, "fake-key", "fake-host", self.root)
            invocations = [args for args in commands if args[-1].startswith("sudo -n -- /usr/local/sbin/project1-deploy " + SHA)]
            self.assertEqual(len(invocations), 1)

    def test_command_failure_logs_no_subprocess_output_and_scrubs_environment(self):
        result = subprocess.CompletedProcess([], 1, "private-value", "private-value")
        with mock.patch.object(transport.subprocess, "run", return_value=result) as command:
            with self.assertRaises(verify.VerificationError) as caught:
                transport.run_command(["/usr/bin/ssh", "test"])
        self.assertNotIn("private-value", str(caught.exception))
        self.assertEqual(command.call_args.kwargs["env"], {"PATH": "/usr/bin:/bin", "LANG": "C"})
        self.assertFalse(command.call_args.kwargs.get("shell", False))

    def test_cli_refuses_unconfirmed_or_non_main_without_commands(self):
        baseline = {"GITHUB_EVENT_NAME": "workflow_dispatch", "GITHUB_REF": "refs/heads/main",
                    "GITHUB_REPOSITORY": verify.REPOSITORY, "DEPLOY_CONFIRMED": "true"}
        for key, value in (("DEPLOY_CONFIRMED", "false"), ("GITHUB_REF", "refs/heads/feature"),
                           ("GITHUB_EVENT_NAME", "pull_request"), ("GITHUB_REPOSITORY", "fork/Project1")):
            environment = {**baseline, key: value}
            with mock.patch.dict("os.environ", environment), mock.patch.object(transport, "deploy_verified") as deploy, \
                    mock.patch("sys.stdout", new_callable=io.StringIO):
                self.assertEqual(transport.main(["--directory", str(self.directory)]), 1)
            deploy.assert_not_called()


class WorkflowTests(unittest.TestCase):
    def test_manual_default_validation_permissions_and_no_environment(self):
        workflow = (SCRIPTS.parents[1] / ".github/workflows/deploy-manual.yml").read_text(encoding="utf-8")
        self.assertIn("default: false", workflow)
        self.assertIn("cancel-in-progress: false", workflow)
        self.assertNotIn("  push:", workflow)
        self.assertNotIn("  pull_request:", workflow)
        self.assertNotIn("  workflow_run:", workflow)
        self.assertNotIn("    environment:", workflow)
        validate, deploy = workflow.split("  deploy:\n", 1)
        self.assertNotIn("secrets.", validate)
        self.assertNotIn("id-token: write", validate)
        self.assertIn("inputs.deploy &&", deploy)
        self.assertIn("needs: validate", deploy)
        self.assertIn("id-token: write", deploy)
        self.assertIn("DEPLOY_CONFIRMED: 'true'", deploy)


if __name__ == "__main__":
    unittest.main()
