"""Explicitly query fixed migration history over SSH; never upload or execute SQL."""

from __future__ import annotations

import argparse
from datetime import datetime
import os
from pathlib import Path
import sys
import tempfile

from deploy_verified import HOST, USER, DESTINATION, run_command, ssh_options
from migration_package import migration_ids, strict_json
from verify_ci_artifacts import REPOSITORY, VerificationError
from verify_migration_artifact import load_verified
from migration_target import PRODUCTION_TARGET, ReviewTarget


def compare_history(manifest: dict, response: str, *, target: ReviewTarget = PRODUCTION_TARGET) -> dict:
    if len(response) > 180000:
        raise VerificationError("Remote status exceeded its size limit.")
    status = strict_json(response)
    if (not isinstance(status, dict) or set(status) != {"schema", "server", "database", "login", "checked_at", "migrations"}
            or type(status["schema"]) is not int or status["schema"] != 1
            or status["server"] != target.server or status["database"] != target.database
            or status["login"] != target.login or not isinstance(status["checked_at"], str)):
        raise VerificationError("Unexpected fixed server/database/login status identity.")
    checked_at = datetime.fromisoformat(status["checked_at"])
    if checked_at.tzinfo is None:
        raise VerificationError("Migration status timestamp must include a timezone.")
    applied = migration_ids(status["migrations"], allow_empty=True)
    target_ids = migration_ids(manifest["migrations"])
    if applied != target_ids[:len(applied)]:
        raise VerificationError("Database history is not an exact prefix of the selected version; unknown, missing or newer migrations require review.")
    return {"schema": 1, "commit": manifest["commit"], "run_id": manifest["run_id"],
            "run_attempt": manifest["run_attempt"], "sql_sha256": manifest["sql_sha256"],
            "checked_at": checked_at.isoformat(), "applied": applied,
            "pending": target_ids[len(applied):], "sql_executed": False}


def precheck(directory: Path, key_value: str, hosts_value: str, runner_temp: Path) -> dict:
    manifest = load_verified(directory)  # All provenance, digest/layout checks BEFORE SSH.
    if not key_value.strip() or not hosts_value.strip():
        raise VerificationError("SSH credentials are required for the explicit read-only check.")
    with tempfile.TemporaryDirectory(prefix="project1-precheck-ssh-", dir=runner_temp) as temporary:
        root = Path(temporary)
        key, hosts = root / "key", root / "known_hosts"
        key.write_text(key_value.replace("\r", "").rstrip("\n") + "\n", encoding="utf-8")
        hosts.write_text(hosts_value.replace("\r", "").rstrip("\n") + "\n", encoding="utf-8")
        key.chmod(0o600)
        hosts.chmod(0o600)
        run_command(["/usr/bin/ssh-keygen", "-y", "-P", "", "-f", str(key)])
        run_command(["/usr/bin/ssh-keygen", "-lf", str(hosts)])
        run_command(["/usr/bin/ssh-keygen", "-F", HOST, "-f", str(hosts)])
        ssh = ["/usr/bin/ssh", "-T", *ssh_options(key, hosts), DESTINATION]
        if run_command([*ssh, "whoami"]) != USER:
            raise VerificationError("SSH returned an unexpected user.")
        response = run_command([*ssh, "sudo -n -- /usr/local/sbin/project1-migration-status"], timeout=75)
        return compare_history(manifest, response)


def report_text(report: dict) -> str:
    lines = ["## Read-only migration precheck", "", f"Commit: `{report['commit']}`",
             f"CI run: {report['run_id']}, attempt: {report['run_attempt']}",
             f"SQL SHA-256: `{report['sql_sha256']}`", f"Checked at: {report['checked_at']}",
             f"Applied migration count: {len(report['applied'])}",
             f"Latest applied: {report['applied'][-1] if report['applied'] else '(none)'}", "",
             f"Pending migration count: {len(report['pending'])}"]
    lines.extend(f"- {item}" for item in report["pending"])
    lines.extend(["", "No SQL was uploaded or executed. No service was restarted.",
                  "This is a point-in-time history comparison, not schema validation, SQL approval or permission to deploy.",
                  "Production migration still requires SQL review, a backup and separate explicit authorization.", ""])
    return "\n".join(lines)


def main(arguments=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", required=True, type=Path)
    options = parser.parse_args(arguments)
    try:
        if (sys.platform != "linux" or os.environ.get("GITHUB_REPOSITORY") != REPOSITORY
                or os.environ.get("GITHUB_REF") != "refs/heads/main"
                or os.environ.get("GITHUB_EVENT_NAME") != "workflow_dispatch"
                or os.environ.get("MIGRATION_PRECHECK_CONFIRMED") != "true"
                or os.environ.get("DEPLOY_CONFIRMED") == "true"):
            raise VerificationError("Read-only precheck requires explicit manual-main confirmation, without deployment.")
        os.umask(0o077)
        key = os.environ.pop("DEPLOY_SSH_PRIVATE_KEY", "")
        hosts = os.environ.pop("DEPLOY_SSH_KNOWN_HOSTS", "")
        result = precheck(options.directory, key, hosts, Path(os.environ["RUNNER_TEMP"]))
        text = report_text(result)
        print(text)
        if os.environ.get("GITHUB_STEP_SUMMARY"):
            with Path(os.environ["GITHUB_STEP_SUMMARY"]).open("a", encoding="utf-8") as output:
                output.write(text)
        return 0
    except VerificationError as error:
        print(f"ERROR: {error}")
    except Exception as error:
        print(f"ERROR: Read-only migration precheck failed ({type(error).__name__}).")
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
