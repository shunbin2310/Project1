"""Ubuntu root-managed, argument-free migration-history reader. NEVER applies SQL."""

from __future__ import annotations

from datetime import datetime, timezone
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys

PASSWORD = Path("/etc/project1-migration/password")
SQLCMD = Path("/opt/mssql-tools18/bin/sqlcmd")
MARKER = "PROJECT1_MIGRATION_STATUS_V1"
ID_PATTERN = r"[0-9]{14}_[A-Za-z][A-Za-z0-9_]{0,134}"

# No file input, sqlcmd directives, caller parameters, application startup or dynamic SQL.
QUERY = """
SET NOCOUNT ON;
IF CONVERT(nvarchar(128), SERVERPROPERTY('ServerName')) <> N'homelab-server'
    THROW 51000, 'Unexpected server.', 1;
IF DB_NAME() <> N'Project1Db' OR ORIGINAL_LOGIN() <> N'project1_migrate'
    THROW 51000, 'Unexpected database or login.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = N'Project1Db'
    AND state_desc = N'ONLINE' AND is_read_only = 0 AND is_trustworthy_on = 0)
    THROW 51000, 'Unexpected database state.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
    THROW 51000, 'Migration history is missing or inaccessible.', 1;
IF HAS_PERMS_BY_NAME('dbo.__EFMigrationsHistory', 'OBJECT', 'SELECT') <> 1
    THROW 51000, 'History SELECT permission is required.', 1;
IF EXISTS (SELECT 1 FROM sys.fn_my_permissions(NULL, 'SERVER')
    WHERE permission_name <> 'CONNECT SQL' AND permission_name NOT LIKE 'VIEW %')
    OR EXISTS (SELECT 1 FROM sys.fn_my_permissions(NULL, 'DATABASE')
    WHERE permission_name <> 'CONNECT' AND permission_name NOT LIKE 'VIEW %')
    THROW 51000, 'Precheck account has unexpected broad permissions.', 1;
IF EXISTS (
    SELECT 1 FROM sys.objects AS o
    CROSS APPLY sys.fn_my_permissions(QUOTENAME(SCHEMA_NAME(o.schema_id))
        + '.' + QUOTENAME(o.name), 'OBJECT') AS p
    WHERE o.is_ms_shipped = 0 AND o.type IN ('U', 'V', 'P', 'FN', 'IF', 'TF', 'SN', 'SO')
      AND p.permission_name <> 'SELECT' AND p.permission_name NOT LIKE 'VIEW %'
)
    THROW 51000, 'Precheck account has unexpected object permissions.', 1;
SELECT 'PROJECT1_MIGRATION_STATUS_V1';
SELECT TOP (1001) CONVERT(varchar(150), MigrationId)
FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;
"""


class StatusError(Exception):
    pass


def trusted_path(path: Path, *, exact_mode: int | None = None, directory: bool = False) -> None:
    # No symlink components, or directories writable by non-root identities.
    for current in reversed((path, *path.parents)):
        info = current.lstat()
        if (stat.S_ISLNK(info.st_mode) or info.st_uid != 0 or info.st_mode & stat.S_IWOTH
                or (info.st_mode & stat.S_IWGRP and info.st_gid != 0)):
            raise StatusError("A required server path is not root-managed.")
        if current != path and not stat.S_ISDIR(info.st_mode):
            raise StatusError("Invalid server parent directory.")
    if directory:
        valid = stat.S_ISDIR(info.st_mode)
    else:
        valid = stat.S_ISREG(info.st_mode) and info.st_nlink == 1
    if not valid or (exact_mode is not None and stat.S_IMODE(info.st_mode) != exact_mode):
        raise StatusError("A required server file has an unexpected type or permissions.")


def read_password() -> str:
    trusted_path(PASSWORD.parent, exact_mode=0o700, directory=True)
    trusted_path(PASSWORD, exact_mode=0o600)
    fd = os.open(PASSWORD, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(fd, "r", encoding="ascii") as stream:
        info = os.fstat(stream.fileno())
        if (not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1
                or stat.S_IMODE(info.st_mode) != 0o600):
            raise StatusError("Unsafe password file.")
        password = stream.read(256)
    if not re.fullmatch(r"P1![0-9a-f]{64}\n", password):
        raise StatusError("Unexpected password-file format.")
    return password.rstrip("\n")


def parse_output(output: str) -> list[str]:
    if len(output) > 180000:
        raise StatusError("SQL status output exceeded its limit.")
    lines = [line.strip() for line in output.splitlines() if line.strip()]
    if not lines or lines[0] != MARKER:
        raise StatusError("Unexpected SQL status response.")
    ids = lines[1:]
    if (len(ids) > 1000 or ids != sorted(set(ids))
            or any(not re.fullmatch(ID_PATTERN, item) for item in ids)):
        raise StatusError("Invalid SQL migration-history response.")
    return ids


def read_status() -> dict:
    trusted_path(SQLCMD)
    if not os.access(SQLCMD, os.X_OK):
        raise StatusError("The fixed sqlcmd executable is not executable.")
    environment = {"PATH": "/usr/bin:/bin", "HOME": "/root", "LANG": "C",
                   "SQLCMDPASSWORD": read_password()}
    try:
        # No inherited SQLCMDINI, proxy, SQL settings, -P password argv, -i or caller stdin.
        # -X is not used because its environment-variable behavior varies by sqlcmd variant;
        # the minimal environment and fixed -Q query provide no scripting/startup input.
        result = subprocess.run(
            [str(SQLCMD), "-S", "tcp:127.0.0.1,1433", "-U", "project1_migrate",
             "-d", "Project1Db", "-C", "-b", "-x", "-l", "10", "-t", "30",
             "-h", "-1", "-W", "-w", "256", "-Q", QUERY],
            stdin=subprocess.DEVNULL, capture_output=True, text=True, encoding="ascii",
            env=environment, timeout=60, check=False)
    finally:
        environment.pop("SQLCMDPASSWORD", None)
    if result.returncode != 0 or result.stderr.strip():
        # Never print raw SQL errors: they could contain account/server data or credentials.
        raise StatusError("Read-only SQL query failed; administrator review is required.")
    return {"schema": 1, "server": "homelab-server", "database": "Project1Db",
            "login": "project1_migrate", "checked_at": datetime.now(timezone.utc).isoformat(),
            "migrations": parse_output(result.stdout)}


def main(arguments=None) -> int:
    arguments = sys.argv[1:] if arguments is None else arguments
    try:
        if arguments:
            raise StatusError("This read-only tool accepts no arguments.")
        if sys.platform != "linux" or os.geteuid() != 0:
            raise StatusError("This fixed server tool requires Ubuntu root.")
        # Installed implementation must be outside the deployment user's control.
        trusted_path(Path("/usr/local/libexec/project1-migration-status.py"), exact_mode=0o644)
        os.umask(0o077)
        print(json.dumps(read_status(), separators=(",", ":")))
        return 0
    except StatusError as error:
        print(f"ERROR: {error}", file=sys.stderr)
    except Exception as error:
        print(f"ERROR: Read-only migration status failed ({type(error).__name__}).", file=sys.stderr)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
