#!/usr/bin/env python3
"""Supervise a Quick Tunnel and publish its reachable URL to the project README.

Python standard library only. The GitHub credential is read from systemd's
LoadCredential directory; it is never passed on the command line or to cloudflared.
"""

import base64
import binascii
from dataclasses import dataclass
import json
import logging
import os
from pathlib import Path
import queue
import re
import signal
import subprocess
import threading
import time
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode
from urllib.request import HTTPRedirectHandler, ProxyHandler, Request, build_opener


LOG = logging.getLogger("project1-demo")
START = "<!-- demo-link:start -->"
END = "<!-- demo-link:end -->"
TUNNEL_URL = re.compile(r"https://[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.trycloudflare\.com")
LOG_URL = re.compile(TUNNEL_URL.pattern + r"(?![a-zA-Z0-9./:_-])")
ORIGIN = "http://127.0.0.1:80"
HTTP_TIMEOUT = 10
MAX_RESPONSE = 2 * 1024 * 1024


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        # Never forward a GitHub Authorization header to another endpoint.
        # Health probes must reach Project1 itself, rather than a login redirect.
        return None


# Direct requests: local probes must not go through an environment HTTP proxy.
HTTP = build_opener(ProxyHandler({}), NoRedirect())


def read_response(request):
    with HTTP.open(request, timeout=HTTP_TIMEOUT) as response:
        data = response.read(MAX_RESPONSE + 1)
        if len(data) > MAX_RESPONSE:
            raise ValueError("HTTP response exceeded the size limit")
        return data


def site_ready(url):
    """Check both the website and the specific Project1 API, without a token."""
    try:
        read_response(Request(url + "/", headers={"Cache-Control": "no-cache"}))
        health = json.loads(read_response(Request(
            url + "/api/health", headers={"Cache-Control": "no-cache"}
        )))
        return (
            isinstance(health, dict)
            and health.get("status") == "ok"
            and health.get("message") == "Project1 API is running"
        )
    except HTTPError as error:
        error.close()
        return False
    except (OSError, URLError, ValueError):
        return False


def replace_demo_link(readme, url):
    if not TUNNEL_URL.fullmatch(url):
        raise ValueError("Expected an HTTPS trycloudflare.com URL")
    if readme.count(START) != 1 or readme.count(END) != 1:
        raise ValueError("README must contain exactly one pair of demo-link markers")
    start = readme.index(START) + len(START)
    end = readme.index(END)
    if end < start:
        raise ValueError("README demo-link markers are in the wrong order")
    newline = "\r\n" if "\r\n" in readme else "\n"
    return readme[:start] + newline + f"[Open Live Demo]({url})" + newline + readme[end:]


@dataclass(frozen=True)
class Settings:
    repository: str = "shunbin2310/Project1"
    branch: str = "main"
    interval: float = 30
    startup_timeout: float = 180

    @classmethod
    def from_environment(cls):
        repository = os.environ.get("GITHUB_REPOSITORY", cls.repository)
        branch = os.environ.get("GITHUB_BRANCH", cls.branch)
        if not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository):
            raise ValueError("GITHUB_REPOSITORY must be owner/repository")
        if not branch or any(ord(char) < 32 for char in branch):
            raise ValueError("GITHUB_BRANCH must be a branch name")
        return cls(repository=repository, branch=branch)


class GitHub:
    def __init__(self, settings, token):
        self.settings = settings
        self.token = token
        # Match the tracked filename exactly: GitHub paths are case-sensitive.
        self.endpoint = f"https://api.github.com/repos/{settings.repository}/contents/Readme.md"

    def request(self, method, url, payload=None):
        data = None if payload is None else json.dumps(payload).encode("utf-8")
        return json.loads(read_response(Request(
            url,
            data=data,
            method=method,
            headers={
                "Authorization": f"Bearer {self.token}",
                "Accept": "application/vnd.github+json",
                "Content-Type": "application/json",
                "User-Agent": "project1-homelab-demo",
                "X-GitHub-Api-Version": "2026-03-10",
            },
        )))

    def publish(self, url):
        # Read the current remote file and SHA on EVERY attempt, including retries.
        # A concurrent edit produces a conflict; the next cycle fetches it again.
        if not TUNNEL_URL.fullmatch(url):
            raise ValueError("Refusing to publish an invalid tunnel URL")
        current = self.request("GET", self.endpoint + "?" + urlencode({"ref": self.settings.branch}))
        if current.get("type") != "file" or current.get("encoding") != "base64":
            raise ValueError("GitHub README response is not a base64-encoded file")
        encoded = "".join(current["content"].split())
        original = base64.b64decode(encoded, validate=True).decode("utf-8")
        updated = replace_demo_link(original, url)
        if updated == original:
            return False
        self.request("PUT", self.endpoint, {
            "message": "docs: update live demo link [skip ci]",
            "content": base64.b64encode(updated.encode("utf-8")).decode("ascii"),
            "sha": current["sha"],
            "branch": self.settings.branch,
        })
        return True


def forward_output(stream, urls):
    try:
        for line in stream:
            LOG.info("cloudflared: %s", line.rstrip())
            match = LOG_URL.search(line)
            if match:
                # Only the latest URL matters. Do not retain an unbounded log queue.
                try:
                    urls.get_nowait()
                except queue.Empty:
                    pass
                urls.put_nowait(match.group())
    finally:
        stream.close()


def stop_child(process):
    if process.poll() is None:
        process.terminate()
        try:
            process.wait(timeout=8)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=2)


def serve(settings, github, stop, health=site_ready, process_factory=subprocess.Popen):
    LOG.info("Waiting for the local website and Project1 API")
    while not stop.is_set():
        if health(ORIGIN):
            break
        if stop.wait(settings.interval):
            return 0
    if stop.is_set():
        return 0

    # The child receives no GitHub token, credentials directory or unrelated env.
    process = process_factory(
        ["/usr/bin/cloudflared", "tunnel", "--no-autoupdate", "--url", ORIGIN],
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        encoding="utf-8",
        errors="replace",
        bufsize=1,
        env={"PATH": "/usr/bin:/bin", "HOME": "/nonexistent", "LANG": "C.UTF-8"},
    )
    urls = queue.Queue(maxsize=1)
    reader = threading.Thread(target=forward_output, args=(process.stdout, urls), daemon=True)
    reader.start()
    started_at = time.monotonic()
    candidate = None
    published = None
    next_check = 0
    try:
        while not stop.is_set():
            if process.poll() is not None:
                LOG.error("cloudflared exited; systemd will start a new tunnel")
                return 1
            try:
                candidate = urls.get_nowait()
                LOG.info("Detected demo URL: %s", candidate)
                next_check = 0
            except queue.Empty:
                pass

            now = time.monotonic()
            if candidate is None and now - started_at > settings.startup_timeout:
                LOG.error("No Quick Tunnel URL appeared; restarting through systemd")
                return 1
            if candidate and candidate != published and now >= next_check:
                if health(ORIGIN) and health(candidate) and process.poll() is None and not stop.is_set():
                    try:
                        changed = github.publish(candidate)
                        # Do not remember failed attempts: retry with a fresh remote SHA.
                        published = candidate
                        LOG.info("README %s: %s", "updated" if changed else "already current", candidate)
                    except HTTPError as error:
                        # Never print response bodies, request headers or the token.
                        LOG.warning(
                            "GitHub HTTP %s; retrying. Check token expiry, Contents write permission, "
                            "branch rules, and that README is published with both markers.", error.code
                        )
                        error.close()
                    except (OSError, URLError, ValueError, KeyError, TypeError, binascii.Error) as error:
                        LOG.warning("README update failed (%s); will retry", type(error).__name__)
                else:
                    LOG.info("Waiting for the local and public website/API before publishing")
                next_check = time.monotonic() + settings.interval
            # Cloudflared handles transient reconnects itself. Do not replace a
            # working URL or produce daily commits while the URL stays the same.
            stop.wait(min(1, settings.interval))
        return 0
    finally:
        stop_child(process)
        reader.join(timeout=2)


def main():
    logging.basicConfig(level=logging.INFO, format="%(levelname)s %(message)s")
    stop = threading.Event()
    for signum in (signal.SIGINT, signal.SIGTERM):
        signal.signal(signum, lambda *_: stop.set())
    try:
        settings = Settings.from_environment()
        credential_directory = os.environ.get("CREDENTIALS_DIRECTORY")
        if not credential_directory:
            raise ValueError("Run this script using project1-demo.service (LoadCredential required)")
        token = (Path(credential_directory) / "github-token").read_text(encoding="utf-8").strip()
        if not token or any(char.isspace() for char in token):
            raise ValueError("The GitHub credential must contain one non-empty token")
        return serve(settings, GitHub(settings, token), stop)
    except (OSError, ValueError) as error:
        LOG.error("Startup failed (%s); check service configuration and credential file", type(error).__name__)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
