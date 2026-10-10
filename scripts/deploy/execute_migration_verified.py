"""Manual runner: send only an approval ID to the fixed root-managed helper."""
import argparse
import os
from pathlib import Path
import re
import sys
import tempfile

from deploy_verified import HOST, USER, DESTINATION, run_command, ssh_options
from migration_package import strict_json
from verify_ci_artifacts import REPOSITORY, VerificationError
from verify_migration_artifact import load_verified


def execute_remote(directory, identifier, key_value, hosts_value, runner_temp):
    if not re.fullmatch(r"[0-9a-f]{32}", identifier):
        raise VerificationError("Supply a registered lowercase 32-character approval ID.")
    manifest = load_verified(directory)  # Complete BEFORE secrets/network.
    if not key_value.strip() or not hosts_value.strip():
        raise VerificationError("Pinned SSH credentials are required.")
    with tempfile.TemporaryDirectory(prefix="project1-migration-ssh-", dir=runner_temp) as temporary:
        key, hosts = Path(temporary) / "key", Path(temporary) / "hosts"
        for path, value in ((key, key_value), (hosts, hosts_value)):
            path.write_text(value.replace("\r", "").rstrip("\n") + "\n", encoding="utf-8")
            path.chmod(0o600)
        run_command(["/usr/bin/ssh-keygen", "-y", "-P", "", "-f", str(key)])
        run_command(["/usr/bin/ssh-keygen", "-F", HOST, "-f", str(hosts)])
        ssh = ["/usr/bin/ssh", "-T", *ssh_options(key, hosts), DESTINATION]
        if run_command([*ssh, "whoami"]) != USER:
            raise VerificationError("Unexpected remote user.")
        description = run_command([*ssh, "sudo -n -- /usr/local/sbin/project1-migration-execute --describe " + identifier])
        expected_binding = {"schema": 1, "approval_id": identifier, "commit": manifest["commit"],
                            "run_id": manifest["run_id"], "run_attempt": manifest["run_attempt"],
                            "artifact_sha256": manifest["artifact"]["sha256"],
                            "sql_sha256": manifest["sql_sha256"], "sql_executed": False}
        described = strict_json(description) if len(description) <= 4096 else None
        if (described != expected_binding or described.get("sql_executed") is not False
                or type(described.get("schema")) is not int or type(described.get("run_attempt")) is not int):
            raise VerificationError("Registered approval differs from selected CI package; no execution submitted.")
        output = run_command([*ssh, "sudo -n -- /usr/local/sbin/project1-migration-execute " + identifier], timeout=360)
        if len(output) > 4096:
            raise VerificationError("Unexpected execution response size.")
        result = strict_json(output)
        expected = {"schema": 1, "approval_id": identifier, "commit": manifest["commit"],
                    "phase": "succeeded", "sql_executed": True, "production_enabled": True,
                    "run_id": manifest["run_id"], "run_attempt": manifest["run_attempt"],
                    "artifact_sha256": manifest["artifact"]["sha256"], "sql_sha256": manifest["sql_sha256"]}
        if (result != expected or type(result.get("schema")) is not int
                or type(result.get("run_attempt")) is not int or result.get("sql_executed") is not True
                or result.get("production_enabled") is not True):
            raise VerificationError("Execution could not be confirmed; administrator inspection required, not retry.")
        return expected


def main(arguments=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", required=True, type=Path)
    parser.add_argument("--approval-id", required=True)
    options = parser.parse_args(arguments)
    try:
        if (sys.platform != "linux" or os.environ.get("GITHUB_EVENT_NAME") != "workflow_dispatch"
                or os.environ.get("GITHUB_REF") != "refs/heads/main"
                or os.environ.get("GITHUB_REPOSITORY") != REPOSITORY
                or os.environ.get("MIGRATION_EXECUTION_CONFIRMED") != "true"
                or os.environ.get("DEPLOY_CONFIRMED") != "false"):
            raise VerificationError("Explicit manual main migration execution is required.")
        os.umask(0o077)
        result = execute_remote(options.directory, options.approval_id,
            os.environ.pop("DEPLOY_SSH_PRIVATE_KEY", ""), os.environ.pop("DEPLOY_SSH_KNOWN_HOSTS", ""),
            Path(os.environ["RUNNER_TEMP"]))
        print("Confirmed reviewed migration execution for approval " + result["approval_id"] + "; no app deployment or restart.")
        return 0
    except Exception:
        print("ERROR: Migration execution refused or could not be confirmed. Inspect trusted ledger and database; do not rerun blindly.")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
