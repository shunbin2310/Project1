"""Execution coordinator library, never itself a sudo command or CLI.

The trusted host supplies the ledger, online provenance checker, independent
backup verifier and a database session which holds a session-owned SQL lock.
These are NOT callbacks or JSON that a deployment user may choose. Disposable CI
fixtures and the separately reviewed root-managed host supply distinct adapters.
"""

from __future__ import annotations

import copy
from dataclasses import dataclass
from datetime import datetime, timedelta
import hashlib
from pathlib import Path
from typing import Protocol, ContextManager
import zipfile

from migration_approval import exact_fields, timestamp, validate_approval
from migration_approval_ledger import ApprovalLedger, BLOCKING, approval_digest, canonical, utc_clock
from migration_package import LIMITS, strict_json
from migration_target import ReviewTarget
from precheck_migrations import compare_history
from verify_migration_artifact import load_verified


NOTE_MIGRATION = "20261009164508_AddProductExampleAttr"


class ExecutionError(Exception):
    """Fixed safe diagnostic; underlying errors must not reach deployment logs."""


@dataclass(frozen=True)
class VerifiedSql:
    manifest: dict
    sql: bytes

    @classmethod
    def from_directory(cls, directory: Path):
        manifest = load_verified(directory)
        with zipfile.ZipFile(directory / "migrations.zip") as archive:
            with archive.open("migrations.sql") as stream:
                sql = stream.read(LIMITS["migrations.sql"] + 1)
        value = cls(copy.deepcopy(manifest), sql)
        value.check_bytes()
        return value

    def check_bytes(self):
        if (not isinstance(self.sql, bytes) or not 0 < len(self.sql) <= LIMITS["migrations.sql"]
                or hashlib.sha256(self.sql).hexdigest() != self.manifest.get("sql_sha256")):
            raise ExecutionError("Execution SQL does not match the verified digest.")
        # The SQL implementation rejects directives and parses these immutable bytes.
        self.sql.decode("utf-8-sig", errors="strict")


class LockedSession(Protocol):
    def status(self) -> bytes: ...
    def assert_before(self) -> None: ...
    def apply(self, sql: bytes, sql_sha256: str) -> None: ...
    def assert_after(self) -> None: ...


class ExecutionServices(Protocol):
    target: ReviewTarget
    def now(self) -> datetime: ...
    def package(self) -> VerifiedSql: ...
    def online_check(self, manifest: dict) -> None: ...
    def session(self) -> ContextManager[LockedSession]: ...
    def backup_evidence(self, approval: dict) -> dict: ...


def check_backup(evidence: dict, approval: dict, target: ReviewTarget, now: datetime):
    """Evidence comes from an independent trusted verifier, not request JSON.

    The production adapter must hash the actual private backup, inspect its
    database/header and run CHECKSUM VERIFYONLY itself. Boolean input is no proof.
    """
    exact_fields(evidence, {"server", "database", "sha256", "completed_at", "checked_at",
                            "checksum_valid", "restore_verified", "copy_only"}, "backup evidence")
    utc_clock(now)
    checked = timestamp(evidence["checked_at"])
    if (evidence["server"] != target.server or evidence["database"] != target.database
            or evidence["sha256"] != approval["backup"]["sha256"]
            or timestamp(evidence["completed_at"]) != timestamp(approval["backup"]["completed_at"])
            or not timedelta(0) <= now - checked <= timedelta(minutes=5)
            or any(evidence[name] is not True for name in ("checksum_valid", "restore_verified", "copy_only"))):
        raise ExecutionError("Independent backup evidence does not match the approval.")


def execute(identifier: str, ledger: ApprovalLedger, services: ExecutionServices) -> dict:
    """Never initialize/register approvals, retry SQL, clear blockers or restart apps.

    Claim is durable before SQL. Any failure after claiming blocks further work.
    If completion persistence fails, the unresolved claim is retained. Session
    context MUST close its unpooled connection and roll back any open transaction
    before releasing its database lock, even if the coordinator is interrupted.
    """
    receipt = None
    try:
        if ledger.target != services.target:
            raise ExecutionError("Ledger and execution target differ.")
        state = ledger.inspect()
        if any(item["phase"] in BLOCKING for item in state["entries"].values()):
            raise ExecutionError("Unresolved execution blocks new work.")
        entry = state["entries"].get(identifier)
        if entry is None or entry["phase"] != "approved":
            raise ExecutionError("An unconsumed administrator approval is required.")
        approval = copy.deepcopy(entry["approval"])
        if approval["pending"] != [NOTE_MIGRATION]:
            raise ExecutionError("Only the reviewed optional Product Note upgrade is supported.")
        package = services.package()
        # Own immutable SQL and metadata; services cannot replace either after checking.
        package = VerifiedSql(copy.deepcopy(package.manifest), bytes(package.sql))
        package.check_bytes()
        services.online_check(copy.deepcopy(package.manifest))
        with services.session() as session:
            try:
                status = session.status()
                validate_approval(canonical(approval), package.manifest, status,
                                  now=services.now(), target=services.target)
                session.assert_before()
                check_backup(services.backup_evidence(copy.deepcopy(approval)), approval,
                             services.target, services.now())
                # Requery while still holding the same DB lock; backup verification can take time.
                status = session.status()
                receipt = ledger.claim(identifier, package.manifest, status, now=services.now())
                if receipt["approval_sha256"] != approval_digest(approval):
                    raise ExecutionError("Stored approval changed during preflight.")
                services.online_check(copy.deepcopy(package.manifest))
                # Do not start SQL if provenance checking consumed the remaining approval window.
                validate_approval(canonical(approval), package.manifest, session.status(),
                                  now=services.now(), target=services.target)
                session.assert_before()
                package.check_bytes()
                session.apply(package.sql, package.manifest["sql_sha256"])
                after = session.status()
                report = compare_history(package.manifest, after.decode("utf-8"), target=services.target)
                checked = timestamp(strict_json(after)["checked_at"])
                if (report["pending"] or report["applied"] != package.manifest["migrations"]
                        or not timedelta(0) <= services.now() - checked <= timedelta(minutes=5)):
                    raise ExecutionError("Post-execution migration history did not match.")
                session.assert_after()
                ledger.finish(receipt, "succeeded", now=services.now())
                receipt = None  # Completion has been persisted; never try to overwrite it.
            except BaseException as error:
                if receipt is not None:
                    try:
                        ledger.finish(receipt, "interrupted" if not isinstance(error, Exception) else "failed",
                                      now=services.now())
                    except BaseException:
                        # Fail closed: a claimed entry is already durable. Never clear it.
                        raise ExecutionError("Execution outcome could not be recorded; administrator review required.") from None
                raise
        return {"schema": 1, "approval_id": identifier, "commit": package.manifest["commit"],
                "phase": "succeeded", "sql_executed": True, "production_enabled": False}
    except (KeyboardInterrupt, SystemExit):
        raise ExecutionError("Execution interrupted; administrator review required.") from None
    except Exception:
        # No SQL text, connection strings, subprocess output or input records in errors.
        raise ExecutionError("Execution refused or failed; inspect trusted audit state before any further action.") from None
