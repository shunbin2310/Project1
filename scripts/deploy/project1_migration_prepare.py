"""Root-managed fixed-target SQL staging and backup. Uploaded SQL is NEVER run."""

from __future__ import annotations

from dataclasses import dataclass
from contextlib import contextmanager
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import zipfile

from migration_package import LIMITS, MAX_PACKAGE_BYTES, read_package
from project1_deploy import (deployment_lock, open_upload, require_root_directory,
                             require_root_file, write_record)
from project1_migration_status import read_status, trusted_path
from verify_ci_artifacts import checked_sha, positive_id

STATE = Path("/var/lib/project1-migration-prepare")
SHARED_STATE = Path("/var/lib/project1-deploy")
UPLOADS = Path("/home/project1_deploy/uploads/migration-uploads")
BACKUPS = Path("/var/opt/mssql/backup")
PASSWORD = Path("/etc/project1-backup/password")
SQLCMD = Path("/opt/mssql-tools18/bin/sqlcmd")
MODULES = Path("/usr/local/libexec/project1-migration-prepare")
USAGE = "Usage: project1-migration-prepare COMMIT RUN_ID ATTEMPT TOKEN ZIP_SHA256"
SAFE_ENV = {"PATH": "/usr/bin:/bin", "HOME": "/root", "LANG": "C", "SYSTEMD_PAGER": ""}


class PreparationError(Exception):
    pass


@dataclass(frozen=True)
class Request:
    commit: str
    run_id: int
    attempt: int
    token: str
    digest: str

    @classmethod
    def parse(cls, arguments: list[str]) -> Request:
        if len(arguments) != 5:
            raise PreparationError(USAGE)
        commit, run_id, attempt, token, digest = arguments
        if not re.fullmatch(r"[0-9a-f]{32}", token) or not re.fullmatch(r"[0-9a-f]{64}", digest):
            raise PreparationError("Invalid bounded token or ZIP digest.")
        return cls(checked_sha(commit), positive_id(run_id), positive_id(attempt), token, digest)


def require_api_stopped() -> None:
    result = subprocess.run(
        ["/usr/bin/systemctl", "show", "project1-api.service", "--no-pager",
         "--property=LoadState,ActiveState,SubState,MainPID,FragmentPath"],
        env=SAFE_ENV, stdin=subprocess.DEVNULL, capture_output=True, text=True,
        timeout=15, check=False)
    expected = {"LoadState": "loaded", "ActiveState": "inactive", "SubState": "dead",
                "MainPID": "0", "FragmentPath": "/etc/systemd/system/project1-api.service"}
    if result.returncode or result.stderr.strip():
        raise PreparationError("Cannot confirm the fixed API service state.")
    lines = result.stdout.splitlines()
    if len(lines) != len(expected) or dict(line.split("=", 1) for line in lines) != expected:
        raise PreparationError("Manually stop the fixed API service before preparation.")


def pending_history(content: dict) -> tuple[list[str], list[str]]:
    status = read_status()  # Existing account remains read-only and unchanged.
    if (status["server"] != "homelab-server" or status["database"] != "Project1Db"
            or status["login"] != "project1_migrate"):
        raise PreparationError("Unexpected fixed history identity.")
    applied = status["migrations"]
    if applied != content["migrations"][:len(applied)]:
        raise PreparationError("History is not a prefix of the selected package.")
    pending = content["migrations"][len(applied):]
    if not pending:
        raise PreparationError("No pending migrations; preparation is unnecessary.")
    return applied, pending


def snapshot(request: Request, operation: Path, upload_uid: int) -> dict:
    destination = operation / "migrations.zip"
    hasher = hashlib.sha256()
    with open_upload(UPLOADS / request.token / "migrations.zip", upload_uid) as source:
        if os.fstat(source.fileno()).st_size > MAX_PACKAGE_BYTES:
            raise PreparationError("Migration upload exceeds its size limit.")
        body = source.read(MAX_PACKAGE_BYTES + 1)
    if not 0 < len(body) <= MAX_PACKAGE_BYTES:
        raise PreparationError("Migration upload changed or exceeded its size limit.")
    hasher.update(body)
    if hasher.hexdigest() != request.digest:
        raise PreparationError("Uploaded ZIP digest mismatch.")
    with destination.open("xb") as output:
        output.write(body)
        output.flush()
        os.fsync(output.fileno())
    # Validate the private snapshot, not an upload that can be changed later.
    content = read_package(destination, request.run_id, request.commit, request.attempt)
    with zipfile.ZipFile(destination) as archive:
        for name in LIMITS:
            with (operation / name).open("xb") as output:
                output.write(archive.read(name))
                output.flush()
                os.fsync(output.fileno())
    return content


def require_backup_ancestry(path: Path, sql_uid: int, sql_gid: int) -> None:
    for current in reversed((path, *path.parents)):
        item = current.lstat()
        if (not stat.S_ISDIR(item.st_mode) or item.st_uid not in (0, sql_uid)
                or item.st_mode & 0o002
                or (item.st_mode & 0o020 and item.st_gid not in (0, sql_gid))):
            raise PreparationError("Backup ancestry must be root/mssql managed, without links.")


@contextmanager
def backup_directory(path: Path, sql_uid: int, sql_gid: int):
    """Pin each backup path component; a renamed/symlinked ancestor cannot redirect root I/O."""
    descriptor = os.open("/", os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    try:
        for component in path.parts[1:]:
            next_descriptor = os.open(component, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW, dir_fd=descriptor)
            os.close(descriptor)
            descriptor = next_descriptor
            item = os.fstat(descriptor)
            if (item.st_uid not in (0, sql_uid) or item.st_mode & 0o002
                    or (item.st_mode & 0o020 and item.st_gid not in (0, sql_gid))):
                raise PreparationError("Unsafe backup directory permissions.")
        yield descriptor
    finally:
        os.close(descriptor)


def create_backup_directory(token: str, sql_uid: int, sql_gid: int) -> Path:
    name = "project1-prepare-" + token
    with backup_directory(BACKUPS, sql_uid, sql_gid) as parent:
        # The final backup parent must be root-owned/non-writable by mssql, as on this server.
        item = os.fstat(parent)
        if item.st_uid != 0 or item.st_mode & 0o022:
            raise PreparationError("Backup parent must be root-owned and not group/other writable.")
        os.mkdir(name, mode=0o700, dir_fd=parent)
        child = os.open(name, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW, dir_fd=parent)
        try:
            os.fchown(child, sql_uid, sql_gid)
            os.fsync(parent)
        finally:
            os.close(child)
    return BACKUPS / name


def backup_query(token: str) -> str:
    if not re.fullmatch(r"[0-9a-f]{32}", token):
        raise PreparationError("Invalid backup token.")
    # The only SQL this helper can invoke is the installed, signed fixed backup procedure.
    # NO uploaded SQL, dynamic SQL text, sqlcmd -i, or arbitrary database/path parameters.
    return f"SET NOCOUNT ON; EXEC master.dbo.project1_prepare_backup @Token = N'{token}';"


def run_backup(token: str) -> None:
    trusted_path(PASSWORD.parent, exact_mode=0o700, directory=True)
    trusted_path(PASSWORD, exact_mode=0o600)
    trusted_path(SQLCMD)
    descriptor = os.open(PASSWORD, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(descriptor, "r", encoding="ascii") as stream:
        item = os.fstat(stream.fileno())
        if (not stat.S_ISREG(item.st_mode) or item.st_uid != 0 or item.st_nlink != 1
                or stat.S_IMODE(item.st_mode) != 0o600):
            raise PreparationError("Unsafe backup credential file.")
        password = stream.read(256)
    if not re.fullmatch(r"P1![0-9a-f]{64}\n", password):
        raise PreparationError("Unexpected backup credential format.")
    environment = {**SAFE_ENV, "SQLCMDPASSWORD": password.rstrip("\n")}
    try:
        result = subprocess.run(
            [str(SQLCMD), "-S", "tcp:127.0.0.1,1433", "-U", "project1_backup",
             "-d", "master", "-C", "-b", "-I", "-x", "-l", "15", "-t", "900",
             "-h", "-1", "-W", "-Q", backup_query(token)],
            stdin=subprocess.DEVNULL, env=environment, capture_output=True,
            text=True, encoding="utf-8", timeout=1000, check=False)
    finally:
        environment.pop("SQLCMDPASSWORD", None)
    lines = result.stdout.splitlines()
    if (result.returncode or result.stderr.strip() or len(result.stdout) > 20000
            or not lines or lines[-1].strip() != "PROJECT1_PREPARE_BACKUP_V1"):
        raise PreparationError("Fixed backup/checksum verification failed; inspect server state.")


def backup_digest(path: Path, sql_uid: int, sql_gid: int) -> tuple[str, int]:
    with backup_directory(path.parent, sql_uid, sql_gid) as parent:
        descriptor = os.open(path.name, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK, dir_fd=parent)
    with os.fdopen(descriptor, "rb") as stream:
        item = os.fstat(stream.fileno())
        if (not stat.S_ISREG(item.st_mode) or item.st_nlink != 1
                or item.st_uid != sql_uid or item.st_size <= 0):
            raise PreparationError("Backup must be a nonempty mssql-owned single-link file.")
        os.fchmod(stream.fileno(), 0o600)
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
        after = os.fstat(stream.fileno())
        if (after.st_size, after.st_mtime_ns) != (item.st_size, item.st_mtime_ns):
            raise PreparationError("Backup changed while hashing.")
    return digest, item.st_size


def refuse_uncertain_backups() -> None:
    for operation in STATE.iterdir():
        require_root_directory(operation)
        record = operation / "status.json"
        require_root_file(record)
        if record.stat().st_size > 180000:
            raise PreparationError("Oversized preparation record.")
        value = json.loads(record.read_text(encoding="utf-8"))
        if value.get("phase") in ("backing-up", "backup-uncertain"):
            raise PreparationError("An earlier backup is uncertain; administrator review is required.")


def prepare(request: Request, upload_uid: int, sql_uid: int, sql_gid: int) -> dict:
    require_root_directory(STATE)
    require_root_directory(SHARED_STATE)
    require_api_stopped()
    with deployment_lock(SHARED_STATE):  # Same lock as the existing app deployment helper.
        if os.path.lexists(SHARED_STATE / "pending.json"):
            raise PreparationError("An earlier app deployment is unresolved; administrator review is required.")
        require_api_stopped()
        refuse_uncertain_backups()
        operation = STATE / request.token
        operation.mkdir(mode=0o700)  # Never overwrite or replay a request.
        phase = "validating"
        def save(value: str, **extra) -> None:
            write_record(operation / "status.json", {"phase": value, "commit": request.commit,
                         "run_id": request.run_id, "run_attempt": request.attempt,
                         "token": request.token, **extra})
        save(phase)
        try:
            content = snapshot(request, operation, upload_uid)
            applied, pending = pending_history(content)
            require_api_stopped()
            require_backup_ancestry(BACKUPS, sql_uid, sql_gid)
            backup_dir = create_backup_directory(request.token, sql_uid, sql_gid)
            backup_file = backup_dir / "Project1Db.bak"
            phase = "backing-up"
            save(phase)
            run_backup(request.token)
            digest, size = backup_digest(backup_file, sql_uid, sql_gid)
            require_api_stopped()
            after, after_pending = pending_history(content)
            if after != applied or after_pending != pending:
                raise PreparationError("Migration history changed during preparation.")
            report = {"schema": 1, "commit": request.commit, "run_id": request.run_id,
                      "run_attempt": request.attempt, "token": request.token,
                      "server": "homelab-server", "database": "Project1Db",
                      "sql_sha256": content["sql_sha256"], "sql_path": str(operation / "migrations.sql"),
                      "backup_path": str(backup_file), "backup_sha256": digest, "backup_size": size,
                      "prepared_at": datetime.now(timezone.utc).isoformat(), "pending": pending,
                      "sql_executed": False, "api_stopped": True, "backup_verified": True}
            save("prepared", report=report)
            return report
        except BaseException:
            save("backup-uncertain" if phase == "backing-up" else "failed")
            # Keep all records, snapshots and any backup, including after interrupts.
            raise


def main(arguments=None) -> int:
    arguments = sys.argv[1:] if arguments is None else arguments
    try:
        if sys.platform != "linux" or os.geteuid() != 0:
            raise PreparationError("This fixed Ubuntu tool requires root.")
        for name in ("project1_migration_prepare.py", "project1_deploy.py", "project1_migration_status.py",
                     "migration_package.py", "verify_ci_artifacts.py"):
            require_root_file(MODULES / name)
        if arguments == ["--help"]:
            print(USAGE)
            return 0
        if arguments == ["--check-stopped"]:
            require_api_stopped()
            print("API_STOPPED")
            return 0
        request = Request.parse(arguments)
        import pwd
        upload = pwd.getpwnam("project1_deploy")
        sql = pwd.getpwnam("mssql")
        os.umask(0o077)
        print(json.dumps(prepare(request, upload.pw_uid, sql.pw_uid, sql.pw_gid), separators=(",", ":")))
        return 0
    except Exception as error:
        print(f"ERROR: Preparation failed ({type(error).__name__}); inspect retained records. No migration or service command was run.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
