"""Manually triggered SQL upload and fixed server backup. NEVER apply migrations."""

from __future__ import annotations

from datetime import datetime
import argparse
import os
from pathlib import Path
import re
import sys
import tempfile
import uuid

from deploy_verified import HOST, USER, DESTINATION, run_command, ssh_options
from migration_package import strict_json, migration_ids
from verify_ci_artifacts import REPOSITORY, VerificationError
from verify_migration_artifact import load_verified

HELPER = "/usr/local/sbin/project1-migration-prepare"
USAGE = "Usage: project1-migration-prepare COMMIT RUN_ID ATTEMPT TOKEN ZIP_SHA256"
STATE = "/var/lib/project1-migration-prepare"
BACKUPS = "/var/opt/mssql/backup"


def validate_report(body: str, manifest: dict, token: str) -> dict:
    if len(body) > 180000:
        raise VerificationError("Preparation report exceeded its limit.")
    report = strict_json(body)
    keys = {"schema", "commit", "run_id", "run_attempt", "token", "server", "database",
            "sql_sha256", "sql_path", "backup_path", "backup_sha256", "backup_size",
            "prepared_at", "pending", "sql_executed", "api_stopped", "backup_verified"}
    if not isinstance(report, dict) or set(report) != keys:
        raise VerificationError("Unexpected preparation report shape.")
    expected = {"schema": 1, "commit": manifest["commit"], "run_id": manifest["run_id"],
                "run_attempt": manifest["run_attempt"], "token": token,
                "server": "homelab-server", "database": "Project1Db",
                "sql_sha256": manifest["sql_sha256"], "sql_executed": False,
                "api_stopped": True, "backup_verified": True,
                "sql_path": f"{STATE}/{token}/migrations.sql",
                "backup_path": f"{BACKUPS}/project1-prepare-{token}/Project1Db.bak"}
    if any(type(report[key]) is not type(value) or report[key] != value for key, value in expected.items()):
        raise VerificationError("Preparation identity, paths or completion flags do not match.")
    if (type(report["backup_size"]) is not int or report["backup_size"] <= 0
            or not isinstance(report["backup_sha256"], str)
            or not re.fullmatch(r"[0-9a-f]{64}", report["backup_sha256"])):
        raise VerificationError("Invalid backup digest or size.")
    if not isinstance(report["prepared_at"], str) or datetime.fromisoformat(report["prepared_at"]).tzinfo is None:
        raise VerificationError("Preparation timestamp must include a timezone.")
    pending = migration_ids(report["pending"])
    if pending != manifest["migrations"][-len(pending):]:
        raise VerificationError("Pending migrations do not match the selected package.")
    return report


def prepare(directory: Path, key_value: str, hosts_value: str, runner_temp: Path) -> dict:
    manifest = load_verified(directory)  # BEFORE any SSH or upload.
    if not key_value.strip() or not hosts_value.strip():
        raise VerificationError("SSH credentials are required for explicit preparation.")
    token = uuid.uuid4().hex
    print(f"Preparation request: {token}; commit: {manifest['commit']}; CI: {manifest['run_id']}/{manifest['run_attempt']}.")
    with tempfile.TemporaryDirectory(prefix="project1-prepare-ssh-", dir=runner_temp) as temporary:
        root = Path(temporary)
        key, hosts = root / "key", root / "known_hosts"
        key.write_text(key_value.replace("\r", "").rstrip("\n") + "\n", encoding="utf-8")
        hosts.write_text(hosts_value.replace("\r", "").rstrip("\n") + "\n", encoding="utf-8")
        key.chmod(0o600)
        hosts.chmod(0o600)
        run_command(["/usr/bin/ssh-keygen", "-y", "-P", "", "-f", str(key)])
        run_command(["/usr/bin/ssh-keygen", "-lf", str(hosts)])
        run_command(["/usr/bin/ssh-keygen", "-F", HOST, "-f", str(hosts)])
        options = ssh_options(key, hosts)
        ssh = ["/usr/bin/ssh", "-T", *options, DESTINATION]
        if run_command([*ssh, "whoami"]) != USER:
            raise VerificationError("SSH returned an unexpected user.")
        if run_command([*ssh, f"sudo -n -- {HELPER} --help"]) != USAGE:
            raise VerificationError("Install and review the fixed preparation helper first.")
        print("Checking that the administrator stopped the API; no service changes will be made.")
        # Refuse before uploading if the administrator has not stopped the API.
        if run_command([*ssh, f"sudo -n -- {HELPER} --check-stopped"]) != "API_STOPPED":
            raise VerificationError("API stop could not be confirmed.")
        remote = f"/home/{USER}/uploads/migration-uploads/{token}"
        print("Uploading verified SQL package to a new private directory.")
        run_command([*ssh, f"umask 077; mkdir -m 0700 -- {remote}"])
        run_command(["/usr/bin/scp", *options, str(directory / "migrations.zip"),
                     f"{DESTINATION}:{remote}/"], timeout=300)
        run_command([*ssh, f"chmod 0600 -- {remote}/migrations.zip"])
        arguments = f"{manifest['commit']} {manifest['run_id']} {manifest['run_attempt']} {token} {manifest['artifact']['sha256']}"
        print("Requesting fixed server backup/checksum verification only. Uploaded SQL will NOT run.")
        output = run_command([*ssh, f"sudo -n -- {HELPER} {arguments}"], timeout=1100)
        return validate_report(output, manifest, token)  # No retry on uncertain success.


def report_text(report: dict) -> str:
    return "\n".join([
        "## Migration preparation ONLY", "", f"Commit: `{report['commit']}`",
        f"CI run: {report['run_id']}, attempt: {report['run_attempt']}",
        f"Prepared at: {report['prepared_at']}", f"SQL: `{report['sql_path']}`",
        f"SQL SHA-256: `{report['sql_sha256']}`", f"Backup: `{report['backup_path']}`",
        f"Backup SHA-256: `{report['backup_sha256']}`", f"Backup bytes: {report['backup_size']}",
        "", "Pending migrations:", *[f"- {item}" for item in report["pending"]], "",
        "Backup checksum verification passed; this is NOT an actual restore rehearsal.",
        "API remains stopped. No uploaded SQL was executed. No application was deployed.",
        "Administrator: review SQL, retain a protected off-server backup, manually apply and verify,",
        "then start the compatible API and separately deploy the matching application version.",
        "If writes resume, refresh the backup before migration. Preparation is not execution approval.", ""])


def main(arguments=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", required=True, type=Path)
    options = parser.parse_args(arguments)
    try:
        if (sys.platform != "linux" or os.environ.get("GITHUB_REPOSITORY") != REPOSITORY
                or os.environ.get("GITHUB_REF") != "refs/heads/main"
                or os.environ.get("GITHUB_EVENT_NAME") != "workflow_dispatch"
                or os.environ.get("MIGRATION_PREPARE_CONFIRMED") != "true"
                or os.environ.get("DEPLOY_CONFIRMED") == "true"
                or os.environ.get("MIGRATION_PRECHECK_CONFIRMED") == "true"):
            raise VerificationError("Preparation requires exclusive, explicit manual-main confirmation.")
        os.umask(0o077)
        key = os.environ.pop("DEPLOY_SSH_PRIVATE_KEY", "")
        hosts = os.environ.pop("DEPLOY_SSH_KNOWN_HOSTS", "")
        report = prepare(options.directory, key, hosts, Path(os.environ["RUNNER_TEMP"]))
        text = report_text(report)
        print(text)
        if os.environ.get("GITHUB_STEP_SUMMARY"):
            with Path(os.environ["GITHUB_STEP_SUMMARY"]).open("a", encoding="utf-8") as output:
                output.write(text)
        return 0
    except Exception as error:
        # Never publish subprocess output, credentials or an unvalidated remote report.
        print(f"ERROR: Preparation failed ({type(error).__name__}). Inspect server state; do not blindly retry. API was not started.")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
