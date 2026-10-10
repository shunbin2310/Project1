"""Portable guard/fixture checks. Never install anything or open SQL locally."""
import copy
import os
from pathlib import Path
import sys
import tempfile
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
                mock.patch.object(acceptance.subprocess, "run") as child, mock.patch("sys.stderr"):
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


if __name__ == "__main__":
    unittest.main()
