"""ONE-TIME administrator installation, never run by any GitHub workflow.

Run only from a reviewed root-owned private staging directory on homelab-server.
It creates a dedicated EXECUTE-only backup login and a certificate-signed procedure,
installs fixed tools and a separate sudoers entry. No business migration or backup runs.
"""

from __future__ import annotations

import getpass
import os
from pathlib import Path
import secrets
import subprocess
import sys
import tempfile

from project1_deploy import require_root_directory, require_root_file
from project1_migration_prepare import MODULES, STATE, SHARED_STATE, SQLCMD, BACKUPS, require_backup_ancestry, backup_directory

MODULE_NAMES = ("project1_migration_prepare.py", "project1_deploy.py", "project1_migration_status.py",
                "migration_package.py", "verify_ci_artifacts.py")
CONFIG = Path("/etc/project1-backup")
WRAPPER = Path("/usr/local/sbin/project1-migration-prepare")
SUDOERS = Path("/etc/sudoers.d/project1-migration-prepare")
ENV = {"PATH": "/usr/bin:/bin", "HOME": "/root", "LANG": "C"}


def command(arguments: list[str], *, environment=None, timeout=60) -> str:
    result = subprocess.run(arguments, stdin=subprocess.DEVNULL, capture_output=True, text=True,
                            timeout=timeout, check=False, env=environment or ENV)
    if result.returncode:
        raise RuntimeError("Installation command failed; inspect server setup privately, do not blindly retry.")
    return result.stdout.strip()


def create_private_file(path: Path, body: bytes, mode: int) -> None:
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, mode)
    with os.fdopen(descriptor, "wb") as output:
        output.write(body)
        output.flush()
        os.fsync(output.fileno())
    os.chmod(path, mode)


def install() -> None:
    if sys.platform != "linux" or os.geteuid() != 0:
        raise RuntimeError("Ubuntu administrator installation only.")
    import pwd
    source = Path(__file__).resolve().parent
    for name in (*MODULE_NAMES, "install_migration_prepare.py", "install_migration_prepare.sql",
                 "project1-migration-prepare", "project1-migration-prepare.sudoers"):
        require_root_file(source / name)
    for directory in (SHARED_STATE, Path("/usr/local/libexec"), WRAPPER.parent, SUDOERS.parent):
        require_root_directory(directory)
    require_root_file(SQLCMD)
    require_root_file(Path("/usr/local/libexec/project1-migration-status.py"))
    pwd.getpwnam("project1_deploy")
    sql_user = pwd.getpwnam("mssql")
    require_backup_ancestry(BACKUPS, sql_user.pw_uid, sql_user.pw_gid)
    with backup_directory(BACKUPS, sql_user.pw_uid, sql_user.pw_gid) as descriptor:
        item = os.fstat(descriptor)
        if item.st_uid != 0 or item.st_mode & 0o022:
            raise RuntimeError("Backup parent must be root-owned and not group/other writable.")
    inbox = Path("/home/project1_deploy/uploads/migration-uploads")
    for path in (MODULES, STATE, CONFIG, WRAPPER, SUDOERS, inbox):
        if os.path.lexists(path):
            raise RuntimeError("An installation path exists; administrator review is required, no overwrites.")
    command(["/usr/sbin/visudo", "-c"])
    command(["/usr/sbin/visudo", "-cf", str(source / "project1-migration-prepare.sudoers")])
    # This mkdir runs WITHOUT root privileges: no root chown/write through deploy-controlled paths.
    command(["/usr/sbin/runuser", "-u", "project1_deploy", "--", "/usr/bin/mkdir", "-m", "0700", "--", str(inbox)])
    administrator_password = getpass.getpass("SQL sa password (never saved): ")
    if not administrator_password:
        raise RuntimeError("Administrator password is required.")
    backup_password, signing_password = "P1!" + secrets.token_hex(32), "P1!" + secrets.token_hex(32)
    template = (source / "install_migration_prepare.sql").read_text(encoding="utf-8")
    if template.count("__BACKUP_PASSWORD__") != 1 or template.count("__SIGNING_PASSWORD__") != 2:
        raise RuntimeError("Unexpected reviewed SQL installation template.")
    sql = template.replace("__BACKUP_PASSWORD__", backup_password).replace("__SIGNING_PASSWORD__", signing_password)
    CONFIG.mkdir(mode=0o700)
    create_private_file(CONFIG / "password", (backup_password + "\n").encode("ascii"), 0o600)
    # Keep the new login credential after a failed/partial install, for administrator recovery.
    with tempfile.TemporaryDirectory(prefix=".install-", dir=CONFIG) as temporary:
        script = Path(temporary) / "setup.sql"
        create_private_file(script, sql.encode("utf-8"), 0o600)
        environment = {**ENV, "SQLCMDPASSWORD": administrator_password}
        try:
            output = command([str(SQLCMD), "-S", "tcp:127.0.0.1,1433", "-U", "sa", "-d", "master",
                              "-C", "-b", "-I", "-x", "-f", "65001", "-l", "15", "-t", "60",
                              "-h", "-1", "-W", "-i", str(script)], environment=environment, timeout=120)
        finally:
            environment.pop("SQLCMDPASSWORD", None)
            administrator_password = ""
        if not output.splitlines() or output.splitlines()[-1].strip() != "PROJECT1_PREPARE_INSTALL_V1":
            raise RuntimeError("SQL installation result could not be confirmed.")
    # Verify that the authenticating login itself has no broad server/database permissions.
    check = """SET NOCOUNT ON;
IF ISNULL(IS_SRVROLEMEMBER('sysadmin'), -1) <> 0 OR ISNULL(HAS_PERMS_BY_NAME(NULL, NULL, 'CREATE ANY DATABASE'), -1) <> 0
   OR ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CONTROL'), -1) <> 0
   OR ISNULL(HAS_PERMS_BY_NAME('dbo.project1_prepare_backup', 'OBJECT', 'EXECUTE'), -1) <> 1
    THROW 51000, 'Unexpected backup login permissions.', 1;
SELECT N'PROJECT1_PREPARE_LOGIN_V1';"""
    environment = {**ENV, "SQLCMDPASSWORD": backup_password}
    try:
        output = command([str(SQLCMD), "-S", "tcp:127.0.0.1,1433", "-U", "project1_backup", "-d", "master",
                          "-C", "-b", "-I", "-x", "-l", "15", "-t", "30", "-h", "-1", "-W", "-Q", check],
                         environment=environment)
    finally:
        environment.pop("SQLCMDPASSWORD", None)
    if output != "PROJECT1_PREPARE_LOGIN_V1":
        raise RuntimeError("Backup login permission check failed.")
    MODULES.mkdir(mode=0o755)
    STATE.mkdir(mode=0o700)
    for name in MODULE_NAMES:
        create_private_file(MODULES / name, (source / name).read_bytes(), 0o644)
    create_private_file(WRAPPER, (source / WRAPPER.name).read_bytes().replace(b"\r\n", b"\n"), 0o755)
    create_private_file(SUDOERS, (source / SUDOERS.name).read_bytes().replace(b"\r\n", b"\n"), 0o440)
    command(["/usr/sbin/visudo", "-c"])
    print("Installed preparation-only tools. No backup, business migration or service operation occurred.")


def main() -> int:
    try:
        if len(sys.argv) != 1:
            raise RuntimeError("Installer accepts no arguments.")
        os.umask(0o077)
        install()
        return 0
    except Exception as error:
        print(f"ERROR: Installation failed ({type(error).__name__}); review partial setup privately. Do not rerun blindly.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
