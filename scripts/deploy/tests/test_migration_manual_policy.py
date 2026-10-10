"""Regression checks for CI validation plus administrator-run migrations."""

from pathlib import Path
import re
import unittest


ROOT = Path(__file__).resolve().parents[3]


class ManualMigrationPolicyTests(unittest.TestCase):
    def test_ci_keeps_core_sql_checks_without_host_dependency_or_execution_jobs(self):
        workflow = (ROOT / ".github/workflows/ci.yml").read_text(encoding="utf-8")
        self.assertEqual(re.findall(r"^  ([a-z][a-z-]*):$", workflow.split("jobs:\n", 1)[1], re.M),
                         ["backend", "frontend"])
        backend = workflow.split("  backend:\n", 1)[1].split("  frontend:\n", 1)[0]
        self.assertNotIn("    needs:", backend)
        self.assertNotRegex(workflow, r"\$\{\{\s*secrets\.")
        self.assertIn("127.0.0.1:14333:1433", backend)
        self.assertIn("--hostname project1-ci-sqlserver", backend)
        self.assertIn("migrations has-pending-model-changes", backend)
        self.assertIn('FullyQualifiedName!~MigrationSqlIntegrationTests"', backend)
        self.assertIn('FullyQualifiedName~MigrationSqlIntegrationTests"', backend)
        self.assertIn("migrations script 0 --idempotent", backend)
        self.assertIn("migrations list --no-connect", backend)
        self.assertIn("CD never executes it.", backend)
        stages = [
            "Test fresh database and existing database migrations on SQL Server",
            "Generate reviewable migration SQL (no database access)",
            "Test deployment and read-only migration tooling without server access",
            "Test generated SQL on disposable SQL Server databases",
            "Verify migration SQL files are unchanged after tests",
            "Save reviewable migration SQL",
            "Publish backend for Ubuntu x64",
        ]
        positions = [backend.index("- name: " + stage) for stage in stages]
        self.assertEqual(positions, sorted(positions))
        for removed in ("MigrationHost", "MigrationExecutor", "MigrationExecutionWorkflowSqlTests",
                        "ci_host_acceptance", "ACCEPTANCE_RESULT"):
            self.assertNotIn(removed, workflow)

    def test_manual_workflow_only_exposes_validation_app_deploy_and_read_only_precheck(self):
        workflow = (ROOT / ".github/workflows/deploy-manual.yml").read_text(encoding="utf-8")
        inputs = workflow.split("    inputs:\n", 1)[1].split("\npermissions:", 1)[0]
        self.assertEqual(re.findall(r"^      ([a-z_]+):$", inputs, re.M),
                         ["ci_run_id", "commit_sha", "deploy", "check_migrations"])
        self.assertEqual(inputs.count("default: false"), 2)
        self.assertEqual(re.findall(r"^  ([a-z][a-z-]*):$", workflow.split("jobs:\n", 1)[1], re.M),
                         ["validate", "deploy", "migration-precheck"])
        for removed in ("execute_migrations", "migration_approval_id",
                        "execute_migration_verified.py", "project1-migration-execute"):
            self.assertNotIn(removed, workflow)
        validate = workflow.split("  validate:\n", 1)[1].split("  deploy:\n", 1)[0]
        self.assertNotIn("secrets.", validate)
        code = validate.split("python3 - <<'PY'\n", 1)[1].split("\n          PY", 1)[0]
        # Exercise the actual mode guard, not a second copy of its logic.
        import os
        import textwrap
        from unittest.mock import patch
        for deploy, precheck in (("false", "false"), ("true", "false"), ("false", "true")):
            with self.subTest(deploy=deploy, precheck=precheck), patch.dict(
                    os.environ, {"DEPLOY": deploy, "CHECK_MIGRATIONS": precheck}, clear=True):
                exec(textwrap.dedent(code), {})
        with patch.dict(os.environ, {"DEPLOY": "true", "CHECK_MIGRATIONS": "true"}, clear=True):
            with self.assertRaises(SystemExit):
                exec(textwrap.dedent(code), {})

    def test_solution_and_test_project_do_not_reference_removed_tools(self):
        solution = (ROOT / "Project1.slnx").read_text(encoding="utf-8")
        project = (ROOT / "tests/Project1.Migrations.Tests/Project1.Migrations.Tests.csproj").read_text(
            encoding="utf-8")
        for removed in ("MigrationExecutor", "MigrationHost", "CiFixtureDiagnostics"):
            self.assertNotIn(removed, solution + project)
        self.assertIn("Project1.Migrations.Tests.csproj", solution)
        self.assertIn("Project1.Api.csproj", project)
        self.assertTrue((ROOT / "tests/Project1.Migrations.Tests/SqlBatchParser.cs").is_file())

    def test_automatic_execution_source_is_removed_not_only_disabled(self):
        removed = [
            "scripts/deploy/ci_host_acceptance.py",
            "scripts/deploy/execute_migration_verified.py",
            "scripts/deploy/migration_approval.py",
            "scripts/deploy/migration_approval_ledger.py",
            "scripts/deploy/migration_execution.py",
            "scripts/deploy/production_entry.py",
            "scripts/deploy/production_migration.py",
            "scripts/deploy/project1-migration-admin",
            "scripts/deploy/project1-migration-execute",
            "scripts/deploy/project1-migration-execute.sudoers",
            "scripts/deploy/dotnet/Project1.MigrationHost/Project1.MigrationHost.csproj",
            "scripts/deploy/dotnet/Project1.MigrationExecutor/Project1.MigrationExecutor.csproj",
            "tests/Project1.MigrationHost.Acceptance/Project1.MigrationHost.Acceptance.csproj",
        ]
        for path in removed:
            with self.subTest(path=path):
                self.assertFalse((ROOT / path).exists())


if __name__ == "__main__":
    unittest.main()
