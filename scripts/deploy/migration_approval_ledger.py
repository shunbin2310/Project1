"""Uninstalled approval bookkeeping library. No CLI, SQL, SSH or authorization output.

Pure transition functions are for sandbox testing, not an authorization boundary.
Linux storage requires an existing root-owned private directory. Future callers
must supply verified packages/live status from trusted code and separate backup
evidence; the supplied snapshots in this stage do not establish their authenticity.
"""

from __future__ import annotations

from contextlib import contextmanager
import copy
from datetime import datetime, timedelta
import hashlib
import json
import os
from pathlib import Path
import stat
import sys
import uuid

from migration_approval import (exact_fields, hex_value, timestamp, validate_approval)
from migration_package import strict_json
from verify_ci_artifacts import REPOSITORY_ID, VerificationError
from migration_target import PRODUCTION_TARGET, ReviewTarget

MAX_ENTRIES = 64
MAX_LEDGER_BYTES = 16 * 1024 * 1024
PHASES = {"approved", "claimed", "succeeded", "failed", "interrupted"}
BLOCKING = {"claimed", "failed", "interrupted"}
ENTRY_FIELDS = {"approval", "approval_sha256", "registered_at", "phase", "attempt_id",
                "claimed_at", "completed_at"}


class LedgerError(Exception):
    """Only fixed, non-sensitive bookkeeping diagnostics."""


def canonical(value: object) -> bytes:
    try:
        return json.dumps(value, sort_keys=True, separators=(",", ":"), allow_nan=False).encode("ascii")
    except (ValueError, TypeError, UnicodeError):
        raise LedgerError("Invalid ledger JSON values.") from None


def utc_clock(now: datetime) -> None:
    if not isinstance(now, datetime) or now.tzinfo is None or now.utcoffset() != timedelta(0):
        raise LedgerError("Ledger operations require an explicit UTC clock.")


def approval_digest(record: dict) -> str:
    return hashlib.sha256(canonical(record)).hexdigest()


def empty_state() -> dict:
    # Never used implicitly when a persisted ledger is missing or corrupt.
    return {"schema": 1, "entries": {}}


def validate_state(value: object, *, target: ReviewTarget = PRODUCTION_TARGET) -> dict:
    """Check stored structure and internal consistency, NOT approver identity."""
    try:
        state = exact_fields(value, {"schema", "entries"}, "ledger")
        if (type(state["schema"]) is not int or state["schema"] != 1
                or not isinstance(state["entries"], dict) or len(state["entries"]) > MAX_ENTRIES):
            raise LedgerError("Invalid ledger schema or entry limit.")
        for identifier, entry in state["entries"].items():
            hex_value(identifier, 32)
            exact_fields(entry, ENTRY_FIELDS, "ledger entry")
            record = entry["approval"]
            if not isinstance(record, dict) or record.get("approval_id") != identifier:
                raise LedgerError("Ledger approval identity changed.")
            hex_value(entry["approval_sha256"], 64)
            if approval_digest(record) != entry["approval_sha256"]:
                raise LedgerError("Stored approval content changed.")
            registered = timestamp(entry["registered_at"])
            # Revalidate the saved record at registration time, not today's clock.
            # Derived context only checks structure; it is NOT provenance evidence.
            context = {"repository": record.get("repository"), "repository_id": REPOSITORY_ID,
                       "run_id": record.get("run_id"), "run_attempt": record.get("run_attempt"),
                       "commit": record.get("commit"), "sql_sha256": record.get("sql_sha256"),
                       "artifact": {"sha256": record.get("artifact_sha256")},
                       "migrations": record["applied"] + record["pending"]}
            snapshot = {"schema": 1, "server": record.get("server"), "database": record.get("database"),
                        "login": target.login, "checked_at": registered.isoformat(),
                        "migrations": record["applied"]}
            validate_approval(canonical(record), context, canonical(snapshot), now=registered, target=target)
            if not isinstance(entry["phase"], str) or entry["phase"] not in PHASES:
                raise LedgerError("Invalid ledger phase.")
            if entry["phase"] == "approved":
                if any(entry[field] is not None for field in ("attempt_id", "claimed_at", "completed_at")):
                    raise LedgerError("Approved entry contains an unexpected attempt.")
            else:
                hex_value(entry["attempt_id"], 32)
                claimed = timestamp(entry["claimed_at"])
                if not registered <= claimed < timestamp(record["expires_at"]):
                    raise LedgerError("Invalid claim timeline.")
                if entry["phase"] == "claimed":
                    if entry["completed_at"] is not None:
                        raise LedgerError("Unresolved claim contains a completion time.")
                elif timestamp(entry["completed_at"]) < claimed:
                    raise LedgerError("Invalid completion timeline.")
        if sum(entry["phase"] in BLOCKING for entry in state["entries"].values()) > 1:
            raise LedgerError("Contradictory unresolved ledger attempts.")
        if len(canonical(state)) > MAX_LEDGER_BYTES:
            raise LedgerError("Ledger exceeds its size limit.")
        return state
    except (VerificationError, KeyError, TypeError, ValueError, UnicodeError):
        raise LedgerError("Invalid or inconsistent ledger record; administrator review required.") from None


def decode_state(body: bytes, *, target: ReviewTarget = PRODUCTION_TARGET) -> dict:
    if not isinstance(body, bytes) or not 0 < len(body) <= MAX_LEDGER_BYTES:
        raise LedgerError("Invalid ledger file size.")
    try:
        return validate_state(strict_json(body), target=target)
    except (VerificationError, ValueError, UnicodeError):
        raise LedgerError("Invalid ledger JSON; administrator review required.") from None


def register_record(state: dict, approval_body: bytes | str, verified: dict,
                    status_body: bytes | str, *, now: datetime, target: ReviewTarget = PRODUCTION_TARGET) -> dict:
    """Future administrator-only operation. No replacement of any prior ID."""
    validate_state(state, target=target)
    utc_clock(now)
    try:
        validate_approval(approval_body, verified, status_body, now=now, target=target)
        record = strict_json(approval_body)
    except (VerificationError, ValueError, TypeError, UnicodeError):
        raise LedgerError("Approval registration validation failed.") from None
    identifier = record["approval_id"]
    if identifier in state["entries"]:
        raise LedgerError("Approval identifier already exists; replacement is refused.")
    if len(state["entries"]) >= MAX_ENTRIES:
        raise LedgerError("Ledger is full; no automatic deletion or pruning.")
    updated = copy.deepcopy(state)
    updated["entries"][identifier] = {
        "approval": record, "approval_sha256": approval_digest(record),
        "registered_at": now.isoformat(), "phase": "approved",
        "attempt_id": None, "claimed_at": None, "completed_at": None}
    return validate_state(updated, target=target)


def claim_record(state: dict, identifier: str, verified: dict, status_body: bytes | str,
                 *, now: datetime, target: ReviewTarget = PRODUCTION_TARGET) -> tuple[dict, dict]:
    """Use ONLY the stored approval, never caller-supplied replacement JSON."""
    validate_state(state, target=target)
    utc_clock(now)
    try:
        hex_value(identifier, 32)
    except VerificationError:
        raise LedgerError("Invalid approval identifier.") from None
    entry = state["entries"].get(identifier)
    if entry is None:
        raise LedgerError("Approval identifier is not registered.")
    if entry["phase"] != "approved":
        raise LedgerError("Approval has already been claimed; reuse is refused.")
    if any(item["phase"] in BLOCKING for item in state["entries"].values()):
        raise LedgerError("An unresolved attempt blocks new claims; administrator review required.")
    if now < timestamp(entry["registered_at"]):
        raise LedgerError("Claim clock precedes registration.")
    try:
        validate_approval(canonical(entry["approval"]), verified, status_body, now=now, target=target)
    except (VerificationError, ValueError, TypeError, UnicodeError):
        raise LedgerError("Stored approval no longer matches the package, history or validity window.") from None
    updated = copy.deepcopy(state)
    candidate = updated["entries"][identifier]
    candidate.update(phase="claimed", attempt_id=uuid.uuid4().hex, claimed_at=now.isoformat())
    validate_state(updated, target=target)
    receipt = {"approval_id": identifier, "attempt_id": candidate["attempt_id"],
               "approval_sha256": candidate["approval_sha256"],
               "execution_authorized": False, "sql_executed": False}
    return updated, receipt


def finish_record(state: dict, receipt: dict, outcome: str, *, now: datetime,
                  target: ReviewTarget = PRODUCTION_TARGET) -> dict:
    """Bookkeeping only; a caller-supplied result is NOT proof that SQL ran."""
    validate_state(state, target=target)
    utc_clock(now)
    try:
        exact_fields(receipt, {"approval_id", "attempt_id", "approval_sha256",
                               "execution_authorized", "sql_executed"}, "claim receipt")
        hex_value(receipt["approval_id"], 32)
        hex_value(receipt["attempt_id"], 32)
        hex_value(receipt["approval_sha256"], 64)
    except VerificationError:
        raise LedgerError("Invalid claim receipt.") from None
    if receipt["execution_authorized"] is not False or receipt["sql_executed"] is not False:
        raise LedgerError("This ledger cannot attest execution or authorize SQL.")
    if not isinstance(outcome, str) or outcome not in {"succeeded", "failed", "interrupted"}:
        raise LedgerError("Unsupported terminal outcome; approvals cannot be rearmed.")
    entry = state["entries"].get(receipt["approval_id"])
    if (entry is None or entry["phase"] != "claimed"
            or any(entry[field] != receipt[field] for field in ("attempt_id", "approval_sha256"))):
        raise LedgerError("Receipt does not match an unresolved claim.")
    if now < timestamp(entry["claimed_at"]):
        raise LedgerError("Completion clock precedes claim.")
    updated = copy.deepcopy(state)
    updated["entries"][receipt["approval_id"]].update(phase=outcome, completed_at=now.isoformat())
    return validate_state(updated, target=target)


def _require_owner(uid: int) -> None:
    if uid != 0:
        raise LedgerError("Ledger storage must be root-owned.")


def _trusted_directory(path: Path) -> None:
    if not path.is_absolute() or ".." in path.parts:
        raise LedgerError("Ledger requires an absolute, trusted directory.")
    for current in reversed((path, *path.parents)):
        info = current.lstat()
        _require_owner(info.st_uid)
        if not stat.S_ISDIR(info.st_mode) or info.st_mode & 0o022:
            raise LedgerError("Unsafe ledger directory or ancestor.")
    if stat.S_IMODE(info.st_mode) != 0o700:
        raise LedgerError("Ledger directory must have mode 0700.")


def _private_file(info: os.stat_result) -> None:
    _require_owner(info.st_uid)
    if (not stat.S_ISREG(info.st_mode) or info.st_nlink != 1
            or stat.S_IMODE(info.st_mode) != 0o600):
        raise LedgerError("Ledger files must be private, regular, single-link files.")


class ApprovalLedger:
    """Linux persistence backend, deliberately with no production path or entrypoint.

    The directory must already exist. No root fallback, owner override, automatic
    initialization, cleanup, recovery, or SQL callbacks are provided. register()
    is an administrative API and MUST NOT be exposed to the deployment user.
    """

    def __init__(self, directory: Path, *, target: ReviewTarget = PRODUCTION_TARGET):
        self.directory = Path(directory)
        self.target = target  # Trusted host policy, never selected from saved/caller JSON.

    @contextmanager
    def _directory(self):
        if sys.platform != "linux" or os.geteuid() != 0:
            raise LedgerError("Persistent ledger operations require Linux root.")
        _trusted_directory(self.directory)
        descriptor = os.open(self.directory, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
        try:
            info = os.fstat(descriptor)
            _require_owner(info.st_uid)
            if not stat.S_ISDIR(info.st_mode) or stat.S_IMODE(info.st_mode) != 0o700:
                raise LedgerError("Unsafe opened ledger directory.")
            yield descriptor
        finally:
            os.close(descriptor)

    def initialize(self) -> None:
        """Future admin-only explicit initialization; retain incomplete artifacts."""
        with self._directory() as directory:
            # Never overwrite/reinitialize a partial or existing ledger.
            for name in ("ledger.lock", "ledger.json"):
                try:
                    os.stat(name, dir_fd=directory, follow_symlinks=False)
                except FileNotFoundError:
                    continue
                raise LedgerError("Ledger artifacts already exist; initialization is refused.")
            for name, body in (("ledger.lock", b""), ("ledger.json", canonical(empty_state()))):
                descriptor = os.open(name, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW,
                                     0o600, dir_fd=directory)
                with os.fdopen(descriptor, "wb") as stream:
                    _private_file(os.fstat(stream.fileno()))
                    stream.write(body)
                    stream.flush()
                    os.fsync(stream.fileno())
            os.fsync(directory)

    @contextmanager
    def _locked(self):
        with self._directory() as directory:
            import fcntl  # Linux persistence only; never substituted in production.
            descriptor = os.open("ledger.lock", os.O_RDWR | os.O_NOFOLLOW | os.O_NONBLOCK,
                                 dir_fd=directory)  # Missing lock is an error, never recreated.
            try:
                info = os.fstat(descriptor)
                _private_file(info)
                if info.st_size != 0:
                    raise LedgerError("Unexpected ledger lock contents.")
                try:
                    fcntl.flock(descriptor, fcntl.LOCK_EX | fcntl.LOCK_NB)
                except BlockingIOError:
                    raise LedgerError("Another ledger operation holds the lock.") from None
                linked = os.stat("ledger.lock", dir_fd=directory, follow_symlinks=False)
                if (linked.st_dev, linked.st_ino) != (info.st_dev, info.st_ino):
                    raise LedgerError("Ledger lock changed while acquiring it.")
                yield directory
            finally:
                os.close(descriptor)

    def _read(self, directory: int) -> dict:
        descriptor = os.open("ledger.json", os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK,
                             dir_fd=directory)
        with os.fdopen(descriptor, "rb") as stream:
            info = os.fstat(stream.fileno())
            _private_file(info)
            if not 0 < info.st_size <= MAX_LEDGER_BYTES:
                raise LedgerError("Invalid persisted ledger size.")
            return decode_state(stream.read(MAX_LEDGER_BYTES + 1), target=self.target)

    def _write(self, directory: int, state: dict) -> None:
        body = canonical(validate_state(state, target=self.target))
        name = ".ledger-" + uuid.uuid4().hex
        descriptor = os.open(name, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW,
                             0o600, dir_fd=directory)
        try:
            with os.fdopen(descriptor, "wb") as stream:
                _private_file(os.fstat(stream.fileno()))
                stream.write(body)
                stream.flush()
                os.fsync(stream.fileno())
            os.replace(name, "ledger.json", src_dir_fd=directory, dst_dir_fd=directory)
            os.fsync(directory)
        finally:
            try:
                os.unlink(name, dir_fd=directory)
            except FileNotFoundError:
                pass

    def inspect(self) -> dict:
        with self._locked() as directory:
            return self._read(directory)

    def register(self, approval_body: bytes | str, verified: dict, status_body: bytes | str,
                 *, now: datetime) -> None:
        with self._locked() as directory:
            state = register_record(self._read(directory), approval_body, verified, status_body,
                                    now=now, target=self.target)
            self._write(directory, state)

    def claim(self, identifier: str, verified: dict, status_body: bytes | str, *, now: datetime) -> dict:
        with self._locked() as directory:
            state, receipt = claim_record(self._read(directory), identifier, verified, status_body,
                                          now=now, target=self.target)
            self._write(directory, state)  # No receipt is returned before both fsyncs succeed.
            return receipt

    def finish(self, receipt: dict, outcome: str, *, now: datetime) -> None:
        with self._locked() as directory:
            state = finish_record(self._read(directory), receipt, outcome, now=now, target=self.target)
            self._write(directory, state)
