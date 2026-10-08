"""Explicitly deploy verified ZIPs over pinned ordinary OpenSSH through Tailscale."""

from __future__ import annotations

import argparse
import os
from pathlib import Path
import subprocess
import sys
import tempfile

from verify_ci_artifacts import REPOSITORY, VerificationError, load_verified

HOST = "100.78.117.97"
USER = "project1_deploy"
DESTINATION = USER + "@" + HOST


def run_command(arguments: list[str], timeout: int = 45) -> str:
    result = subprocess.run(arguments, capture_output=True, text=True, timeout=timeout,
                            check=False, env={"PATH": "/usr/bin:/bin", "LANG": "C"})
    if result.returncode != 0:
        # Do not expose private keys, subprocess output, or unexpected remote messages.
        raise VerificationError("SSH transfer/command failed. Stop and inspect the workflow step and server transaction state; do not blindly retry.")
    return result.stdout.strip()


def ssh_options(key: Path, known_hosts: Path) -> list[str]:
    return ["-F", "/dev/null", "-i", str(key),
            "-o", "IdentitiesOnly=yes", "-o", "IdentityAgent=none",
            "-o", "PreferredAuthentications=publickey", "-o", "BatchMode=yes",
            "-o", "StrictHostKeyChecking=yes", "-o", f"UserKnownHostsFile={known_hosts}",
            "-o", "GlobalKnownHostsFile=/dev/null", "-o", "HostKeyAlgorithms=ssh-ed25519",
            "-o", "UpdateHostKeys=no", "-o", "ConnectTimeout=10",
            "-o", "ConnectionAttempts=1", "-o", "ServerAliveInterval=15",
            "-o", "ServerAliveCountMax=3"]


def deploy_verified(directory: Path, key_value: str, hosts_value: str, runner_temp: Path) -> dict:
    if not key_value.strip() or not hosts_value.strip():
        raise VerificationError("Deployment SSH secrets are required.")
    manifest = load_verified(directory)  # Must finish BEFORE any SSH command.
    with tempfile.TemporaryDirectory(prefix="project1-cd-ssh-", dir=runner_temp) as temporary:
        root = Path(temporary)
        key, hosts = root / "deploy_key", root / "known_hosts"
        key.write_text(key_value.replace("\r", "").rstrip("\n") + "\n", encoding="utf-8")
        hosts.write_text(hosts_value.replace("\r", "").rstrip("\n") + "\n", encoding="utf-8")
        key.chmod(0o600)
        hosts.chmod(0o600)
        run_command(["/usr/bin/ssh-keygen", "-y", "-P", "", "-f", str(key)])
        run_command(["/usr/bin/ssh-keygen", "-lf", str(hosts)])
        run_command(["/usr/bin/ssh-keygen", "-F", HOST, "-f", str(hosts)])
        options = ssh_options(key, hosts)
        ssh = ["/usr/bin/ssh", "-T", *options, DESTINATION]
        if run_command([*ssh, "whoami"]) != USER:
            raise VerificationError("SSH returned an unexpected deployment user.")
        help_text = run_command([*ssh, "sudo -n -- /usr/local/sbin/project1-deploy --help"])
        if not help_text.startswith("Usage: project1-deploy COMMIT_SHA API_ZIP_SHA256 FRONTEND_ZIP_SHA256"):
            raise VerificationError("Unexpected server deployment helper.")
        commit = manifest["commit"]  # Strict 40-character lowercase SHA; never shell input.
        remote = "/home/project1_deploy/uploads/" + commit
        # mkdir WITHOUT -p fails on an existing commit directory; never overwrite/retry.
        run_command([*ssh, f"umask 077; mkdir -m 0700 -- {remote}"])
        run_command(["/usr/bin/scp", *options, str(directory / "api.zip"),
                     str(directory / "frontend.zip"), f"{DESTINATION}:{remote}/"], timeout=300)
        run_command([*ssh, f"chmod 0600 -- {remote}/api.zip {remote}/frontend.zip"])
        api = manifest["artifacts"]["api"]["sha256"]
        frontend = manifest["artifacts"]["frontend"]["sha256"]
        output = run_command([*ssh, f"sudo -n -- /usr/local/sbin/project1-deploy {commit} {api} {frontend}"], timeout=420)
        if not output.startswith(f"Deployed {commit}; transaction ") or not output.endswith("API and frontend health checks passed."):
            raise VerificationError("Deployment response could not be confirmed. Ask the administrator to inspect server state before retrying.")
    return manifest


def main(arguments=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", required=True, type=Path)
    options = parser.parse_args(arguments)
    try:
        if (sys.platform != "linux" or os.environ.get("GITHUB_EVENT_NAME") != "workflow_dispatch"
                or os.environ.get("GITHUB_REF") != "refs/heads/main"
                or os.environ.get("GITHUB_REPOSITORY") != REPOSITORY
                or os.environ.get("DEPLOY_CONFIRMED") != "true"):
            raise VerificationError("Deployment requires an explicitly confirmed manual main workflow in Project1.")
        os.umask(0o077)
        key = os.environ.pop("DEPLOY_SSH_PRIVATE_KEY", "")
        hosts = os.environ.pop("DEPLOY_SSH_KNOWN_HOSTS", "")
        manifest = deploy_verified(options.directory, key, hosts, Path(os.environ["RUNNER_TEMP"]))
        print(f"Deployment confirmed for {manifest['commit']}; API and frontend health checks passed.")
        return 0
    except VerificationError as error:
        print(f"ERROR: {error}")
    except Exception as error:
        print(f"ERROR: Deployment transport failed ({type(error).__name__}). Inspect server state before retrying.")
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
