"""Offline review-record validation only. NOT authorization or a SQL executor."""

from __future__ import annotations

import argparse
from datetime import datetime, timedelta, timezone
import json
from pathlib import Path
import re
import stat

from migration_package import migration_ids, strict_json
from precheck_migrations import compare_history
from verify_ci_artifacts import REPOSITORY, REPOSITORY_ID, VerificationError
from verify_migration_artifact import load_verified
from migration_target import PRODUCTION_TARGET, ReviewTarget

MAX_RECORD_BYTES = 180000
MAX_APPROVAL_LIFETIME = timedelta(hours=1)
MAX_STATUS_AGE = timedelta(minutes=5)
MAX_BACKUP_AGE = timedelta(hours=24)
FIELDS = {"schema", "purpose", "approval_id", "repository", "server", "database",
          "run_id", "run_attempt", "commit", "artifact_sha256", "sql_sha256",
          "applied", "pending", "approved_at", "expires_at", "backup", "confirmations"}
CONFIRMATIONS = {"sql_reviewed", "target_schema_reviewed", "restore_plan_ready"}


def exact_fields(value: object, fields: set[str], label: str) -> dict:
    if not isinstance(value, dict) or set(value) != fields:
        raise VerificationError(f"Invalid {label} fields.")
    return value


def timestamp(value: object) -> datetime:
    # Explicit UTC timestamps only; no local-time assumptions or date-only values.
    if not isinstance(value, str) or not re.fullmatch(
            r"[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,6})?(?:Z|\+00:00)", value):
        raise VerificationError("Review timestamps must be explicit UTC date-times.")
    try:
        return datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        raise VerificationError("Invalid review timestamp.") from None


def hex_value(value: object, length: int) -> None:
    if not isinstance(value, str) or not re.fullmatch(r"[0-9a-f]{" + str(length) + r"}", value):
        raise VerificationError("Invalid review identifier or lowercase digest.")


def read_record(path: Path) -> bytes:
    # Only explicitly supplied review inputs, never application/production secrets.
    info = path.lstat()
    if not stat.S_ISREG(info.st_mode) or info.st_nlink != 1 or not 0 < info.st_size <= MAX_RECORD_BYTES:
        raise VerificationError("Review input must be a bounded, regular, single-link file.")
    with path.open("rb") as stream:
        body = stream.read(MAX_RECORD_BYTES + 1)
    if not 0 < len(body) <= MAX_RECORD_BYTES:
        raise VerificationError("Review input exceeds its size limit.")
    return body


def validate_approval(approval_body: bytes | str, verified: dict, status_body: bytes | str,
                      *, now: datetime, target: ReviewTarget = PRODUCTION_TARGET) -> dict:
    """Compare a record to load_verified() output and a supplied status snapshot.

    Neither JSON nor local validation establishes an approver's identity. A future
    executor MUST require administrator-controlled approval storage, consumption,
    fresh live checks under its lock and independently verified backup evidence.
    """
    for body in (approval_body, status_body):
        if not isinstance(body, (bytes, str)) or not 0 < len(body) <= MAX_RECORD_BYTES:
            raise VerificationError("Invalid review input size.")
    if not isinstance(now, datetime) or now.tzinfo is None or now.utcoffset() != timedelta(0):
        raise VerificationError("Review validation requires an explicit UTC clock.")
    approval = exact_fields(strict_json(approval_body), FIELDS, "approval")
    if (type(approval["schema"]) is not int or approval["schema"] != 1
            or approval["purpose"] != "migration-execution-review"
            or approval["repository"] != REPOSITORY
            or approval["server"] != target.server or approval["database"] != target.database):
        raise VerificationError("Unexpected approval schema, purpose or target.")
    hex_value(approval["approval_id"], 32)
    hex_value(approval["commit"], 40)
    for name in ("artifact_sha256", "sql_sha256"):
        hex_value(approval[name], 64)
    for name in ("run_id", "run_attempt"):
        if type(approval[name]) is not int or not 0 < approval[name] < 10**20:
            raise VerificationError("Approval CI identifiers must be positive JSON integers.")
    if (verified.get("repository") != REPOSITORY
            or type(verified.get("repository_id")) is not int
            or verified["repository_id"] != REPOSITORY_ID
            or any(type(verified.get(name)) is not int for name in ("run_id", "run_attempt"))
            or any(approval[name] != verified.get(name) for name in
                   ("commit", "run_id", "run_attempt", "sql_sha256"))
            or approval["artifact_sha256"] != verified.get("artifact", {}).get("sha256")):
        raise VerificationError("Approval does not match the selected verified CI package.")
    applied = migration_ids(approval["applied"], allow_empty=True)
    pending = migration_ids(approval["pending"])  # Empty approvals cannot authorize work.
    target_ids = migration_ids(verified["migrations"])
    if applied + pending != target_ids:
        raise VerificationError("Approval history and pending migrations do not match the package.")
    approved, expires = timestamp(approval["approved_at"]), timestamp(approval["expires_at"])
    if not approved <= now < expires or not timedelta(0) < expires - approved <= MAX_APPROVAL_LIFETIME:
        raise VerificationError("Approval is future-dated, expired or exceeds its maximum lifetime.")
    confirmations = exact_fields(approval["confirmations"], CONFIRMATIONS, "review confirmation")
    if any(value is not True for value in confirmations.values()):
        raise VerificationError("SQL, target schema and restore plan must be explicitly confirmed.")
    backup = exact_fields(approval["backup"], {"sha256", "completed_at", "verified"}, "backup confirmation")
    hex_value(backup["sha256"], 64)
    completed = timestamp(backup["completed_at"])
    if backup["verified"] is not True or completed > approved or now - completed > MAX_BACKUP_AGE:
        raise VerificationError("A recent, verified pre-approval backup must be confirmed.")
    status = strict_json(status_body)
    if not isinstance(status, dict):
        raise VerificationError("Invalid history snapshot.")
    checked = timestamp(status.get("checked_at"))
    if not timedelta(0) <= now - checked <= MAX_STATUS_AGE:
        raise VerificationError("History snapshot is stale or future-dated.")
    report = compare_history(verified, json.dumps(status), target=target)
    if report["applied"] != applied or report["pending"] != pending:
        raise VerificationError("Database history changed from the approved migration range.")
    return {"schema": 1, "approval_id": approval["approval_id"], "commit": approval["commit"],
            "pending_count": len(pending), "record_valid": True,
            "execution_authorized": False, "sql_executed": False}


def main(arguments=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", required=True, type=Path,
                        help="Existing verified migration package directory (offline recheck).")
    parser.add_argument("--approval", required=True, type=Path)
    parser.add_argument("--status", required=True, type=Path,
                        help="Previously captured read-only status JSON; no live query.")
    options = parser.parse_args(arguments)
    try:
        verified = load_verified(options.directory)
        result = validate_approval(read_record(options.approval), verified, read_record(options.status),
                                   now=datetime.now(timezone.utc))
        print(json.dumps(result, separators=(",", ":")))
        print("OFFLINE REVIEW ONLY: no execution authorization, SQL, upload or service restart.")
        return 0
    except VerificationError as error:
        print(f"ERROR: {error}")
    except Exception as error:
        # Do not echo arbitrary supplied JSON, paths or library exception messages.
        print(f"ERROR: Offline approval validation failed ({type(error).__name__}).")
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
