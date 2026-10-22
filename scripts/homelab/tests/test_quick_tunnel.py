"""Behavior checks without Cloudflare/GitHub credentials or external requests."""

import base64
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import sys
import threading
import unittest
from unittest.mock import Mock, patch
from urllib.error import HTTPError, URLError


MODULE_PATH = Path(__file__).resolve().parents[1] / "quick_tunnel.py"
SPEC = importlib.util.spec_from_file_location("quick_tunnel", MODULE_PATH)
demo = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = demo
SPEC.loader.exec_module(demo)

URL = "https://project1-demo-test.trycloudflare.com"
README = "# Project1\n\n" + demo.START + "\npending\n" + demo.END + "\n\nAvailability\n"


def remote_file(text, sha="first-sha"):
    return {
        "type": "file", "encoding": "base64", "sha": sha,
        "content": base64.b64encode(text.encode()).decode(),
    }


class ReadmeTests(unittest.TestCase):
    def test_updates_only_marked_region_and_is_idempotent(self):
        expected = README.replace("pending", f"[Open Live Demo]({URL})")
        updated = demo.replace_demo_link(README, URL)
        self.assertEqual(updated, expected)
        self.assertEqual(demo.replace_demo_link(updated, URL), updated)

    def test_preserves_crlf_and_unicode(self):
        original = (README + "中文说明\n").replace("\n", "\r\n")
        self.assertEqual(
            demo.replace_demo_link(original, URL),
            original.replace("pending", f"[Open Live Demo]({URL})"),
        )

    def test_refuses_missing_duplicate_and_reversed_markers(self):
        for text in ("no markers", README + demo.START, README + demo.END, demo.END + demo.START):
            with self.subTest(text=text), self.assertRaises(ValueError):
                demo.replace_demo_link(text, URL)

    def test_refuses_links_outside_the_expected_hostname(self):
        for url in (
            "http://example.trycloudflare.com", "https://trycloudflare.com",
            URL + ".example.org", URL + "/path", URL + ":443", URL + ") injected",
            "https://user:password@example.trycloudflare.com", "https://example.com",
        ):
            with self.subTest(url=url), self.assertRaises(ValueError):
                demo.replace_demo_link(README, url)


class GitHubTests(unittest.TestCase):
    def setUp(self):
        self.client = demo.GitHub(demo.Settings(branch="demo links"), "test-token-not-real")
        self.client.request = Mock()

    def test_uses_remote_sha_branch_and_skip_ci(self):
        self.client.request.side_effect = [remote_file(README), {}]
        self.assertTrue(self.client.publish(URL))
        get_call, put_call = self.client.request.call_args_list
        self.assertTrue(get_call.args[1].endswith("?ref=demo+links"))
        method, endpoint, payload = put_call.args
        self.assertEqual(method, "PUT")
        self.assertEqual(endpoint, "https://api.github.com/repos/shunbin2310/Project1/contents/Readme.md")
        self.assertEqual(payload["sha"], "first-sha")
        self.assertEqual(payload["branch"], "demo links")
        self.assertIn("[skip ci]", payload["message"])
        self.assertEqual(base64.b64decode(payload["content"]).decode(), demo.replace_demo_link(README, URL))

    def test_does_not_commit_an_unchanged_link(self):
        self.client.request.return_value = remote_file(demo.replace_demo_link(README, URL))
        self.assertFalse(self.client.publish(URL))
        self.client.request.assert_called_once()

    def test_conflict_retry_refetches_and_preserves_concurrent_edit(self):
        concurrent = README + "Another author's new paragraph.\n"
        conflict = HTTPError(self.client.endpoint, 409, "Conflict", {}, None)
        self.client.request.side_effect = [
            remote_file(README), conflict, remote_file(concurrent, "new-sha"), {},
        ]
        with self.assertRaises(HTTPError):
            self.client.publish(URL)
        self.assertTrue(self.client.publish(URL))
        payload = self.client.request.call_args_list[-1].args[2]
        self.assertEqual(payload["sha"], "new-sha")
        self.assertIn("Another author's new paragraph.", base64.b64decode(payload["content"]).decode())

    def test_invalid_url_and_missing_markers_cannot_write(self):
        with self.assertRaises(ValueError):
            self.client.publish("https://example.com")
        self.client.request.assert_not_called()
        self.client.request.return_value = remote_file("README without markers")
        with self.assertRaises(ValueError):
            self.client.publish(URL)
        self.client.request.assert_called_once()

    def test_token_is_used_only_on_the_github_api_request(self):
        client = demo.GitHub(demo.Settings(), "test-token-not-real")
        with patch.object(demo, "read_response", return_value=b"{}") as read:
            client.request("GET", client.endpoint)
        request = read.call_args.args[0]
        self.assertEqual(request.get_header("Authorization"), "Bearer test-token-not-real")
        self.assertEqual(request.get_header("X-github-api-version"), "2026-03-10")
        self.assertEqual(request.host, "api.github.com")


class HealthTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *_):
                pass

            def do_GET(self):
                cls.headers_seen.append(dict(self.headers))
                if cls.mode == "redirect":
                    self.send_response(302)
                    self.send_header("Location", "/login")
                    self.end_headers()
                    return
                self.send_response(503 if cls.mode == "down" else 200)
                self.end_headers()
                if self.path == "/api/health":
                    if cls.mode == "wrong-app":
                        self.wfile.write(b'{"status":"ok","message":"Another app"}')
                    elif cls.mode == "html":
                        self.wfile.write(b"<html>Login</html>")
                    else:
                        self.wfile.write(b'{"status":"ok","message":"Project1 API is running"}')
                else:
                    self.wfile.write(b"<html>Project1</html>")

        cls.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        cls.thread = threading.Thread(target=cls.server.serve_forever, daemon=True)
        cls.thread.start()
        cls.url = f"http://127.0.0.1:{cls.server.server_port}"

    @classmethod
    def tearDownClass(cls):
        cls.server.shutdown()
        cls.server.server_close()
        cls.thread.join(timeout=2)

    def test_requires_real_project1_health_and_sends_no_authorization(self):
        for mode, expected in (("ok", True), ("down", False), ("wrong-app", False), ("html", False), ("redirect", False)):
            with self.subTest(mode=mode):
                type(self).mode = mode
                type(self).headers_seen = []
                self.assertEqual(demo.site_ready(self.url), expected)
                self.assertTrue(all("Authorization" not in headers for headers in self.headers_seen))


class SupervisorTests(unittest.TestCase):
    def test_waits_for_public_health_retries_github_and_stops_child(self):
        stop = threading.Event()
        settings = demo.Settings(interval=0.01, startup_timeout=5)
        counts = {"public": 0, "publish": 0}
        client = Mock()

        def health(url):
            if url == demo.ORIGIN:
                return True
            counts["public"] += 1
            return counts["public"] > 1

        def publish(url):
            self.assertEqual(url, URL)
            self.assertGreater(counts["public"], 1)
            counts["publish"] += 1
            if counts["publish"] == 1:
                raise URLError("simulated GitHub outage")
            stop.set()
            return True

        client.publish.side_effect = publish
        children = []

        def process_factory(command, **kwargs):
            self.assertEqual(command[-1], demo.ORIGIN)
            self.assertNotIn("CREDENTIALS_DIRECTORY", kwargs["env"])
            self.assertNotIn("GITHUB_TOKEN", kwargs["env"])
            process = subprocess.Popen(
                [sys.executable, "-u", "-c", f"import time; print({URL!r}, flush=True); time.sleep(10)"],
                **kwargs,
            )
            children.append(process)
            return process

        watchdog = threading.Timer(5, stop.set)
        watchdog.start()
        try:
            with self.assertLogs(demo.LOG, level="INFO"):
                self.assertEqual(demo.serve(settings, client, stop, health, process_factory), 0)
            self.assertEqual(counts["publish"], 2)
            self.assertIsNotNone(children[0].poll())
        finally:
            watchdog.cancel()
            for child in children:
                demo.stop_child(child)

    def test_cloudflared_exit_requests_systemd_restart(self):
        client = Mock()
        process = Mock()
        process.stdout = io.StringIO("")
        process.poll.return_value = 1
        with self.assertLogs(demo.LOG, level="ERROR"):
            result = demo.serve(demo.Settings(), client, threading.Event(), lambda _: True, lambda *_, **__: process)
        self.assertEqual(result, 1)
        client.publish.assert_not_called()

    def test_missing_startup_url_stops_child_for_systemd_restart(self):
        client = Mock()
        process = Mock()
        process.stdout = io.StringIO("No URL in this startup output\n")
        process.poll.return_value = None
        with self.assertLogs(demo.LOG, level="INFO"):
            result = demo.serve(
                demo.Settings(startup_timeout=0), client, threading.Event(),
                lambda _: True, lambda *_, **__: process,
            )
        self.assertEqual(result, 1)
        process.terminate.assert_called_once()
        client.publish.assert_not_called()

    def test_shutdown_while_waiting_never_starts_a_tunnel(self):
        stop = threading.Event()
        process_factory = Mock()

        def not_ready(_):
            stop.set()
            return False

        with self.assertLogs(demo.LOG, level="INFO"):
            self.assertEqual(demo.serve(demo.Settings(), Mock(), stop, not_ready, process_factory), 0)
        process_factory.assert_not_called()


if __name__ == "__main__":
    unittest.main()
