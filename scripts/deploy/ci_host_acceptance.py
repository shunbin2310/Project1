"""DESTRUCTIVE CI FIXTURE ONLY: installs into a disposable GitHub-hosted VM.

Never run on the homelab. No SSH, Tailscale, production secrets or GitHub API.
The installed production files and fixed .NET host are unmodified. Only the
GitHub client is replaced by an in-memory fixture inside this root test harness.
Registration/execution call the actual installed entrypoint in-process; sudo
wrapper describe/refusals run as real subprocesses. No wrapper SQL submission.
"""
from __future__ import annotations

import copy
from contextlib import redirect_stdout
from datetime import datetime, timedelta, timezone
import hashlib
import io
import json
import os
from pathlib import Path
import re
import secrets
import shutil
import stat
import subprocess
import sys
import zipfile
from unittest import mock

MARKER = "disposable-github-runner-v1"
IMAGE = "mcr.microsoft.com/mssql/server@sha256:2b5b581621126574f3d1f75e78d3eebe8d05aedb59ad0cfdf9aa42cb0634d726"
MODULES = (
    "production_entry.py", "production_migration.py", "migration_execution.py",
    "migration_target.py", "migration_approval.py", "migration_approval_ledger.py",
    "migration_package.py", "precheck_migrations.py", "verify_migration_artifact.py",
    "verify_ci_artifacts.py", "project1_deploy.py", "deploy_verified.py",
)
ROOT = Path("/usr/local/libexec/project1-migration")
HOST = Path("/usr/local/libexec/project1-migration-host")
CONFIG = Path("/etc/project1-migration-execution")
BACKUPS = Path("/var/lib/project1-migration-backups")
LEDGER = Path("/var/lib/project1-migration-ledger")
STAGING = Path("/var/lib/project1-host-acceptance-staging")
SHA, RUN, WORKFLOW = "a" * 40, 42, 17  # Fabricated provenance, never a real approval.
STAGES = {
    "S00": "ci-guard-and-inputs", "S01": "existing-installation-check",
    "S02": "copy-root-runtime", "S03": "validate-system-dotnet-link",
    "S04": "copy-fixed-host", "S05": "copy-ci-provisioner", "S06": "install-python-and-wrappers",
    "S07": "create-archive-group", "S08": "create-deploy-user", "S09": "validate-sudoers-template",
    "S10": "install-and-validate-sudoers", "S11": "prepare-private-test-files",
    "S12": "start-disposable-sql-container", "S13": "check-container-isolation",
    "S14": "check-sql-service-uid", "S15": "check-sql-service-groups",
    "S16": "provision-disposable-sql-baseline", "S17": "archive-checksum-backup",
    "S18": "validate-fixed-installation", "S19": "initialize-root-ledger",
    "S20": "fixed-stdio-lock-and-backup", "S21": "cross-batch-rollback",
    "S22": "verify-synthetic-artifact", "S23": "check-sudo-execute-authorization",
    "S24": "check-default-off", "S25": "register-fixture-approval", "S26": "sudo-describe-binding",
    "S27": "reject-sudo-arguments-and-environment", "S28": "check-private-file-access",
    "S29": "execute-approved-fixture", "S30": "verify-schema-history-and-product",
    "S31": "reject-consumed-approval", "S99": "remove-owned-container",
}
COMMAND_LABELS = frozenset({
    "groupadd", "useradd", "visudo-template", "visudo-installed", "docker-start",
    "docker-inspect", "docker-uid", "docker-groups", "fixture-initialize", "ledger-initialize",
    "fixture-before", "sudo-execute-authorization", "sudo-default-off", "sudo-describe",
    "sudo-invalid-arguments", "sudo-forbidden-command", "sudo-environment-injection",
    "private-write-denial", "credential-read-denial", "fixture-after", "sudo-consumed-describe",
    "sudo-consumed-execute", "docker-cleanup",
})
_CURRENT_STAGE = "S00"


def set_stage(identifier):
    global _CURRENT_STAGE
    if identifier not in STAGES:
        raise ValueError("Unknown diagnostic stage.")
    _CURRENT_STAGE = identifier
    print(f"BEGIN [{identifier}] {STAGES[identifier]}", flush=True)


class CommandFailure(RuntimeError):
    """Only allowlisted labels/numeric status are reported, never raw child context."""
    def __init__(self, label, reason, *, exit_code=None, errno=None):
        if (label not in COMMAND_LABELS or reason not in {"exit-status", "timeout", "launch-error"}
                or (exit_code is not None and type(exit_code) is not int)
                or (errno is not None and type(errno) is not int)):
            raise ValueError("Invalid diagnostic fields.")
        super().__init__("Acceptance command failed.")
        self.label, self.reason, self.exit_code, self.errno = label, reason, exit_code, errno


def report_failure(error):
    # No repr(error), traceback, argv, stdin, env, stdout or stderr. Even an
    # exception's message/type could be derived from sensitive child inputs.
    detail = "operation-failed"
    if (isinstance(error, CommandFailure) and error.label in COMMAND_LABELS
            and error.reason in {"exit-status", "timeout", "launch-error"}
            and (error.exit_code is None or type(error.exit_code) is int)
            and (error.errno is None or type(error.errno) is int)):
        detail = f"command={error.label} reason={error.reason}"
        if error.exit_code is not None:
            detail += f" exit_code={error.exit_code}"
        if error.errno is not None:
            detail += f" errno={error.errno}"
    elif isinstance(error, OSError):
        detail = "filesystem-error"
        if type(error.errno) is int:
            detail += f" errno={error.errno}"
    print(f"ERROR [{_CURRENT_STAGE}] {STAGES[_CURRENT_STAGE]}: {detail}; no production access.", file=sys.stderr)


def cleanup_container(identifier, *, already_failing):
    global _CURRENT_STAGE
    # Delete ONLY the exact container created by this invocation; no host path cleanup.
    if not identifier or not re.fullmatch(r"[0-9a-f]{64}", identifier):
        return
    original_stage = _CURRENT_STAGE
    try:
        set_stage("S99")
        run(["/usr/bin/docker", "rm", "--force", identifier], label="docker-cleanup")
    except Exception as cleanup_error:
        if not already_failing:
            raise
        report_failure(cleanup_error)
    finally:
        if already_failing:
            _CURRENT_STAGE = original_stage


def require_ci(environment, platform, uid):
    if (platform != "linux" or uid != 0 or environment.get("GITHUB_ACTIONS") != "true"
            or environment.get("PROJECT1_HOST_ACCEPTANCE") != MARKER
            or environment.get("GITHUB_REPOSITORY") != "shunbin2310/Project1"
            or environment.get("GITHUB_JOB") != "migration-host-acceptance"
            or environment.get("RUNNER_ENVIRONMENT") != "github-hosted"
            or not re.fullmatch(r"[1-9][0-9]*", environment.get("GITHUB_RUN_ID", ""))):
        raise RuntimeError("Only the explicit disposable GitHub-hosted job is allowed.")


def run(arguments, *, label, body=None, succeeds=True, environment=None, timeout=180):
    if label not in COMMAND_LABELS:
        raise ValueError("Unknown diagnostic command label.")
    try:
        result = subprocess.run(arguments, input=body, capture_output=True, timeout=timeout,
                                env=environment)
    except subprocess.TimeoutExpired:
        raise CommandFailure(label, "timeout") from None
    except OSError as error:
        raise CommandFailure(label, "launch-error", errno=error.errno) from None
    if (result.returncode == 0) != succeeds:
        # Never echo arbitrary child output (possibly credential-bearing SQL errors).
        raise CommandFailure(label, "exit-status", exit_code=result.returncode)
    return result.stdout


def write_private(path, body):
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    with os.fdopen(descriptor, "wb") as stream:
        stream.write(body)


def copy_tree(source, target):
    shutil.copytree(source, target)
    for path in (target, *target.rglob("*")):
        if path.is_symlink():
            raise RuntimeError("Published output must not contain symlinks.")
        os.chown(path, 0, 0)
        path.chmod(0o755 if path.is_dir() or path.name == "dotnet" else 0o644)


class FixtureClient:
    """In-memory artifact service. Makes NO network requests."""
    def __init__(self, files):
        from verify_ci_artifacts import REPOSITORY, REPOSITORY_ID, WORKFLOW_PATH
        from verify_migration_artifact import PREFIX
        buffer = io.BytesIO()
        with zipfile.ZipFile(buffer, "w", zipfile.ZIP_DEFLATED) as archive:
            for name, body in files.items():
                archive.writestr(name, body)
        self.payload = buffer.getvalue()
        repository = {"id": REPOSITORY_ID, "full_name": REPOSITORY}
        self.run = {"id": RUN, "workflow_id": WORKFLOW, "path": WORKFLOW_PATH,
                    "repository": repository, "head_repository": repository, "head_sha": SHA,
                    "head_branch": "main", "event": "push", "status": "completed",
                    "conclusion": "success", "run_attempt": 1}
        self.item = {"id": 3, "name": PREFIX + SHA, "size_in_bytes": len(self.payload),
                     "digest": "sha256:" + hashlib.sha256(self.payload).hexdigest(),
                     "expired": False, "expires_at": (datetime.now(timezone.utc) + timedelta(days=1)).isoformat(),
                     "workflow_run": {"id": RUN, "repository_id": REPOSITORY_ID,
                                      "head_repository_id": REPOSITORY_ID, "head_branch": "main", "head_sha": SHA}}

    def get(self, path):
        from verify_ci_artifacts import WORKFLOW_PATH
        if path.endswith("/workflows/ci.yml"):
            return {"id": WORKFLOW, "path": WORKFLOW_PATH, "name": "Project1 CI"}
        if path.endswith("/runs/42"):
            return copy.deepcopy(self.run)
        raise RuntimeError("Unknown fixture API path.")

    def artifacts(self, run_id):
        if run_id != RUN:
            raise RuntimeError("Unknown fixture run.")
        return [copy.deepcopy(self.item)]

    def download(self, identifier, destination, digest):
        from verify_ci_artifacts import copy_archive
        if identifier != 3:
            raise RuntimeError("Unknown fixture artifact.")
        copy_archive(io.BytesIO(self.payload), destination, digest)


def package_files(fixture):
    from verify_ci_artifacts import REPOSITORY
    info = "\n".join([
        f"Repository: {REPOSITORY}", f"Commit SHA: {SHA}", f"Checked-out commit: {SHA}",
        f"CI run ID: {RUN}", "CI run attempt: 1", "Event: push", "Ref: refs/heads/main",
        f"Run URL: https://github.com/{REPOSITORY}/actions/runs/{RUN}",
        "Migration range: 0 to latest migration in this commit; idempotent SQL Server script.",
        "REVIEW REQUIRED: production execution needs a separately registered administrator approval.",
        "Review, test against the target schema and back up before manual production use.", ""])
    files = {"migrations.sql": fixture["sql"].encode(), "build-info.txt": info.encode(),
             "migration-manifest.json": json.dumps({"schema": 1, "migrations": fixture["migrations"]}).encode()}
    files["SHA256SUMS"] = "".join(hashlib.sha256(body).hexdigest() + "  " + name + "\n"
                                for name, body in files.items()).encode()
    return files


def acceptance(scripts, published_host, published_fixture, dotnet):
    # Guard BEFORE any filesystem, Docker or SQL operation. No local opt-out.
    require_ci(os.environ, sys.platform, os.geteuid())
    import grp
    import pwd
    set_stage("S01")
    fixed = (ROOT, HOST, CONFIG, BACKUPS, LEDGER, STAGING,
             Path("/usr/local/sbin/project1-migration-execute"),
             Path("/usr/local/sbin/project1-migration-admin"),
             Path("/etc/sudoers.d/project1-migration-execute"))
    if any(os.path.lexists(path) for path in fixed):
        raise RuntimeError("Never overwrite an existing migration installation.")
    for kind, name in ((pwd, "project1_deploy"), (grp, "mssql")):
        try:
            (kind.getpwnam if kind is pwd else kind.getgrnam)(name)
        except KeyError:
            continue
        raise RuntimeError("Refuse existing acceptance account/group.")
    os.umask(0o077)
    print("BEGIN: disposable root installation and real sudoers validation", flush=True)
    # Copy runtime to root-managed storage rather than trusting the runner's writable SDK.
    runtime = Path("/opt/project1-host-acceptance-dotnet")
    set_stage("S02")
    copy_tree(dotnet.resolve(strict=True).parent, runtime)
    set_stage("S03")
    system_dotnet = Path("/usr/bin/dotnet")
    if os.path.lexists(system_dotnet):
        if not system_dotnet.is_symlink() or system_dotnet.resolve() != dotnet.resolve():
            raise RuntimeError("Unexpected existing system runtime.")
        system_dotnet.unlink()
    system_dotnet.symlink_to(runtime / "dotnet")
    set_stage("S04")
    copy_tree(published_host, HOST)
    fixture_root = Path("/usr/local/libexec/project1-host-acceptance-fixture")
    set_stage("S05")
    copy_tree(published_fixture, fixture_root)
    set_stage("S06")
    ROOT.mkdir(mode=0o755, parents=True)
    for name in MODULES:
        shutil.copyfile(scripts / name, ROOT / name)
        (ROOT / name).chmod(0o644)
    for name in ("project1-migration-execute", "project1-migration-admin"):
        destination = Path("/usr/local/sbin") / name
        shutil.copyfile(scripts / name, destination)
        destination.chmod(0o755)
    set_stage("S07")
    run(["/usr/sbin/groupadd", "--gid", "10001", "mssql"], label="groupadd")
    set_stage("S08")
    run(["/usr/sbin/useradd", "--no-create-home", "--shell", "/usr/sbin/nologin", "project1_deploy"], label="useradd")
    candidate = scripts / "project1-migration-execute.sudoers"
    set_stage("S09")
    run(["/usr/sbin/visudo", "-cf", str(candidate)], label="visudo-template")
    set_stage("S10")
    sudoers = Path("/etc/sudoers.d/project1-migration-execute")
    shutil.copyfile(candidate, sudoers)
    sudoers.chmod(0o440)
    run(["/usr/sbin/visudo", "-c"], label="visudo-installed")
    set_stage("S11")
    CONFIG.mkdir(mode=0o700)
    (CONFIG / "approvals").mkdir(mode=0o700)
    BACKUPS.mkdir(mode=0o750)
    os.chown(BACKUPS, 0, 10001)
    STAGING.mkdir(mode=0o700)
    os.chown(STAGING, 10001, 10001)
    execution, verifier = "P1!" + secrets.token_hex(32), "P1!" + secrets.token_hex(32)
    for name, value in (("execution-password", execution), ("verifier-password", verifier)):
        write_private(CONFIG / name, (value + "\n").encode())
    # Own isolated SQL container, loopback-only; SQL cannot write the root backup archive.
    identifier = None
    try:
        set_stage("S12")
        identifier = run(["/usr/bin/docker", "run", "--detach", "--hostname", "homelab-server",
                          "--memory", "3g", "--group-add", "10001", "--publish", "127.0.0.1:1433:1433",
                          "--env", "ACCEPT_EULA=Y", "--env", "MSSQL_PID=Express",
                          "--env", "MSSQL_SA_PASSWORD=Project1CiOnly-Tests!2026",
                          "--env", "MSSQL_MEMORY_LIMIT_MB=2048",
                          "--mount", f"type=bind,src={STAGING},dst=/var/opt/mssql/backup",
                          "--mount", f"type=bind,src={BACKUPS},dst={BACKUPS},readonly",
                          IMAGE], label="docker-start", timeout=300).decode().strip()
        if not re.fullmatch(r"[0-9a-f]{64}", identifier):
            raise RuntimeError("Unexpected disposable container ID.")
        set_stage("S13")
        inspect = json.loads(run(["/usr/bin/docker", "inspect", identifier], label="docker-inspect"))[0]
        if (inspect["Config"]["Image"] != IMAGE or inspect["Config"]["Hostname"] != "homelab-server"
                or inspect["HostConfig"]["PortBindings"] != {"1433/tcp": [{"HostIp": "127.0.0.1", "HostPort": "1433"}]}):
            raise RuntimeError("Unexpected disposable container isolation.")
        set_stage("S14")
        if run(["/usr/bin/docker", "exec", identifier, "id", "-u"], label="docker-uid").strip() != b"10001":
            raise RuntimeError("SQL service must use the non-root fixture UID.")
        set_stage("S15")
        if b"10001" not in run(["/usr/bin/docker", "exec", identifier, "id", "-G"], label="docker-groups").split():
            raise RuntimeError("SQL service cannot read the immutable archive group.")
        child_env = {key: os.environ[key] for key in
                     ("GITHUB_ACTIONS", "GITHUB_REPOSITORY", "PROJECT1_HOST_ACCEPTANCE",
                      "GITHUB_JOB", "RUNNER_ENVIRONMENT")}
        child_env.update(PATH="/usr/bin:/bin", DOTNET_CLI_TELEMETRY_OPTOUT="1", HOME="/root")
        fixture_command = [str(system_dotnet), "exec", str(fixture_root / "Project1.MigrationHost.Acceptance.dll")]
        print("BEGIN: disposable SQL baseline, scoped accounts and checksum backup", flush=True)
        set_stage("S16")
        fixture = json.loads(run(fixture_command + ["initialize"], environment=child_env,
                                 label="fixture-initialize",
                                 body=json.dumps({"execution_password": execution, "verifier_password": verifier}).encode() + b"\n"))
        set_stage("S17")
        source = STAGING / "project1-host-acceptance.bak"
        if not stat.S_ISREG(source.lstat().st_mode) or source.lstat().st_nlink != 1:
            raise RuntimeError("Unexpected fixture backup type.")
        with source.open("rb") as stream:
            digest = hashlib.file_digest(stream, "sha256").hexdigest()
        archived = BACKUPS / (digest + ".bak")
        shutil.copyfile(source, archived)
        os.chown(archived, 0, 10001)
        archived.chmod(0o640)
        sys.dont_write_bytecode = True
        sys.path.insert(0, str(ROOT))
        import production_migration as production
        from migration_approval_ledger import ApprovalLedger, canonical
        import production_entry as entrypoint
        from verify_migration_artifact import verify
        set_stage("S18")
        production.require_installation(enabled=False)
        set_stage("S19")
        run(["/usr/local/sbin/project1-migration-admin", "initialize"], label="ledger-initialize")
        print("BEGIN: actual fixed stdio host, independent verifier and failure cleanup", flush=True)
        # Actual stdio, fixed identity, least-privilege verifier and real checksum verification.
        set_stage("S20")
        with production.HostSession() as session:
            session.assert_before()
            status = session.status()
            if json.loads(status)["migrations"] != fixture["migrations"][:-1]:
                raise RuntimeError("Actual host history differed from the baseline.")
            evidence = session.backup(digest)
            if evidence["completed_at"] != fixture["completed_at"]:
                raise RuntimeError("Backup timestamp was not independently preserved.")
            try:
                with production.HostSession():
                    raise AssertionError("Competing database lock unexpectedly acquired.")
            except production.ProductionError:
                pass
        # A failed later GO batch must roll back earlier uncommitted DDL.
        set_stage("S21")
        failure = b"BEGIN TRANSACTION;\nALTER TABLE dbo.Products ADD CiUncommitted int NULL;\nGO\nTHROW 51055, 'CI failure', 1;\nGO\nCOMMIT;\nGO\n"
        try:
            with production.HostSession() as session:
                session.apply(failure, hashlib.sha256(failure).hexdigest())
            raise AssertionError("Injected SQL failure was accepted.")
        except production.ProductionError:
            pass
        run(fixture_command + ["before"], environment=child_env, label="fixture-before")
        print("PASS: real stdio identity, SQL lock, scoped backup verifier, cross-batch rollback", flush=True)
        set_stage("S22")
        client = FixtureClient(package_files(fixture))
        directory = Path("/root/project1-host-acceptance-package")
        manifest = verify(client, RUN, SHA, directory, 1)
        now = datetime.now(timezone.utc)
        approval_id = "a" + secrets.token_hex(16)[1:]  # Uppercase rejection must never be a numeric-only ID.
        record = {"schema": 1, "purpose": "migration-execution-review", "approval_id": approval_id,
                  "repository": "shunbin2310/Project1", "server": production.TARGET.server,
                  "database": production.TARGET.database, "run_id": RUN, "run_attempt": 1, "commit": SHA,
                  "artifact_sha256": manifest["artifact"]["sha256"], "sql_sha256": manifest["sql_sha256"],
                  "applied": fixture["migrations"][:-1], "pending": fixture["migrations"][-1:],
                  "approved_at": now.isoformat(), "expires_at": (now + timedelta(minutes=30)).isoformat(),
                  "backup": {"sha256": digest, "completed_at": fixture["completed_at"], "verified": True},
                  "confirmations": {"sql_reviewed": True, "target_schema_reviewed": True, "restore_plan_ready": True}}
        ledger = ApprovalLedger(LEDGER, target=production.TARGET)
        sudo = ["/usr/bin/sudo", "-u", "project1_deploy", "--", "/usr/bin/sudo", "-n", "--"]
        wrapper = "/usr/local/sbin/project1-migration-execute"
        set_stage("S23")
        # Check authorization separately: a refusal must come from the disabled
        # production entry, not from a broken sudo execute regex.
        run(["/usr/bin/sudo", "-u", "project1_deploy", "--", "/usr/bin/sudo", "-n", "-l",
             "--", wrapper, approval_id], label="sudo-execute-authorization")
        # Valid sudo ID is allowed, but the actual entry must refuse while disabled.
        set_stage("S24")
        run(sudo + [wrapper, approval_id], succeeds=False, label="sudo-default-off")
        write_private(CONFIG / "enabled", production.ENABLED)
        write_private(CONFIG / "github-token", b"acceptance-fixture-not-a-real-token\n")
        write_private(CONFIG / "approvals" / (approval_id + ".json"), canonical(record))
        def invoke_entry(arguments):
            output = io.StringIO()
            # This is the ONLY mocked production dependency. Keep trust, stdio,
            # credentials, backup hashing/SQL, locks and ledger persistence real.
            with mock.patch.object(production, "GitHubClient", return_value=client), redirect_stdout(output):
                code = entrypoint.main(arguments)
            if code != 0:
                raise RuntimeError("Actual installed entrypoint refused the fixture.")
            return json.loads(output.getvalue())
        set_stage("S25")
        registration = invoke_entry(["admin", "register", approval_id])
        if registration["registered"] is not True or registration["sql_executed"] is not False:
            raise RuntimeError("Registration unexpectedly executed SQL.")
        set_stage("S26")
        description = json.loads(run(sudo + [wrapper, "--describe", approval_id], label="sudo-describe"))
        if description["sql_sha256"] != manifest["sql_sha256"] or description["sql_executed"] is not False:
            raise RuntimeError("Actual sudo describe binding differed.")
        set_stage("S27")
        for args in ([], [approval_id, "extra"], [approval_id.upper()], ["--sql", "/tmp/anything.sql"],
                     ["--describe", approval_id, "extra"], [";id"], ["admin", "initialize"]):
            run(sudo + [wrapper, *args], succeeds=False, label="sudo-invalid-arguments")
        for command in (["/usr/local/sbin/project1-migration-admin", "inspect"],
                        ["/usr/bin/python3", "-c", "pass"], ["/usr/bin/dotnet", "--info"]):
            run(sudo + command, succeeds=False, label="sudo-forbidden-command")
        run(["/usr/bin/sudo", "-u", "project1_deploy", "--", "/usr/bin/sudo", "-n",
             "PYTHONPATH=/tmp", wrapper, "--describe", approval_id], succeeds=False, label="sudo-environment-injection")
        set_stage("S28")
        for path in (ROOT / "production_migration.py", CONFIG / "execution-password",
                     LEDGER, archived, HOST / "Project1.MigrationHost.dll"):
            run(["/usr/bin/sudo", "-u", "project1_deploy", "--", "/usr/bin/test", "-w", str(path)], succeeds=False, label="private-write-denial")
        run(["/usr/bin/sudo", "-u", "project1_deploy", "--", "/usr/bin/test", "-r",
             str(CONFIG / "execution-password")], succeeds=False, label="credential-read-denial")
        print("PASS: actual sudo regex/describe, invalid arguments, NOSETENV, private root files, default-off", flush=True)
        # Actual installed entry/coordinator/root ledger/fixed host; only GitHub is a fixture.
        set_stage("S29")
        result = invoke_entry(["execute", approval_id])
        if result["phase"] != "succeeded" or ledger.inspect()["entries"][approval_id]["phase"] != "succeeded":
            raise RuntimeError("Durable terminal result differed.")
        set_stage("S30")
        run(fixture_command + ["after"], environment=child_env, label="fixture-after")
        set_stage("S31")
        run(sudo + [wrapper, "--describe", approval_id], succeeds=False, label="sudo-consumed-describe")
        run(sudo + [wrapper, approval_id], succeeds=False, label="sudo-consumed-execute")
        print("PASS: real approved SQL bytes, preserved product, durable success and replay rejection", flush=True)
        print("CI ONLY: fabricated artifact provenance; no production wrapper SQL submission, API, SSH or server access.", flush=True)
    finally:
        # Cleanup errors must not mask the original diagnostic stage/failure.
        cleanup_container(identifier, already_failing=sys.exc_info()[0] is not None)


def main(arguments=None):
    arguments = sys.argv[1:] if arguments is None else arguments
    try:
        set_stage("S00")
        require_ci(os.environ, sys.platform, os.geteuid() if hasattr(os, "geteuid") else -1)
        if len(arguments) != 4:
            raise RuntimeError("Expected reviewed CI build locations.")
        acceptance(*(Path(value).resolve(strict=True) for value in arguments))
        return 0
    except Exception as error:
        report_failure(error)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
