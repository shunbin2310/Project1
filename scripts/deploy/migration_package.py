"""Strict, offline migration-package validation. Nothing here executes SQL."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import stat
import zipfile

from verify_ci_artifacts import REPOSITORY, VerificationError

ID_PATTERN = r"[0-9]{14}_[A-Za-z][A-Za-z0-9_]{0,134}"
LIMITS = {"migrations.sql": 10 * 1024 * 1024, "build-info.txt": 16384,
          "migration-manifest.json": 160000, "SHA256SUMS": 1024}
MAX_PACKAGE_BYTES = 12 * 1024 * 1024


def strict_json(body: bytes | str):
    def unique_object(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise VerificationError("Duplicate JSON object fields are refused.")
            result[key] = value
        return result
    return json.loads(body, object_pairs_hook=unique_object)


def migration_ids(value: object, *, allow_empty: bool = False) -> list[str]:
    if (not isinstance(value, list) or len(value) > 1000 or (not value and not allow_empty)
            or any(not isinstance(item, str) or not re.fullmatch(ID_PATTERN, item) for item in value)
            or value != sorted(set(value))):
        raise VerificationError("Migration identifiers must be unique, ordered, bounded and valid.")
    return value


def manifest_ids(body: bytes) -> list[str]:
    value = strict_json(body)
    if (not isinstance(value, dict) or set(value) != {"schema", "migrations"}
            or type(value["schema"]) is not int or value["schema"] != 1):
        raise VerificationError("Invalid migration manifest schema.")
    return migration_ids(value["migrations"])


def check_sql_ids(sql: bytes, ids: list[str]) -> None:
    # A narrow consistency check for our EF SQL format, NOT a SQL safety analysis.
    text = sql.decode("utf-8-sig")
    found = re.findall(
        r"(?m)^[ \t]*INSERT INTO \[__EFMigrationsHistory\] \(\[MigrationId\], \[ProductVersion\]\)\s*"
        r"VALUES \(N'(" + ID_PATTERN + r")', N'[0-9.]+(?:[-A-Za-z0-9.]*)'\);\s*$", text)
    if found != ids:
        raise VerificationError("SQL migration-history inserts do not match the migration manifest.")


def read_package(archive: Path, run_id: int, commit: str, attempt: int) -> dict:
    if archive.is_symlink() or not archive.is_file() or not 0 < archive.stat().st_size <= MAX_PACKAGE_BYTES:
        raise VerificationError("Invalid migration ZIP file or size.")
    files = {}
    with zipfile.ZipFile(archive) as package:
        entries = package.infolist()
        if len(entries) != len(LIMITS) or {entry.filename for entry in entries} != set(LIMITS):
            raise VerificationError("Migration ZIP must contain exactly the four expected root files.")
        for entry in entries:
            mode = entry.external_attr >> 16
            if (entry.is_dir() or entry.flag_bits & 1
                    or stat.S_IFMT(mode) not in (0, stat.S_IFREG)
                    or not 0 < entry.file_size <= LIMITS[entry.filename]):
                raise VerificationError("Unsafe migration ZIP member or size.")
            with package.open(entry) as stream:
                body = stream.read(LIMITS[entry.filename] + 1)
            if len(body) != entry.file_size:
                raise VerificationError("Invalid migration ZIP member content.")
            files[entry.filename] = body
    sums = files["SHA256SUMS"].decode("ascii").splitlines()
    expected = [hashlib.sha256(files[name]).hexdigest() + "  " + name
                for name in ("migrations.sql", "build-info.txt", "migration-manifest.json")]
    if sums != expected:
        raise VerificationError("Migration file checksums do not match.")
    lines = files["build-info.txt"].decode("utf-8").splitlines()
    provenance = [f"Repository: {REPOSITORY}", f"Commit SHA: {commit}",
                  f"Checked-out commit: {commit}", f"CI run ID: {run_id}",
                  f"CI run attempt: {attempt}", "Event: push", "Ref: refs/heads/main",
                  f"Run URL: https://github.com/{REPOSITORY}/actions/runs/{run_id}"]
    if (lines[:8] != provenance or len(lines) != 11 or
            lines[8] != "Migration range: 0 to latest migration in this commit; idempotent SQL Server script." or
            lines[9] not in {
                "REVIEW ONLY: CI tests this SQL only in disposable databases. CD never executes it.",
                "REVIEW REQUIRED: production execution needs a separately registered administrator approval.",
            } or lines[10] != "Review, test against the target schema and back up before manual production use."):
        raise VerificationError("Migration build information does not match the selected CI attempt.")
    ids = manifest_ids(files["migration-manifest.json"])
    check_sql_ids(files["migrations.sql"], ids)
    return {"migrations": ids, "sql_sha256": hashlib.sha256(files["migrations.sql"]).hexdigest()}


def generate_manifest(output: str) -> bytes:
    if len(output) > 1024 * 1024:
        raise VerificationError("EF migration list is too large.")
    # --prefix-output distinguishes JSON data from host/tool diagnostic messages.
    data = "\n".join(line[5:].lstrip() for line in output.splitlines() if line.startswith("data:"))
    entries = strict_json(data)
    if not isinstance(entries, list) or any(not isinstance(item, dict) for item in entries):
        raise VerificationError("Invalid EF migration-list output.")
    ids = migration_ids([item.get("id") for item in entries])
    return (json.dumps({"schema": 1, "migrations": ids}, indent=2) + "\n").encode("utf-8")


def main(arguments=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ef-list", required=True, type=Path)
    parser.add_argument("--directory", required=True, type=Path)
    options = parser.parse_args(arguments)
    try:
        with options.ef_list.open("r", encoding="utf-8-sig") as stream:
            body = generate_manifest(stream.read(1024 * 1024 + 1))
        sql = (options.directory / "migrations.sql").read_bytes()
        if not 0 < len(sql) <= LIMITS["migrations.sql"]:
            raise VerificationError("Invalid generated SQL size.")
        check_sql_ids(sql, manifest_ids(body))
        with (options.directory / "migration-manifest.json").open("xb") as output:
            output.write(body)
        print("Migration manifest generated and matched to SQL; no database access.")
        return 0
    except Exception as error:
        print(f"ERROR: Migration manifest generation failed ({type(error).__name__}).")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
