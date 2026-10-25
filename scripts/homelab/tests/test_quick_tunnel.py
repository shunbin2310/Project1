"""Behavior checks without Cloudflare/GitHub credentials or external requests."""

import base64
from html.parser import HTMLParser
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import importlib.util
import io
import json
import os
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
OLD_URL = "https://previous-demo-test.trycloudflare.com"


def remote_file(text, sha="first-sha"):
    return {
        "type": "file", "encoding": "base64", "sha": sha,
        "content": base64.b64encode(text.encode()).decode(),
    }


def http_error(code, message="Error"):
    return HTTPError("https://api.github.com/test", code, message, {}, io.BytesIO())


class PageTests(unittest.TestCase):
    def test_automatic_redirect_and_clickable_fallback_agree(self):
        class Elements(HTMLParser):
            def __init__(self):
                super().__init__()
                self.tags = []

            def handle_starttag(self, tag, attrs):
                self.tags.append((tag, dict(attrs)))

        page = demo.redirect_page(URL)
        parser = Elements()
        parser.feed(page)
        refresh = [attrs for tag, attrs in parser.tags if tag == "meta" and attrs.get("http-equiv") == "refresh"]
        anchors = [attrs for tag, attrs in parser.tags if tag == "a"]
        self.assertEqual(refresh, [{"http-equiv": "refresh", "content": f"0; url={URL}"}])
        self.assertEqual(anchors, [{"href": URL}])
        self.assertIn(f"window.location.replace({json.dumps(URL)})", page)
        self.assertEqual(page.count(demo.PAGE_MARKER), 1)

    def test_refuses_external_urls_paths_and_html_or_script_injection(self):
        for url in (
            "http://example.trycloudflare.com", "https://trycloudflare.com",
            URL + ".example.org", URL + "/path", URL + ":443", URL + '"><script>alert(1)</script>',
            "https://user:password@example.trycloudflare.com", "https://example.com",
        ):
            with self.subTest(url=url), self.assertRaises(ValueError):
                demo.redirect_page(url)


class GitHubTests(unittest.TestCase):
    def setUp(self):
        self.client = demo.GitHub(demo.Settings(), "test-token-not-real")
        self.client.request = Mock()

    def test_updates_only_index_on_demo_links_with_fresh_sha_and_allows_pages_build(self):
        self.client.request.side_effect = [remote_file(demo.redirect_page(OLD_URL)), {}]
        self.assertTrue(self.client.publish(URL))
        get_call, put_call = self.client.request.call_args_list
        self.assertTrue(get_call.args[1].endswith("?ref=demo-links"))
        method, endpoint, payload = put_call.args
        self.assertEqual(method, "PUT")
        self.assertEqual(endpoint, "https://api.github.com/repos/shunbin2310/PurchasingManagementSystem/contents/index.html")
        self.assertEqual(payload["sha"], "first-sha")
        self.assertEqual(payload["branch"], "demo-links")
        self.assertNotIn("[skip ci]", payload["message"])
        self.assertEqual(base64.b64decode(payload["content"]).decode(), demo.redirect_page(URL))

    def test_does_not_commit_an_unchanged_link(self):
        self.client.request.return_value = remote_file(demo.redirect_page(URL))
        self.assertFalse(self.client.publish(URL))
        self.client.request.assert_called_once()

    def test_conflict_retry_refetches_sha_and_never_resets_branch_history(self):
        self.client.request.side_effect = [
            remote_file(demo.redirect_page(OLD_URL)), http_error(409),
            remote_file(demo.redirect_page(OLD_URL), "new-sha"), {},
        ]
        with self.assertRaises(HTTPError):
            self.client.publish(URL)
        self.assertTrue(self.client.publish(URL))
        calls = self.client.request.call_args_list
        self.assertEqual([call.args[0] for call in calls], ["GET", "PUT", "GET", "PUT"])
        self.assertEqual(calls[-1].args[2]["sha"], "new-sha")
        self.assertTrue(all("/contents/index.html" in call.args[1] for call in calls))

    def test_bootstrap_creates_orphan_branch_containing_only_redirect_and_nojekyll(self):
        self.client.request.side_effect = [
            http_error(404), http_error(404), {"sha": "tree-sha"}, {"sha": "commit-sha"}, {},
        ]
        self.assertTrue(self.client.publish(URL))
        calls = self.client.request.call_args_list
        self.assertEqual([call.args[0] for call in calls], ["GET", "GET", "POST", "POST", "POST"])
        tree = calls[2].args[2]
        self.assertEqual({entry["path"] for entry in tree["tree"]}, {"index.html", ".nojekyll"})
        self.assertNotIn("base_tree", tree)
        self.assertTrue(all(entry["type"] == "blob" for entry in tree["tree"]))
        self.assertEqual(tree["tree"][0]["content"], demo.redirect_page(URL))
        commit = calls[3].args[2]
        self.assertEqual(commit["parents"], [])
        self.assertEqual(commit["tree"], "tree-sha")
        self.assertEqual(calls[4].args[2], {"ref": "refs/heads/demo-links", "sha": "commit-sha"})
        self.assertNotIn("[skip ci]", commit["message"])
        self.assertFalse(any("PATCH" == call.args[0] for call in calls))

    def test_existing_branch_without_index_is_not_replaced(self):
        self.client.request.side_effect = [http_error(404), {"ref": "refs/heads/demo-links"}]
        with self.assertRaisesRegex(ValueError, "already exists"):
            self.client.publish(URL)
        self.assertEqual(self.client.request.call_count, 2)
        self.assertTrue(all(call.args[0] == "GET" for call in self.client.request.call_args_list))

    def test_branch_creation_race_recovers_by_reading_existing_file_next_cycle(self):
        self.client.request.side_effect = [
            http_error(404), http_error(404), {"sha": "tree"}, {"sha": "commit"}, http_error(422),
            remote_file(demo.redirect_page(URL)),
        ]
        with self.assertRaises(HTTPError):
            self.client.publish(URL)
        self.assertFalse(self.client.publish(URL))
        self.assertEqual(self.client.request.call_args_list[-1].args[0], "GET")

    def test_unrelated_page_and_duplicate_markers_are_not_overwritten(self):
        for original in ("Unrelated website", demo.redirect_page(OLD_URL) + demo.PAGE_MARKER):
            with self.subTest(original=original):
                self.client.request.reset_mock()
                self.client.request.return_value = remote_file(original)
                with self.assertRaises(ValueError):
                    self.client.publish(URL)
                self.client.request.assert_called_once()

    def test_invalid_url_cannot_make_any_api_requests(self):
        with self.assertRaises(ValueError):
            self.client.publish("https://example.com")
        self.client.request.assert_not_called()

    def test_non_404_read_error_cannot_initialize_a_branch(self):
        self.client.request.side_effect = http_error(403)
        with self.assertRaises(HTTPError):
            self.client.publish(URL)
        self.client.request.assert_called_once()

    def test_legacy_main_config_is_ignored_and_main_writer_is_rejected(self):
        with patch.dict(os.environ, {"GITHUB_BRANCH": "main"}, clear=True):
            settings = demo.Settings.from_environment()
        self.assertEqual(settings.branch, "demo-links")
        with self.assertRaises(ValueError):
            demo.GitHub(demo.Settings(branch="main"), "test-token-not-real")

    def test_token_is_used_only_on_the_github_api_request(self):
        client = demo.GitHub(demo.Settings(), "test-token-not-real")
        with patch.object(demo, "read_response", return_value=b"{}") as read:
            client.request("GET", client.endpoint)
        request = read.call_args.args[0]
        self.assertEqual(request.get_header("Authorization"), "Bearer test-token-not-real")
        self.assertEqual(request.get_header("X-github-api-version"), "2026-03-10")
        self.assertEqual(request.host, "api.github.com")

    def test_error_logging_redacts_token_and_omits_other_response_fields(self):
        payload = {"message": "Protected branch.\n secret-token", "errors": ["unrelated-private-detail"]}
        stream = io.BytesIO(json.dumps(payload).encode())
        error = HTTPError("https://api.github.com/test", 409, "Conflict", {}, stream)
        message = demo.github_error_message(error, "secret-token")
        self.assertEqual(message, "Protected branch. [REDACTED]")
        self.assertNotIn("unrelated-private-detail", message)
        self.assertTrue(stream.closed)


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
