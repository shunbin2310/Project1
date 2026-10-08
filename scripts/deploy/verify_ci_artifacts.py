"""Download and verify an explicit trusted CI run; never connect to the server."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request

from project1_deploy import (CHUNK_BYTES, MAX_ARCHIVE_BYTES, DeploymentError,
                             Request, extract_archive)

REPOSITORY = "shunbin2310/Project1"
REPOSITORY_ID = 1334916963
WORKFLOW_PATH = ".github/workflows/ci.yml"
API_ROOT = "https://api.github.com"
MAX_JSON_BYTES = 2 * 1024 * 1024


class VerificationError(Exception):
    """Only fixed, safe messages are exposed to workflow logs."""


def positive_id(value: object) -> int:
    if isinstance(value, bool) or not re.fullmatch(r"[1-9][0-9]{0,19}", str(value)):
        raise VerificationError("Run/artifact identifiers must be positive decimal integers.")
    return int(str(value))


def checked_sha(value: str) -> str:
    if not re.fullmatch(r"[0-9a-f]{40}", value):
        raise VerificationError("Supply the full lowercase 40-character CI commit SHA.")
    return value


def trusted_repository(value: object) -> bool:
    return (isinstance(value, dict) and value.get("id") == REPOSITORY_ID
            and value.get("full_name") == REPOSITORY)


def validate_run(run: dict, run_id: int, commit: str, workflow_id: int) -> int:
    if not isinstance(run, dict):
        raise VerificationError("Invalid CI run metadata.")
    if (run.get("id") != run_id or run.get("workflow_id") != workflow_id
            or run.get("path") != WORKFLOW_PATH
            or not trusted_repository(run.get("repository"))
            or not trusted_repository(run.get("head_repository"))
            or run.get("head_sha") != commit or run.get("head_branch") != "main"
            or run.get("event") != "push" or run.get("status") != "completed"
            or run.get("conclusion") != "success"):
        raise VerificationError("CI must be a successful completed main push in this repository and ci.yml, at the requested SHA.")
    return positive_id(run.get("run_attempt"))


def select_artifacts(items: list, run_id: int, commit: str, now: datetime) -> dict:
    result = {}
    for kind, prefix in (("api", "project1-api-linux-x64-"),
                         ("frontend", "project1-frontend-dist-")):
        matches = [item for item in items if isinstance(item, dict)
                   and item.get("name") == prefix + commit]
        if len(matches) != 1:
            raise VerificationError("Each exact CI build artifact name must occur once; missing or duplicate artifacts are refused.")
        item = matches[0]
        artifact_id = positive_id(item.get("id"))
        size = item.get("size_in_bytes")
        if type(size) is not int or not 0 < size <= MAX_ARCHIVE_BYTES:
            raise VerificationError("CI artifact size is empty or exceeds the ZIP limit.")
        digest = item.get("digest")
        if not isinstance(digest, str) or not re.fullmatch(r"sha256:[0-9a-f]{64}", digest):
            raise VerificationError("GitHub must supply a SHA-256 artifact digest.")
        try:
            expiration = datetime.fromisoformat(item["expires_at"].replace("Z", "+00:00"))
            if expiration.tzinfo is None:
                raise ValueError
        except (KeyError, AttributeError, TypeError, ValueError):
            raise VerificationError("Invalid artifact expiration metadata.") from None
        provenance = item.get("workflow_run")
        if (item.get("expired") is not False or expiration <= now
                or not isinstance(provenance, dict) or provenance.get("id") != run_id
                or provenance.get("repository_id") != REPOSITORY_ID
                or provenance.get("head_repository_id") != REPOSITORY_ID
                or provenance.get("head_branch") != "main" or provenance.get("head_sha") != commit):
            raise VerificationError("Artifact is expired or does not belong to the selected trusted CI run.")
        result[kind] = {"id": artifact_id, "name": item["name"], "sha256": digest[7:]}
    if result["api"]["id"] == result["frontend"]["id"]:
        raise VerificationError("API and frontend must be different artifacts.")
    return result


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, response, code, message, headers, new_url):
        return None


def storage_url(url: str) -> str:
    parsed = urllib.parse.urlsplit(url)
    host = parsed.hostname or ""
    if (parsed.scheme != "https" or parsed.username is not None or parsed.password is not None
            or parsed.port not in (None, 443) or parsed.fragment
            or not any(host.endswith(suffix) for suffix in
                       (".blob.core.windows.net", ".githubusercontent.com"))):
        raise VerificationError("Artifact redirect is not a trusted HTTPS GitHub storage endpoint.")
    return url


def copy_archive(stream, destination: Path, expected_digest: str) -> None:
    count = 0
    hasher = hashlib.sha256()
    deadline = time.monotonic() + 180
    with destination.open("xb") as output:
        while chunk := stream.read(CHUNK_BYTES):
            count += len(chunk)
            if count > MAX_ARCHIVE_BYTES or time.monotonic() > deadline:
                raise VerificationError("Artifact download exceeded its size/time limit.")
            hasher.update(chunk)
            output.write(chunk)
    if count == 0 or hasher.hexdigest() != expected_digest:
        raise VerificationError("Downloaded ZIP does not match the GitHub artifact SHA-256 digest.")


class GitHubClient:
    def __init__(self, token: str):
        if not token or "\n" in token or "\r" in token:
            raise VerificationError("GITHUB_TOKEN is required for artifact verification.")
        self.token = token
        # Do not follow API redirects with Authorization or use ambient proxy settings.
        self.opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())

    def request(self, path: str):
        if not path.startswith(f"/repos/{REPOSITORY}/actions/"):
            raise VerificationError("Only the fixed repository Actions API is allowed.")
        request = urllib.request.Request(API_ROOT + path, headers={
            "Authorization": "Bearer " + self.token,
            "Accept": "application/vnd.github+json",
            "X-GitHub-Api-Version": "2026-03-10",
            "User-Agent": "Project1-manual-CD",
        })
        return self.opener.open(request, timeout=20)

    def get(self, path: str) -> dict:
        with self.request(path) as response:
            if response.status != 200:
                raise VerificationError("Unexpected GitHub API response status.")
            body = response.read(MAX_JSON_BYTES + 1)
        if len(body) > MAX_JSON_BYTES:
            raise VerificationError("GitHub API response exceeded the JSON size limit.")
        value = json.loads(body)
        if not isinstance(value, dict):
            raise VerificationError("GitHub API response must be an object.")
        return value

    def artifacts(self, run_id: int) -> list:
        items = []
        total = None
        for page in range(1, 11):
            result = self.get(f"/repos/{REPOSITORY}/actions/runs/{run_id}/artifacts?per_page=100&page={page}")
            batch = result.get("artifacts")
            count = result.get("total_count")
            if (not isinstance(batch, list) or len(batch) > 100
                    or type(count) is not int or not 0 <= count <= 1000
                    or (total is not None and count != total)):
                raise VerificationError("Invalid or changing artifact pagination metadata.")
            total = count
            items.extend(batch)
            if len(items) == total:
                return items
            if not batch or len(items) > total:
                break
        raise VerificationError("Artifact pagination is incomplete or exceeds the limit.")

    def download(self, artifact_id: int, destination: Path, digest: str) -> None:
        path = f"/repos/{REPOSITORY}/actions/artifacts/{artifact_id}/zip"
        try:
            with self.request(path):
                raise VerificationError("Artifact download must return an explicit storage redirect.")
        except urllib.error.HTTPError as response:
            try:
                if response.code != 302:
                    raise VerificationError("GitHub could not issue an artifact download redirect.")
                location = storage_url(response.headers.get("Location", ""))
            finally:
                response.close()
        # Fresh request, NO GitHub bearer token, and no further redirects accepted.
        with self.opener.open(urllib.request.Request(location), timeout=20) as response:
            if response.status != 200:
                raise VerificationError("Unexpected artifact storage response status.")
            copy_archive(response, destination, digest)


def verify(client, run_id: int, commit: str, directory: Path, expected: tuple | None = None) -> dict:
    run_id = positive_id(run_id)
    commit = checked_sha(commit)
    workflow = client.get(f"/repos/{REPOSITORY}/actions/workflows/ci.yml")
    if workflow.get("path") != WORKFLOW_PATH or workflow.get("name") != "Project1 CI":
        raise VerificationError("Unexpected CI workflow identity.")
    workflow_id = positive_id(workflow.get("id"))
    run_path = f"/repos/{REPOSITORY}/actions/runs/{run_id}"
    attempt = validate_run(client.get(run_path), run_id, commit, workflow_id)
    artifacts = select_artifacts(client.artifacts(run_id), run_id, commit, datetime.now(timezone.utc))
    if expected is not None:
        expected_attempt, api_digest, frontend_digest = expected
        positive_id(expected_attempt)
        Request(commit, api_digest, frontend_digest).validate()
        if (attempt, artifacts["api"]["sha256"], artifacts["frontend"]["sha256"]) != expected:
            raise VerificationError("CI attempt or ZIP digests changed since the validation job; deployment is refused.")
    directory.mkdir(mode=0o700)  # Never reuse stale ZIPs/manifests from another invocation.
    for kind, artifact in artifacts.items():
        archive = directory / (kind + ".zip")
        client.download(artifact["id"], archive, artifact["sha256"])
        # Only temporary extraction/validation, NEVER execute artifact contents.
        with tempfile.TemporaryDirectory(prefix="project1-package-check-") as sandbox:
            extract_archive(archive, Path(sandbox) / "contents", kind)
    if validate_run(client.get(run_path), run_id, commit, workflow_id) != attempt:
        raise VerificationError("CI was rerun while checking artifacts; choose a stable successful run.")
    if select_artifacts(client.artifacts(run_id), run_id, commit, datetime.now(timezone.utc)) != artifacts:
        raise VerificationError("Artifact metadata changed while downloading; deployment is refused.")
    manifest = {"schema": 1, "repository": REPOSITORY, "repository_id": REPOSITORY_ID,
                "run_id": run_id, "run_attempt": attempt, "commit": commit, "artifacts": artifacts}
    with (directory / "verified.json").open("x", encoding="utf-8") as output:
        json.dump(manifest, output, indent=2)
        output.write("\n")
    return manifest


def write_outputs(manifest: dict, path: Path) -> None:
    # Values have been validated as decimal IDs / hex digests, never untrusted text.
    with path.open("a", encoding="utf-8") as output:
        output.write(f"ci_attempt={manifest['run_attempt']}\n")
        for kind in ("api", "frontend"):
            output.write(f"{kind}_digest={manifest['artifacts'][kind]['sha256']}\n")


def load_verified(directory: Path) -> dict:
    manifest_path = directory / "verified.json"
    if manifest_path.is_symlink() or manifest_path.stat().st_size > 16384:
        raise VerificationError("Invalid verified manifest file.")
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    if (not isinstance(manifest, dict) or manifest.get("schema") != 1
            or manifest.get("repository") != REPOSITORY
            or manifest.get("repository_id") != REPOSITORY_ID
            or not isinstance(manifest.get("artifacts"), dict)
            or set(manifest["artifacts"]) != {"api", "frontend"}):
        raise VerificationError("Invalid verified manifest identity.")
    checked_sha(manifest["commit"])
    positive_id(manifest["run_id"])
    positive_id(manifest["run_attempt"])
    for kind, prefix in (("api", "project1-api-linux-x64-"), ("frontend", "project1-frontend-dist-")):
        artifact = manifest["artifacts"][kind]
        positive_id(artifact["id"])
        Request(manifest["commit"], artifact["sha256"], artifact["sha256"]).validate()
        if artifact["name"] != prefix + manifest["commit"]:
            raise VerificationError("Invalid verified artifact name.")
        path = directory / (kind + ".zip")
        if path.is_symlink() or not path.is_file():
            raise VerificationError("Verified archive must be a regular file, not a link.")
        with path.open("rb") as stream:
            # Recheck immediately before SSH; no extraction or execution needed here.
            hasher = hashlib.sha256()
            total = 0
            while chunk := stream.read(CHUNK_BYTES):
                total += len(chunk)
                if total > MAX_ARCHIVE_BYTES:
                    raise VerificationError("Verified ZIP exceeds the size limit.")
                hasher.update(chunk)
        if total == 0 or hasher.hexdigest() != artifact["sha256"]:
            raise VerificationError("Verified ZIP changed after verification.")
    return manifest


def main(arguments=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--commit", required=True)
    parser.add_argument("--directory", required=True, type=Path)
    parser.add_argument("--expected-attempt")
    parser.add_argument("--expected-api-digest")
    parser.add_argument("--expected-frontend-digest")
    options = parser.parse_args(arguments)
    try:
        os.umask(0o077)
        run_id = positive_id(options.run_id)
        commit = checked_sha(options.commit)
        values = (options.expected_attempt, options.expected_api_digest, options.expected_frontend_digest)
        if any(value is not None for value in values) and any(value is None for value in values):
            raise VerificationError("Supply all three expected attempt/digest values, or none.")
        expected = None
        if values[0] is not None:
            expected = (positive_id(values[0]), values[1], values[2])
            Request(commit, values[1], values[2]).validate()
        manifest = verify(GitHubClient(os.environ.get("GITHUB_TOKEN", "")), run_id, commit, options.directory, expected)
        if os.environ.get("GITHUB_OUTPUT"):
            write_outputs(manifest, Path(os.environ["GITHUB_OUTPUT"]))
        print(f"Verified CI run {run_id}, attempt {manifest['run_attempt']}, commit {commit}.")
        print("Both ZIP digests and package layouts passed. This step performs no deployment.")
        return 0
    except (VerificationError, DeploymentError) as error:
        print(f"ERROR: {error}")
    except Exception as error:
        # Never reveal bearer tokens, signed download URLs, HTTP bodies or ZIP content.
        print(f"ERROR: Artifact verification failed ({type(error).__name__}).")
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
