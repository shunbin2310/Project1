"""Root-managed production adapters. Never import from uploaded artifacts.

Installed only after separate administrator review. No app startup, SSH, SQLCMD,
account provisioning, retry, arbitrary SQL path or caller-selected target.
"""
from __future__ import annotations

import base64
from contextlib import contextmanager
from datetime import datetime, timezone
import hashlib
import os
from pathlib import Path
import re
import select
import stat
import subprocess
import tempfile
import time

from migration_approval import hex_value, validate_approval
from migration_approval_ledger import ApprovalLedger, BLOCKING, canonical
from migration_execution import NOTE_MIGRATION, VerifiedSql, check_backup, execute
from migration_package import strict_json
from migration_target import ReviewTarget
from verify_ci_artifacts import GitHubClient, REPOSITORY, WORKFLOW_PATH, positive_id, validate_run
from verify_migration_artifact import select_artifact, verify

TARGET = ReviewTarget("homelab-server", "Project1Db", "project1_execute")
CONFIG = Path("/etc/project1-migration-execution")
LEDGER = Path("/var/lib/project1-migration-ledger")
BACKUPS = Path("/var/lib/project1-migration-backups")
HOST = Path("/usr/local/libexec/project1-migration-host")
ENABLED = b"ENABLE_REVIEWED_PRODUCT_NOTE_EXECUTION_V1\n"


class ProductionError(Exception):
    pass


def trusted(path: Path, mode: int | None = None, *, directory=False):
    if not path.is_absolute() or ".." in path.parts:
        raise ProductionError("Unsafe installed path.")
    for current in reversed((path, *path.parents)):
        info = current.lstat()
        if (info.st_uid != 0 or info.st_mode & 0o022 or stat.S_ISLNK(info.st_mode)
                or (current != path and not stat.S_ISDIR(info.st_mode))):
            raise ProductionError("Required path is not exclusively root-managed.")
    if ((not stat.S_ISDIR(info.st_mode) if directory else
         not stat.S_ISREG(info.st_mode) or info.st_nlink != 1)
            or (mode is not None and stat.S_IMODE(info.st_mode) != mode)):
        raise ProductionError("Unexpected installed file type or mode.")


def private_read(path: Path, limit: int) -> bytes:
    trusted(path, 0o600)
    descriptor = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(descriptor, "rb") as stream:
        info = os.fstat(stream.fileno())
        if (info.st_uid != 0 or info.st_nlink != 1 or not stat.S_ISREG(info.st_mode)
                or stat.S_IMODE(info.st_mode) != 0o600 or not 0 < info.st_size <= limit):
            raise ProductionError("Unsafe private file.")
        body = stream.read(limit + 1)
    if len(body) > limit:
        raise ProductionError("Private file exceeds its limit.")
    return body


def credential(name: str) -> str:
    value = private_read(CONFIG / name, 256).decode("ascii")
    if not re.fullmatch(r"P1![0-9a-f]{64}\n", value):
        raise ProductionError("Invalid private credential format.")
    return value.rstrip("\n")


def require_installation(*, enabled=True):
    import sys
    if sys.platform != "linux" or os.geteuid() != 0:
        raise ProductionError("Production helper requires Linux root.")
    trusted(CONFIG, 0o700, directory=True)
    trusted(Path("/root"), directory=True)
    if enabled and private_read(CONFIG / "enabled", 128) != ENABLED:
        raise ProductionError("Production execution is not explicitly enabled by the administrator.")
    # No dependency can be replaced by deploy user, SQL service or uploaded package.
    trusted(HOST, 0o755, directory=True)
    for path in HOST.rglob("*"):
        trusted(path, directory=path.is_dir())
    trusted(HOST / "Project1.MigrationHost.dll", 0o644)
    trusted(Path("/usr/bin/dotnet").resolve(strict=True))


def online_check(client, manifest, now):
    workflow = client.get(f"/repos/{REPOSITORY}/actions/workflows/ci.yml")
    if workflow.get("path") != WORKFLOW_PATH or workflow.get("name") != "Project1 CI":
        raise ProductionError("Unexpected source workflow.")
    run_id = positive_id(manifest["run_id"])
    path = f"/repos/{REPOSITORY}/actions/runs/{run_id}"
    workflow_id = positive_id(workflow.get("id"))
    for _ in range(2):
        if validate_run(client.get(path), run_id, manifest["commit"], workflow_id) != manifest["run_attempt"]:
            raise ProductionError("Approved CI attempt changed.")
        if select_artifact(client.artifacts(run_id), run_id, manifest["commit"], now) != manifest["artifact"]:
            raise ProductionError("Approved artifact changed or expired.")


@contextmanager
def verified_backup_file(digest: str):
    hex_value(digest, 64)
    # Root-owned immutable-to-SQL-service parent, group mssql read/traverse only.
    trusted(BACKUPS, 0o750, directory=True)
    path = BACKUPS / (digest + ".bak")
    trusted(path, 0o640)
    import grp
    group = grp.getgrnam("mssql").gr_gid
    if BACKUPS.stat().st_gid != group or path.stat().st_gid != group:
        raise ProductionError("Backup must be readable only by root and the SQL service group.")
    descriptor = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(descriptor, "rb") as stream:
        initial = os.fstat(stream.fileno())
        if (initial.st_uid != 0 or initial.st_gid != group or initial.st_nlink != 1
                or not stat.S_ISREG(initial.st_mode) or stat.S_IMODE(initial.st_mode) != 0o640
                or not 0 < initial.st_size <= 20 * 1024**3):
            raise ProductionError("Unsafe backup file.")
        def check():
            stream.seek(0)
            hasher = hashlib.sha256()
            deadline = time.monotonic() + 120
            while chunk := stream.read(1024 * 1024):
                if time.monotonic() > deadline:
                    raise ProductionError("Backup hash deadline exceeded.")
                hasher.update(chunk)
            observed, linked = os.fstat(stream.fileno()), path.lstat()
            fields = ("st_dev", "st_ino", "st_size", "st_mtime_ns", "st_ctime_ns", "st_mode", "st_uid", "st_gid", "st_nlink")
            if (hasher.hexdigest() != digest or
                    any(getattr(initial, f) != getattr(observed, f) or
                        getattr(initial, f) != getattr(linked, f) for f in fields)):
                raise ProductionError("Backup bytes or identity changed.")
        check()
        yield
        check()


class HostSession:
    def __init__(self):
        self.process = None
        self.deadline = time.monotonic() + 300

    def __enter__(self):
        try:
            execution = credential("execution-password")
            verifier = credential("verifier-password")
            if execution == verifier:
                raise ProductionError("Execution and verification credentials must be independent.")
            self.process = subprocess.Popen(
                [str(Path("/usr/bin/dotnet").resolve(strict=True)), "exec", str(HOST / "Project1.MigrationHost.dll")],
                stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                cwd=HOST, env={"PATH": "/usr/bin:/bin", "HOME": "/root", "LANG": "C.UTF-8",
                               "DOTNET_CLI_TELEMETRY_OPTOUT": "1"})
            self._request({"execution_password": execution, "verifier_password": verifier})
            return self
        except BaseException:
            self._terminate()
            raise

    def _request(self, request):
        self.process.stdin.write(canonical(request) + b"\n")
        self.process.stdin.flush()
        body = bytearray()
        # Unbuffered OS reads avoid readline hanging forever after a partial response.
        while len(body) <= 200000:
            remaining = self.deadline - time.monotonic()
            if remaining <= 0 or not select.select([self.process.stdout], [], [], remaining)[0]:
                raise ProductionError("Private SQL host timed out.")
            chunk = os.read(self.process.stdout.fileno(), 4096)
            if not chunk:
                raise ProductionError("Private SQL host closed unexpectedly.")
            body.extend(chunk)
            if body.endswith(b"\n"):
                value = strict_json(bytes(body))
                if not isinstance(value, dict) or value.get("ok") is not True:
                    raise ProductionError("SQL operation refused or failed.")
                return value.get("value")
        raise ProductionError("Private SQL host response exceeded its limit.")

    def status(self):
        return canonical(self._request({"operation": "status"}))

    def assert_before(self):
        self._request({"operation": "before"})

    def assert_after(self):
        self._request({"operation": "after"})

    def apply(self, sql, sql_sha256):
        self._request({"operation": "apply", "sql": base64.b64encode(sql).decode("ascii"), "sha256": sql_sha256})

    def backup(self, digest):
        with verified_backup_file(digest):
            return self._request({"operation": "backup", "sha256": digest})

    def _terminate(self):
        if self.process is not None:
            if self.process.poll() is None:
                self.process.kill()
            self.process.wait(timeout=15)
            for stream in (self.process.stdin, self.process.stdout):
                try:
                    stream.close()
                except OSError:
                    pass  # Closed pipes must not mask the original SQL failure.

    def __exit__(self, kind, value, trace):
        try:
            try:
                if self.process.poll() is not None:
                    raise ProductionError("SQL host exited without confirmed cleanup.")
                self._request({"operation": "close"})
                if self.process.wait(timeout=15) != 0:
                    raise ProductionError("SQL host cleanup was not confirmed.")
            except Exception:
                if kind is None:
                    raise ProductionError("SQL host cleanup was not confirmed.") from None
                # Preserve original failure; closing never authorizes retry.
        finally:
            self._terminate()


class ProductionServices:
    target = TARGET

    def __init__(self, package, client):
        self.value, self.client, self.active = package, client, None

    def now(self):
        return datetime.now(timezone.utc)

    def package(self):
        return self.value

    def online_check(self, manifest):
        online_check(self.client, manifest, self.now())

    @contextmanager
    def session(self):
        with HostSession() as session:
            self.active = session
            try:
                yield session
            finally:
                self.active = None

    def backup_evidence(self, approval):
        if self.active is None:
            raise ProductionError("Backup verification requires the held database lock.")
        return self.active.backup(approval["backup"]["sha256"])


def approved_package(record, client, directory):
    verify(client, record["run_id"], record["commit"], directory,
           record["run_attempt"], record["artifact_sha256"])
    package = VerifiedSql.from_directory(directory)
    if package.manifest["sql_sha256"] != record["sql_sha256"]:
        raise ProductionError("SQL differs from the administrator approval.")
    return package


def operate(action: str, identifier: str | None = None):
    """Only fixed paths; admin registration has a separate, non-sudoers wrapper."""
    require_installation(enabled=action not in {"initialize", "inspect", "describe"})
    store = ApprovalLedger(LEDGER, target=TARGET)
    if action == "initialize":
        store.initialize()
        return {"schema": 1, "initialized": True, "sql_executed": False}
    state = store.inspect()
    if action == "inspect":
        return {"schema": 1, "entries": {key: value["phase"] for key, value in state["entries"].items()},
                "sql_executed": False}
    hex_value(identifier, 32)
    if any(entry["phase"] in BLOCKING for entry in state["entries"].values()):
        raise ProductionError("Unresolved execution requires administrator investigation.")
    if action == "describe":
        entry = state["entries"].get(identifier)
        if entry is None or entry["phase"] != "approved":
            raise ProductionError("Registered unconsumed approval is required.")
        record = entry["approval"]
        return {"schema": 1, "approval_id": identifier, "commit": record["commit"],
                "run_id": record["run_id"], "run_attempt": record["run_attempt"],
                "artifact_sha256": record["artifact_sha256"], "sql_sha256": record["sql_sha256"],
                "sql_executed": False}
    if action == "register":
        if identifier in state["entries"]:
            raise ProductionError("Approval replacement is refused.")
        trusted(CONFIG / "approvals", 0o700, directory=True)
        body = private_read(CONFIG / "approvals" / (identifier + ".json"), 180000)
        record = strict_json(body)
        if record.get("approval_id") != identifier:
            raise ProductionError("Candidate approval identifier differs.")
    elif action == "execute":
        entry = state["entries"].get(identifier)
        if entry is None or entry["phase"] != "approved":
            raise ProductionError("Registered unconsumed approval is required.")
        record = entry["approval"]
    else:
        raise ProductionError("Unsupported operation.")
    if record.get("pending") != [NOTE_MIGRATION]:
        raise ProductionError("Only the reviewed Product Note upgrade is supported.")
    token = private_read(CONFIG / "github-token", 4096).decode("ascii").rstrip("\n")
    client = GitHubClient(token)
    with tempfile.TemporaryDirectory(prefix="project1-migration-", dir="/root") as temporary:
        package = approved_package(record, client, Path(temporary) / "package")
        services = ProductionServices(package, client)
        if action == "register":
            services.online_check(package.manifest)
            with services.session() as session:
                session.assert_before()
                check_backup(services.backup_evidence(record), record, TARGET, services.now())
                status = session.status()
                validate_approval(body, package.manifest, status, now=services.now(), target=TARGET)
                store.register(body, package.manifest, status, now=services.now())
            return {"schema": 1, "approval_id": identifier, "registered": True, "sql_executed": False}
        result = execute(identifier, store, services)
        return {**result, "production_enabled": True, "run_id": package.manifest["run_id"],
                "run_attempt": package.manifest["run_attempt"],
                "artifact_sha256": package.manifest["artifact"]["sha256"],
                "sql_sha256": package.manifest["sql_sha256"]}
