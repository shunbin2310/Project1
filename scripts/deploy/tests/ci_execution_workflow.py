"""TEST HOST ONLY, launched by the opted-in C# SQL integration fixture.

No production target, credential, path, SSH or deployment arguments. A private
loopback test channel connects the Python coordinator to the actual C# session.
Sandbox UID/ancestor adaptation exists ONLY here, never in the runtime library.
CI provenance is a fabricated package fixture around the generated SQL, not an
uploaded artifact or real administrator approval. The production online adapter
and approval registration entrypoint must be implemented separately.
"""

import base64
from contextlib import contextmanager, ExitStack
from datetime import datetime, timedelta, timezone
import hashlib
import json
import os
from pathlib import Path
import socket
import stat
import sys
import tempfile
from unittest import mock
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import migration_approval_ledger as storage
from migration_execution import ExecutionError, VerifiedSql, execute
from migration_target import ci_target
from verify_ci_artifacts import REPOSITORY, REPOSITORY_ID
from verify_migration_artifact import PREFIX


class Services:
    def __init__(self, settings, directory, stream):
        self.settings, self.directory, self.stream = settings, directory, stream
        self.target = ci_target(settings["database"], settings["login"], os.environ.get)

    def rpc(self, operation, **fields):
        self.stream.write((json.dumps({"token": self.settings["token"], "operation": operation,
                                      **fields}) + "\n").encode())
        self.stream.flush()
        raw = self.stream.readline(180000)
        if not raw.endswith(b"\n"):
            raise RuntimeError("Invalid CI response framing")
        response = json.loads(raw)
        if response.get("ok") is not True:
            raise RuntimeError("CI SQL operation rejected")
        return response["value"]

    def now(self):
        return datetime.now(timezone.utc)

    def package(self):
        return VerifiedSql.from_directory(self.directory)

    def online_check(self, manifest):
        # Simulated provenance boundary. The fixture independently checks CI context
        # and SQL digest; this does NOT claim to check an uploaded GitHub artifact.
        self.rpc("provenance", manifest=manifest)

    @contextmanager
    def session(self):
        self.rpc("lock")
        yield self

    def status(self):
        return json.dumps(self.rpc("status")).encode()

    def assert_before(self):
        self.rpc("before")

    def assert_after(self):
        self.rpc("after")

    def apply(self, sql, sql_sha256):
        self.rpc("apply", sql=base64.b64encode(sql).decode("ascii"), sha256=sql_sha256)

    def backup_evidence(self, approval):
        # Actual hash/header/VERIFYONLY checks run in the independent admin fixture,
        # never by giving backup/server rights to the migration account.
        return self.rpc("backup")


def package_fixture(root, settings):
    # The SQL bytes are copied from artifacts/migrations by the C# fixture. Only
    # the deliberate failure case changes bytes, and gets its own bound approval.
    sql = base64.b64decode(settings["sql"], validate=True)
    commit, run, attempt = settings["commit"], settings["run_id"], settings["run_attempt"]
    files = {"migrations.sql": sql,
             "migration-manifest.json": json.dumps({"schema": 1, "migrations": settings["migrations"]}).encode(),
             "build-info.txt": ("\n".join([
                 f"Repository: {REPOSITORY}", f"Commit SHA: {commit}", f"Checked-out commit: {commit}",
                 f"CI run ID: {run}", f"CI run attempt: {attempt}", "Event: push", "Ref: refs/heads/main",
                 f"Run URL: https://github.com/{REPOSITORY}/actions/runs/{run}",
                 "Migration range: 0 to latest migration in this commit; idempotent SQL Server script.",
                 "REVIEW ONLY: CI tests this SQL only in disposable databases. CD never executes it.",
                 "Review, test against the target schema and back up before manual production use.", ""])).encode()}
    files["SHA256SUMS"] = "".join(hashlib.sha256(files[name]).hexdigest() + "  " + name + "\n"
                                 for name in ("migrations.sql", "build-info.txt", "migration-manifest.json")).encode()
    directory = root / "package"
    directory.mkdir(mode=0o700)
    archive = directory / "migrations.zip"
    with zipfile.ZipFile(archive, "x", compression=zipfile.ZIP_DEFLATED) as output:
        for name, body in files.items():
            output.writestr(name, body)
    manifest = {"schema": 1, "repository": REPOSITORY, "repository_id": REPOSITORY_ID,
                "run_id": run, "run_attempt": attempt, "commit": commit,
                "sql_sha256": hashlib.sha256(sql).hexdigest(), "migrations": settings["migrations"],
                "artifact": {"id": 1, "name": PREFIX + commit, "sha256": hashlib.sha256(archive.read_bytes()).hexdigest()}}
    (directory / "verified-migration.json").write_text(json.dumps(manifest), encoding="utf-8")
    return directory


def main():
    if sys.platform != "linux" or os.environ.get("GITHUB_ACTIONS") != "true":
        raise RuntimeError("CI Linux test host only")
    settings = json.loads(sys.stdin.readline(16 * 1024 * 1024))
    target = ci_target(settings["database"], settings["login"], os.environ.get)
    with tempfile.TemporaryDirectory(prefix="project1-execution-ci-") as temporary, ExitStack() as stack:
        root = Path(temporary)
        directory = package_fixture(root, settings)
        ledger_root = root / "ledger"
        ledger_root.mkdir(mode=0o700)
        uid = os.getuid()
        def owner(actual):
            if actual != uid:
                raise storage.LedgerError("Invalid CI sandbox owner")
        def ancestor(path):
            if path != ledger_root:
                raise storage.LedgerError("Invalid CI sandbox path")
            info = path.lstat()
            owner(info.st_uid)
            if not stat.S_ISDIR(info.st_mode) or stat.S_IMODE(info.st_mode) != 0o700:
                raise storage.LedgerError("Invalid CI sandbox directory")
        stack.enter_context(mock.patch.object(os, "geteuid", return_value=0))
        stack.enter_context(mock.patch.object(storage, "_require_owner", side_effect=owner))
        stack.enter_context(mock.patch.object(storage, "_trusted_directory", side_effect=ancestor))
        store = storage.ApprovalLedger(ledger_root, target=target)
        store.initialize()
        channel = stack.enter_context(socket.create_connection(("127.0.0.1", settings["port"]), timeout=90))
        stream = stack.enter_context(channel.makefile("rwb"))
        services = Services(settings, directory, stream)
        manifest = services.package().manifest
        now = services.now()
        snapshot = services.status()
        record = {"schema": 1, "purpose": "migration-execution-review", "approval_id": "a" * 32,
                  "repository": REPOSITORY, "server": target.server, "database": target.database,
                  "run_id": manifest["run_id"], "run_attempt": manifest["run_attempt"], "commit": manifest["commit"],
                  "artifact_sha256": manifest["artifact"]["sha256"], "sql_sha256": manifest["sql_sha256"],
                  "applied": json.loads(snapshot)["migrations"], "pending": ["20261009164508_AddProductExampleAttr"],
                  "approved_at": now.isoformat(), "expires_at": (now + timedelta(minutes=10)).isoformat(),
                  "backup": {"sha256": settings["backup_sha256"], "completed_at": settings["backup_completed_at"],
                             "verified": True},
                  "confirmations": {"sql_reviewed": True, "target_schema_reviewed": True, "restore_plan_ready": True}}
        store.register(json.dumps(record), manifest, snapshot, now=services.now())
        succeeded = False
        try:
            result = execute(record["approval_id"], store, services)
            succeeded = result["phase"] == "succeeded"
        except ExecutionError:
            pass
        phase = store.inspect()["entries"][record["approval_id"]]["phase"]
        # Reopen persisted state: no same-ID replay after success or failure.
        reopened = storage.ApprovalLedger(ledger_root, target=target)
        replay_blocked = False
        try:
            execute(record["approval_id"], reopened, services)
        except ExecutionError:
            replay_blocked = True
        services.rpc("done")
        print(json.dumps({"succeeded": succeeded, "phase": phase, "replay_blocked": replay_blocked}))
        expected = "failed" if settings["failure"] else "succeeded"
        if phase != expected or not replay_blocked or succeeded != (expected == "succeeded"):
            raise RuntimeError("Unexpected CI execution workflow result")


if __name__ == "__main__":
    try:
        main()
    except Exception:
        print("CI execution workflow failed (redacted).", file=sys.stderr)
        sys.exit(1)
