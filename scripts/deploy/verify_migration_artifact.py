"""Verify a migration artifact from an exact trusted CI attempt; no server access."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re

from migration_package import MAX_PACKAGE_BYTES, read_package, strict_json
from verify_ci_artifacts import (GitHubClient, REPOSITORY, REPOSITORY_ID, WORKFLOW_PATH,
                                 VerificationError, checked_sha, positive_id, validate_run)

PREFIX = "project1-migrations-sql-"


def select_artifact(items: list, run_id: int, commit: str, now: datetime) -> dict:
    matches = [item for item in items if isinstance(item, dict) and item.get("name") == PREFIX + commit]
    if len(matches) != 1:
        raise VerificationError("Exactly one migration artifact is required; older packages without a manifest are unsupported.")
    item = matches[0]
    artifact_id = positive_id(item.get("id"))
    digest = item.get("digest")
    size = item.get("size_in_bytes")
    if (type(size) is not int or not 0 < size <= MAX_PACKAGE_BYTES
            or not isinstance(digest, str) or not re.fullmatch(r"sha256:[0-9a-f]{64}", digest)):
        raise VerificationError("Invalid migration artifact digest or size.")
    try:
        expiration = datetime.fromisoformat(item["expires_at"].replace("Z", "+00:00"))
        if expiration.tzinfo is None:
            raise ValueError
    except (KeyError, TypeError, AttributeError, ValueError):
        raise VerificationError("Invalid migration artifact expiration.") from None
    provenance = item.get("workflow_run")
    if (item.get("expired") is not False or expiration <= now or not isinstance(provenance, dict)
            or provenance.get("id") != run_id or provenance.get("repository_id") != REPOSITORY_ID
            or provenance.get("head_repository_id") != REPOSITORY_ID
            or provenance.get("head_branch") != "main" or provenance.get("head_sha") != commit):
        raise VerificationError("Migration artifact is expired or has incorrect provenance.")
    return {"id": artifact_id, "name": PREFIX + commit, "sha256": digest[7:]}


def verify(client, run_id: int, commit: str, directory: Path,
           expected_attempt: int, expected_digest: str | None = None) -> dict:
    run_id, commit = positive_id(run_id), checked_sha(commit)
    expected_attempt = positive_id(expected_attempt)
    if expected_digest is not None and not re.fullmatch(r"[0-9a-f]{64}", expected_digest):
        raise VerificationError("Invalid expected migration ZIP digest.")
    workflow = client.get(f"/repos/{REPOSITORY}/actions/workflows/ci.yml")
    if workflow.get("path") != WORKFLOW_PATH or workflow.get("name") != "Project1 CI":
        raise VerificationError("Unexpected CI workflow identity.")
    workflow_id = positive_id(workflow.get("id"))
    run_path = f"/repos/{REPOSITORY}/actions/runs/{run_id}"
    attempt = validate_run(client.get(run_path), run_id, commit, workflow_id)
    artifact = select_artifact(client.artifacts(run_id), run_id, commit, datetime.now(timezone.utc))
    if attempt != expected_attempt or (expected_digest is not None and artifact["sha256"] != expected_digest):
        raise VerificationError("CI attempt or migration ZIP digest changed; precheck refused.")
    directory.mkdir(mode=0o700)
    archive = directory / "migrations.zip"
    client.download(artifact["id"], archive, artifact["sha256"])
    content = read_package(archive, run_id, commit, attempt)
    if (validate_run(client.get(run_path), run_id, commit, workflow_id) != attempt
            or select_artifact(client.artifacts(run_id), run_id, commit, datetime.now(timezone.utc)) != artifact):
        raise VerificationError("CI or migration artifact changed during verification.")
    result = {"schema": 1, "repository": REPOSITORY, "repository_id": REPOSITORY_ID,
              "run_id": run_id, "run_attempt": attempt, "commit": commit,
              "artifact": artifact, **content}
    with (directory / "verified-migration.json").open("x", encoding="utf-8") as output:
        json.dump(result, output, indent=2)
        output.write("\n")
    return result


def load_verified(directory: Path) -> dict:
    path = directory / "verified-migration.json"
    if path.is_symlink() or not path.is_file() or path.stat().st_size > 180000:
        raise VerificationError("Invalid verified migration manifest file.")
    value = strict_json(path.read_text(encoding="utf-8"))
    if (not isinstance(value, dict) or set(value) != {"schema", "repository", "repository_id", "run_id",
            "run_attempt", "commit", "artifact", "migrations", "sql_sha256"}
            or type(value["schema"]) is not int or value["schema"] != 1
            or value["repository"] != REPOSITORY or value["repository_id"] != REPOSITORY_ID):
        raise VerificationError("Invalid verified migration identity.")
    run_id, attempt, commit = positive_id(value["run_id"]), positive_id(value["run_attempt"]), checked_sha(value["commit"])
    artifact = value["artifact"]
    if (not isinstance(artifact, dict) or set(artifact) != {"id", "name", "sha256"}
            or artifact["name"] != PREFIX + commit
            or not isinstance(artifact["sha256"], str) or not re.fullmatch(r"[0-9a-f]{64}", artifact["sha256"])):
        raise VerificationError("Invalid verified migration artifact.")
    positive_id(artifact["id"])
    archive = directory / "migrations.zip"
    content = read_package(archive, run_id, commit, attempt)
    with archive.open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    if (digest != artifact["sha256"] or content["migrations"] != value["migrations"]
            or content["sql_sha256"] != value["sql_sha256"]):
        raise VerificationError("Verified migration package changed before SSH.")
    return value


def main(arguments=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--commit", required=True)
    parser.add_argument("--directory", required=True, type=Path)
    parser.add_argument("--expected-attempt", required=True)
    parser.add_argument("--expected-digest")
    options = parser.parse_args(arguments)
    try:
        os.umask(0o077)
        result = verify(GitHubClient(os.environ.get("GITHUB_TOKEN", "")), options.run_id, options.commit,
                        options.directory, options.expected_attempt, options.expected_digest)
        if os.environ.get("GITHUB_OUTPUT"):
            with Path(os.environ["GITHUB_OUTPUT"]).open("a", encoding="utf-8") as output:
                output.write(f"migration_digest={result['artifact']['sha256']}\n")
        print(f"Verified migration package for {result['commit']}; SQL was not executed.")
        return 0
    except VerificationError as error:
        print(f"ERROR: {error}")
    except Exception as error:
        print(f"ERROR: Migration artifact verification failed ({type(error).__name__}).")
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
