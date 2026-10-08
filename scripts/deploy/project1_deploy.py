#!/usr/bin/python3
"""Fixed-path Ubuntu deployment helper. Not installed or invoked by CI yet.

The privileged entry point accepts ONLY a full commit SHA and two archive hashes.
It never executes uploaded scripts, modifies production settings, or runs migrations.
"""

from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import stat
import subprocess
import sys
import tempfile
import time
from contextlib import contextmanager
from dataclasses import dataclass
from typing import Iterator
import urllib.error
import urllib.request
import uuid
import zipfile


MAX_ARCHIVE_BYTES = 256 * 1024 * 1024
MAX_EXPANDED_BYTES = 512 * 1024 * 1024
MAX_FILE_BYTES = 128 * 1024 * 1024
MAX_MEMBERS = 10_000
CHUNK_BYTES = 1024 * 1024
SERVICE = "project1-api.service"
API_REQUIRED = {
    "Project1.Api.dll", "Project1.Api.deps.json", "Project1.Api.runtimeconfig.json"
}
SAFE_ENV = {"PATH": "/usr/bin:/bin", "LANG": "C", "SYSTEMD_PAGER": ""}


class DeploymentError(Exception):
    """A safe-to-display deployment failure (never include archive contents)."""


@dataclass(frozen=True)
class Request:
    commit: str
    api_digest: str
    frontend_digest: str

    def validate(self) -> None:
        if not re.fullmatch(r"[0-9a-f]{40}", self.commit):
            raise DeploymentError("Commit must be a full lowercase 40-character Git SHA.")
        for digest in (self.api_digest, self.frontend_digest):
            if not re.fullmatch(r"[0-9a-f]{64}", digest):
                raise DeploymentError("Each ZIP digest must be a lowercase SHA-256 hex value.")


@dataclass(frozen=True)
class Layout:
    # Test code may construct a sandbox layout; the CLI has no path overrides.
    api: Path = Path("/opt/project1/api")
    frontend: Path = Path("/var/www/project1")
    uploads: Path = Path("/home/project1_deploy/uploads")
    state: Path = Path("/var/lib/project1-deploy")


def entry_exists(path: Path) -> bool:
    return os.path.lexists(path)


def require_root_directory(path: Path) -> None:
    """Reject links and writable/non-root ancestors, not just the final directory."""
    if not path.is_absolute():
        raise DeploymentError("Managed directories must be absolute.")
    for current in [*reversed(path.parents), path]:
        metadata = current.lstat()
        if (not stat.S_ISDIR(metadata.st_mode) or metadata.st_uid != 0
                or metadata.st_mode & 0o022):
            raise DeploymentError("Managed directory ancestry must be root-owned and not group/other writable.")


def require_root_file(path: Path) -> None:
    require_root_directory(path.parent)
    metadata = path.lstat()
    if (not stat.S_ISREG(metadata.st_mode) or metadata.st_uid != 0
            or metadata.st_mode & 0o022 or metadata.st_nlink != 1):
        raise DeploymentError("Managed files must be single-link, root-owned regular files with no group/other writes.")


def validate_live_tree(path: Path) -> None:
    require_root_directory(path)
    for directory, directories, files in os.walk(path, followlinks=False):
        for name in directories:
            require_root_directory(Path(directory) / name)
        for name in files:
            require_root_file(Path(directory) / name)


def validate_upload_metadata(metadata: os.stat_result, upload_uid: int) -> None:
    if (not stat.S_ISREG(metadata.st_mode) or metadata.st_uid != upload_uid
            or metadata.st_nlink != 1 or not 0 < metadata.st_size <= MAX_ARCHIVE_BYTES):
        raise DeploymentError("Upload must be a bounded, single-link regular ZIP owned by project1_deploy.")


@contextmanager
def open_upload(path: Path, upload_uid: int) -> Iterator[object]:
    """Open each untrusted path component by descriptor, without following links.

    The source is only READ. A user renaming it cannot redirect any root writes.
    O_NONBLOCK also avoids waiting forever if an attacker supplies a FIFO.
    """
    directory_fd = os.open("/", os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    try:
        for component in path.parent.parts[1:]:
            next_fd = os.open(component, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW,
                              dir_fd=directory_fd)
            os.close(directory_fd)
            directory_fd = next_fd
            metadata = os.fstat(directory_fd)
            if metadata.st_uid not in (0, upload_uid) or metadata.st_mode & 0o022:
                raise DeploymentError("Upload ancestry must belong to root/deploy and exclude group/other writes.")
        descriptor = os.open(path.name, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK,
                             dir_fd=directory_fd)
        with os.fdopen(descriptor, "rb") as source:
            validate_upload_metadata(os.fstat(source.fileno()), upload_uid)
            yield source
    finally:
        os.close(directory_fd)


def copy_and_verify(source: object, destination: Path, digest: str) -> None:
    """Hash a private snapshot, NOT a user-writable file that is later reopened."""
    hasher = hashlib.sha256()
    count = 0
    with destination.open("xb") as target:
        while chunk := source.read(CHUNK_BYTES):
            count += len(chunk)
            if count > MAX_ARCHIVE_BYTES:
                raise DeploymentError("ZIP exceeded the archive size limit while being copied.")
            hasher.update(chunk)
            target.write(chunk)
        target.flush()
        os.fsync(target.fileno())
    if count == 0 or hasher.hexdigest() != digest:
        raise DeploymentError("ZIP SHA-256 does not match the requested artifact digest.")


def snapshot_upload(source: Path, destination: Path, digest: str, upload_uid: int) -> None:
    with open_upload(source, upload_uid) as stream:
        copy_and_verify(stream, destination, digest)


def validate_members(archive: zipfile.ZipFile, kind: str) -> list[tuple[zipfile.ZipInfo, str]]:
    if kind not in ("api", "frontend"):
        raise DeploymentError("Unknown artifact type.")
    members = archive.infolist()
    if not members or len(members) > MAX_MEMBERS:
        raise DeploymentError("ZIP member count is empty or exceeds the limit.")
    seen: dict[str, bool] = {}
    folded: set[str] = set()
    result = []
    total = 0
    for item in members:
        raw = item.orig_filename
        name = raw[:-1] if item.is_dir() else raw
        parts = name.split("/")
        if (not name or len(raw) > 512 or raw.startswith("/")
                or any(char in raw for char in '\\:*?"<>|')
                or any(ord(char) < 32 or ord(char) == 127 for char in raw)
                or any(part in ("", ".", "..") or part.endswith((" ", ".")) for part in parts)):
            raise DeploymentError("ZIP contains an unsafe member path.")
        if name.casefold() in folded:
            raise DeploymentError("ZIP contains duplicate or case-colliding paths.")
        folded.add(name.casefold())
        mode = (item.external_attr >> 16) & 0xFFFF
        file_type = stat.S_IFMT(mode)
        allowed_types = (0, stat.S_IFDIR) if item.is_dir() else (0, stat.S_IFREG)
        if file_type not in allowed_types or mode & 0o7000:
            raise DeploymentError("ZIP links, special files, and privileged permission bits are forbidden.")
        if item.flag_bits & 1 or item.compress_type not in (zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED):
            raise DeploymentError("ZIP must be unencrypted and use stored/deflate compression.")
        if item.file_size < 0 or item.file_size > MAX_FILE_BYTES:
            raise DeploymentError("ZIP member exceeds the file size limit.")
        if item.is_dir() and item.file_size != 0:
            raise DeploymentError("ZIP directories must not contain data.")
        total += item.file_size
        if total > MAX_EXPANDED_BYTES:
            raise DeploymentError("ZIP expanded size exceeds the limit.")
        for part in parts:
            lower = part.casefold()
            if (lower in (".ssh", "id_rsa", "id_ed25519", "project1_github_deploy")
                    or lower == ".env" or lower.startswith(".env.")
                    or lower in ("appsettings.development.json", "appsettings.production.json")
                    or lower.endswith((".key", ".pem", ".pfx"))):
                raise DeploymentError("Server-specific/secret configuration must not be in a deployment ZIP.")
        seen[name] = item.is_dir()
        result.append((item, name))
    for _, name in result:
        parents = name.split("/")[:-1]
        for index in range(1, len(parents) + 1):
            parent = "/".join(parents[:index])
            if parent in seen and not seen[parent]:
                raise DeploymentError("ZIP file/directory paths conflict.")
    files = {name for item, name in result if not item.is_dir() and item.file_size > 0}
    if kind == "api" and not API_REQUIRED.issubset(files):
        raise DeploymentError("API ZIP must contain publish files directly at its root.")
    if kind == "frontend" and ("index.html" not in files or not any(
            name.startswith("assets/") for name in files)):
        raise DeploymentError("Frontend ZIP must contain index.html and assets directly at its root.")
    return result


def extract_archive(snapshot: Path, destination: Path, kind: str) -> None:
    """Destination is a new private root-owned directory, never a live directory."""
    try:
        with zipfile.ZipFile(snapshot) as archive:
            members = validate_members(archive, kind)
            destination.mkdir(mode=0o700)
            for item, name in members:
                target = destination / name
                if item.is_dir():
                    target.mkdir(mode=0o700, parents=True, exist_ok=True)
                    continue
                target.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
                count = 0
                with archive.open(item) as source, target.open("xb") as output:
                    while chunk := source.read(CHUNK_BYTES):
                        count += len(chunk)
                        if count > item.file_size or count > MAX_FILE_BYTES:
                            raise DeploymentError("ZIP member expanded beyond its declared size.")
                        output.write(chunk)
                    output.flush()
                    os.fsync(output.fileno())
                if count != item.file_size:
                    raise DeploymentError("ZIP member length did not match its metadata.")
        if kind == "api":
            with (destination / "Project1.Api.dll").open("rb") as assembly:
                if assembly.read(2) != b"MZ":
                    raise DeploymentError("API DLL is not a PE assembly.")
            config_path = destination / "Project1.Api.runtimeconfig.json"
            if config_path.stat().st_size > CHUNK_BYTES:
                raise DeploymentError("Runtime configuration is too large.")
            config = json.loads(config_path.read_text(encoding="utf-8"))
            options = config.get("runtimeOptions") if isinstance(config, dict) else None
            if not isinstance(options, dict) or options.get("tfm") != "net10.0":
                raise DeploymentError("This helper expects a .NET 10 publish artifact.")
    except (zipfile.BadZipFile, NotImplementedError, RuntimeError, ValueError, UnicodeError) as error:
        raise DeploymentError("ZIP is corrupt or its runtime configuration is invalid.") from error


def set_runtime_permissions(path: Path, group_id: int) -> None:
    for directory, _, files in os.walk(path, followlinks=False):
        os.chown(directory, 0, group_id)
        os.chmod(directory, 0o750)
        for name in files:
            target = Path(directory) / name
            os.chown(target, 0, group_id)
            os.chmod(target, 0o640)  # Never inherit ZIP execute/setuid/world permissions.


def fsync_directory(path: Path) -> None:
    if os.name == "posix":
        descriptor = os.open(path, os.O_RDONLY | os.O_DIRECTORY)
        try:
            os.fsync(descriptor)
        finally:
            os.close(descriptor)


def write_record(path: Path, value: dict) -> None:
    descriptor, temporary = tempfile.mkstemp(prefix=".record-", dir=path.parent)
    try:
        with os.fdopen(descriptor, "w", encoding="utf-8") as output:
            json.dump(value, output, sort_keys=True, indent=2)
            output.write("\n")
            output.flush()
            os.fsync(output.fileno())
        os.replace(temporary, path)
        fsync_directory(path.parent)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


@contextmanager
def deployment_lock(state: Path) -> Iterator[None]:
    import fcntl  # Linux only; sandbox transaction tests do not invoke this lock.

    descriptor = os.open(state / "deploy.lock", os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW, 0o600)
    try:
        metadata = os.fstat(descriptor)
        if (not stat.S_ISREG(metadata.st_mode) or metadata.st_uid != 0
                or metadata.st_nlink != 1 or metadata.st_mode & 0o077):
            raise DeploymentError("Unsafe deployment lock file.")
        try:
            fcntl.flock(descriptor, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError as error:
            raise DeploymentError("Another deployment is already running.") from error
        yield
    finally:
        os.close(descriptor)


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise DeploymentError("Health checks must not redirect.")


@contextmanager
def http_deadline(seconds: float) -> Iterator[None]:
    # Socket timeouts alone are inactivity timeouts; a slow-drip response could
    # otherwise keep a health request alive indefinitely. The CLI is single-threaded Linux.
    import signal

    def expired(_number, _frame):
        raise TimeoutError("Health request deadline exceeded.")

    previous = signal.signal(signal.SIGALRM, expired)
    signal.setitimer(signal.ITIMER_REAL, seconds)
    try:
        yield
    finally:
        signal.setitimer(signal.ITIMER_REAL, 0)
        signal.signal(signal.SIGALRM, previous)


class ServiceRuntime:
    def __init__(self) -> None:
        import grp
        import pwd

        self.upload_uid = pwd.getpwnam("project1_deploy").pw_uid
        self.api_uid = pwd.getpwnam("project1").pw_uid
        self.api_gid = grp.getgrnam("project1").gr_gid
        self.frontend_gid = grp.getgrnam("www-data").gr_gid
        if 0 in (self.upload_uid, self.api_uid, self.api_gid, self.frontend_gid):
            raise DeploymentError("Deployment/runtime identities must not be root.")
        self.http = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())

    def systemctl(self, *arguments: str) -> str:
        try:
            result = subprocess.run(["/usr/bin/systemctl", *arguments], check=True,
                                    capture_output=True, text=True, timeout=45, env=SAFE_ENV)
            return result.stdout
        except (subprocess.SubprocessError, OSError) as error:
            raise DeploymentError("The fixed project1-api systemctl operation failed.") from error

    def preflight(self, layout: Layout) -> None:
        require_root_file(Path("/etc/systemd/system/project1-api.service"))
        output = self.systemctl("show", SERVICE, "--no-pager",
                               "--property=User,Group,WorkingDirectory,NoNewPrivileges,FragmentPath,DropInPaths,ActiveState")
        properties = dict(line.split("=", 1) for line in output.splitlines() if "=" in line)
        expected = {"User": "project1", "Group": "project1", "WorkingDirectory": str(layout.api),
                    "NoNewPrivileges": "yes", "FragmentPath": "/etc/systemd/system/project1-api.service",
                    "DropInPaths": "", "ActiveState": "active"}
        if any(properties.get(name) != value for name, value in expected.items()):
            raise DeploymentError("API service configuration/state differs from the reviewed deployment layout.")

    def stop(self) -> None:
        self.systemctl("stop", SERVICE)

    def start(self) -> None:
        self.systemctl("start", SERVICE)

    def read_http(self, url: str, limit: int) -> bytes:
        with http_deadline(4):
            with self.http.open(url, timeout=3) as response:
                if response.status != 200:
                    raise DeploymentError("Health endpoint did not return HTTP 200.")
                body = response.read(limit + 1)
        if len(body) > limit:
            raise DeploymentError("Health response exceeded its size limit.")
        return body

    def wait_healthy(self, index_digest: str) -> None:
        deadline = time.monotonic() + 60
        while True:
            try:
                for url in ("http://127.0.0.1:5000/api/health", "http://127.0.0.1/api/health"):
                    payload = json.loads(self.read_http(url, 4096))
                    if not isinstance(payload, dict) or payload.get("status") != "ok":
                        raise DeploymentError("API health status was not ok.")
                index = self.read_http("http://127.0.0.1/index.html", CHUNK_BYTES)
                if hashlib.sha256(index).hexdigest() != index_digest:
                    raise DeploymentError("Nginx did not serve the expected frontend index.")
                if self.systemctl("is-active", SERVICE).strip() != "active":
                    raise DeploymentError("API service is not active after health checks.")
                return
            except (DeploymentError, urllib.error.URLError, TimeoutError, OSError, ValueError):
                if time.monotonic() >= deadline:
                    raise DeploymentError("API/frontend health checks did not pass before the deadline.")
                time.sleep(2)


def file_digest(path: Path) -> str:
    hasher = hashlib.sha256()
    with path.open("rb") as source:
        while chunk := source.read(CHUNK_BYTES):
            hasher.update(chunk)
    return hasher.hexdigest()


def check_layout(layout: Layout) -> None:
    for live in (layout.api, layout.frontend):
        validate_live_tree(live)
        if shutil.disk_usage(live.parent).free < 2 * 1024**3:
            raise DeploymentError("At least 2 GiB free space is required on deployment filesystems.")
    require_root_directory(layout.state)
    if layout.state.stat().st_mode & 0o077:
        raise DeploymentError("Deployment state directory must be private to root (0700).")
    if shutil.disk_usage(layout.state).free < 2 * 1024**3:
        raise DeploymentError("At least 2 GiB free space is required for private ZIP snapshots.")
    for item in layout.api.iterdir():
        if (item.name.casefold() in ("appsettings.production.json", "uploads", "data", ".aspnet")
                or item.name.casefold().startswith(".env")):
            raise DeploymentError("API directory contains local settings/data; review persistence before deploying.")
    if not (layout.frontend / "index.html").is_file():
        raise DeploymentError("The existing frontend index.html is required for rollback checks.")


def rename_directory(source: Path, destination: Path) -> None:
    if entry_exists(destination):
        raise DeploymentError("Refusing to overwrite an existing transaction directory.")
    source.rename(destination)
    fsync_directory(source.parent)


def deploy(request: Request, layout: Layout, runtime: ServiceRuntime) -> str:
    """Must be called under deployment_lock. All candidates validated BEFORE stop.

    Tests use temporary layouts and fake service control. The CLI always uses fixed
    production paths and actual root ownership/permissions checks.
    """
    request.validate()
    pending = layout.state / "pending.json"
    if entry_exists(pending):
        raise DeploymentError("An interrupted deployment needs administrator recovery; pending.json is retained.")
    check_layout(layout)
    runtime.preflight(layout)
    previous_index = file_digest(layout.frontend / "index.html")
    runtime.wait_healthy(previous_index)
    attempt = uuid.uuid4().hex
    workspace = layout.state / attempt
    workspace.mkdir(mode=0o700)
    pairs = [("api", layout.api, runtime.api_gid, request.api_digest),
             ("frontend", layout.frontend, runtime.frontend_gid, request.frontend_digest)]
    components = []
    for kind, live, group_id, digest in pairs:
        snapshot = workspace / f"{kind}.zip"
        snapshot_upload(layout.uploads / request.commit / f"{kind}.zip", snapshot, digest, runtime.upload_uid)
        candidate = live.parent / f".{live.name}-next-{attempt}"
        extract_archive(snapshot, candidate, kind)
        set_runtime_permissions(candidate, group_id)
        components.append({"live": live, "candidate": candidate,
                           "backup": live.parent / f".{live.name}-backup-{attempt}",
                           "failed": live.parent / f".{live.name}-failed-{attempt}"})
    new_index = file_digest(components[1]["candidate"] / "index.html")
    record = {"attempt": attempt, "commit": request.commit,
              "api_sha256": request.api_digest, "frontend_sha256": request.frontend_digest,
              "phase": "prepared", "components": [{key: str(value) for key, value in component.items()}
                                                      for component in components]}

    def save(phase: str) -> None:
        record["phase"] = phase
        write_record(workspace / "transaction.json", record)
        write_record(pending, record)

    def clear_pending() -> None:
        pending.unlink()
        fsync_directory(layout.state)

    save("prepared")  # Crash/kill after this point blocks the next deployment.
    try:
        runtime.stop()
        save("switching")
        for component in components:
            rename_directory(component["live"], component["backup"])
            rename_directory(component["candidate"], component["live"])
        runtime.start()
        runtime.wait_healthy(new_index)
        save("committed")
        clear_pending()
        return attempt
    except (Exception, KeyboardInterrupt) as error:
        try:
            if any(entry_exists(component["backup"]) for component in components):
                runtime.stop()  # Never rename running new code if stop fails.
                for component in reversed(components):
                    if entry_exists(component["backup"]):
                        if entry_exists(component["live"]):
                            rename_directory(component["live"], component["failed"])
                        rename_directory(component["backup"], component["live"])
            runtime.start()
            runtime.wait_healthy(previous_index)
            save("rolled_back")
            clear_pending()
        except (Exception, KeyboardInterrupt) as recovery_error:
            # Do not delete pending.json or backup/failed directories on uncertain recovery.
            try:
                save("recovery_required")
            except Exception:
                pass  # Existing pending record is more important than a new phase label.
            raise DeploymentError("Rollback could not be verified. Administrator recovery is required; pending.json remains.") from recovery_error
        raise DeploymentError("Deployment failed; previous code was restored and its health checks passed.") from error


USAGE = "Usage: project1-deploy COMMIT_SHA API_ZIP_SHA256 FRONTEND_ZIP_SHA256"


def main(arguments: list[str] | None = None) -> int:
    arguments = sys.argv[1:] if arguments is None else arguments
    if arguments == ["--help"]:
        print(USAGE)
        print("Ubuntu root helper; fixed paths only. No arbitrary commands, migrations, or settings changes.")
        return 0
    if len(arguments) != 3:
        print(USAGE, file=sys.stderr)
        return 2
    try:
        request = Request(*arguments)
        request.validate()
        if sys.platform != "linux" or os.geteuid() != 0:
            raise DeploymentError("Deployment requires Linux and the administrator-installed root helper.")
        os.umask(0o077)
        layout = Layout()
        require_root_directory(layout.state)
        if layout.state.stat().st_mode & 0o077:
            raise DeploymentError("Deployment state directory must be root-only (0700).")
        with deployment_lock(layout.state):
            attempt = deploy(request, layout, ServiceRuntime())
        print(f"Deployed {request.commit}; transaction {attempt}. API and frontend health checks passed.")
        return 0
    except DeploymentError as error:
        print(f"ERROR: {error}", file=sys.stderr)
        return 1
    except Exception as error:
        # Do not expose HTTP bodies, file contents, or subprocess stderr in deployment logs.
        print(f"ERROR: Deployment failed ({type(error).__name__}); inspect root-managed transaction state.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
