"""Fabricated review records/packages only; no network, SQL or production secrets."""

import copy
from datetime import datetime, timedelta, timezone
import io
import json
from pathlib import Path
import socket
import subprocess
import sys
import tempfile
import unittest
from unittest import mock
import urllib.request

SCRIPTS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SCRIPTS))
import migration_approval as approval
import verify_migration_artifact as verify
from test_migration_precheck import FakeClient, IDS, response
from test_manual_cd import RUN_ID, SHA

NOW = datetime(2026, 10, 10, 12, 0, tzinfo=timezone.utc)


def record_fixture(manifest, now=NOW):
    return {"schema": 1, "purpose": "migration-execution-review", "approval_id": "a" * 32,
            "repository": verify.REPOSITORY, "server": "homelab-server", "database": "Project1Db",
            "run_id": RUN_ID, "run_attempt": 1, "commit": SHA,
            "artifact_sha256": manifest["artifact"]["sha256"], "sql_sha256": manifest["sql_sha256"],
            "applied": IDS[:1], "pending": IDS[1:],
            "approved_at": (now - timedelta(minutes=2)).isoformat(),
            "expires_at": (now + timedelta(minutes=30)).isoformat(),
            "backup": {"sha256": "b" * 64, "completed_at": (now - timedelta(minutes=10)).isoformat(),
                       "verified": True},
            "confirmations": {name: True for name in approval.CONFIRMATIONS}}


def status_fixture(now=NOW):
    status = json.loads(response(IDS[:1]))
    status["checked_at"] = (now - timedelta(minutes=1)).isoformat()
    return status


class ApprovalTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.directory = self.root / "verified"
        # Only fake GitHub responses and synthetic ZIP contents are used.
        for target in ("subprocess.run", "socket.create_connection", "urllib.request.urlopen"):
            patch = mock.patch(target, side_effect=AssertionError("External access is forbidden in offline tests."))
            patch.start()
            self.addCleanup(patch.stop)
        self.manifest = verify.verify(FakeClient(), RUN_ID, SHA, self.directory, 1)
        self.record = record_fixture(self.manifest)
        self.status = status_fixture()

    def validate(self, record=None, status=None, now=NOW, manifest=None):
        return approval.validate_approval(json.dumps(self.record if record is None else record),
                                          self.manifest if manifest is None else manifest,
                                          json.dumps(self.status if status is None else status), now=now)

    def test_valid_record_is_not_execution_authorization_and_changes_nothing(self):
        before = {file.name: file.read_bytes() for file in self.directory.iterdir()}
        record, manifest, status = copy.deepcopy((self.record, self.manifest, self.status))
        result = self.validate()
        self.assertTrue(result["record_valid"])
        self.assertFalse(result["execution_authorized"])
        self.assertFalse(result["sql_executed"])
        self.assertEqual(result["pending_count"], 1)
        self.assertEqual((self.record, self.manifest, self.status), (record, manifest, status))
        self.assertEqual(before, {file.name: file.read_bytes() for file in self.directory.iterdir()})
        # Replay prevention is intentionally NOT claimed by this offline validator.
        self.assertEqual(self.validate(), result)

    def test_wrong_version_hash_attempt_and_target_are_refused(self):
        for field, value in (("commit", "d" * 40), ("run_id", RUN_ID + 1), ("run_attempt", 2),
                             ("sql_sha256", "c" * 64), ("artifact_sha256", "c" * 64),
                             ("repository", "other/repo"), ("server", "other"),
                             ("database", "master"), ("purpose", "deploy"), ("schema", 2)):
            bad = copy.deepcopy(self.record)
            bad[field] = value
            with self.subTest(field=field), self.assertRaises(verify.VerificationError):
                self.validate(bad)

    def test_missing_unknown_fields_are_refused(self):
        for field in self.record:
            bad = copy.deepcopy(self.record)
            del bad[field]
            with self.subTest(field=field), self.assertRaises(verify.VerificationError):
                self.validate(bad)
        for field in ("sql", "connection", "password", "backup_path", "command"):
            bad = {**self.record, field: "not-supported"}
            with self.subTest(field=field), self.assertRaises(verify.VerificationError):
                self.validate(bad)

    def test_duplicate_keys_and_non_object_json_are_refused(self):
        for body in ('{"schema":1,"schema":1}', '[]', 'null', 'true'):
            with self.subTest(body=body), self.assertRaises(verify.VerificationError):
                approval.validate_approval(body, self.manifest, json.dumps(self.status), now=NOW)
        body = json.dumps(self.status)[:-1] + ',"schema":1}'
        with self.assertRaises(verify.VerificationError):
            approval.validate_approval(json.dumps(self.record), self.manifest, body, now=NOW)

    def test_types_identifiers_and_digests_are_strict(self):
        for field, values in {
            "schema": [True, "1", 1.0], "run_id": [True, str(RUN_ID), 0, -1, 10**20, None],
            "run_attempt": [True, "1", 1.0], "approval_id": ["../bad", "A" * 32, "a" * 31],
            "commit": [None, "D" * 40, "d" * 39],
            "sql_sha256": [None, "C" * 64, "c" * 63], "artifact_sha256": ["sha256:" + "c" * 64],
        }.items():
            for value in values:
                bad = {**self.record, field: value}
                with self.subTest(field=field, value=value), self.assertRaises(verify.VerificationError):
                    self.validate(bad)

    def test_review_confirmations_must_be_literal_true(self):
        for field in approval.CONFIRMATIONS:
            for value in (False, 1, "true", None):
                bad = copy.deepcopy(self.record)
                bad["confirmations"][field] = value
                with self.subTest(field=field, value=value), self.assertRaises(verify.VerificationError):
                    self.validate(bad)
        for value in ({}, True, {**self.record["confirmations"], "other": True}):
            with self.subTest(value=value), self.assertRaises(verify.VerificationError):
                self.validate({**self.record, "confirmations": value})

    def test_backup_missing_false_wrong_digest_stale_or_future_is_refused(self):
        for value in ({}, None, True, {**self.record["backup"], "path": "/anywhere"}):
            with self.subTest(value=value), self.assertRaises(verify.VerificationError):
                self.validate({**self.record, "backup": value})
        for field, value in (("verified", False), ("verified", 1), ("verified", "true"),
                             ("sha256", "not-a-hash"),
                             ("completed_at", (NOW - timedelta(hours=24, seconds=1)).isoformat()),
                             ("completed_at", (NOW - timedelta(minutes=1)).isoformat())):
            bad = copy.deepcopy(self.record)
            bad["backup"][field] = value
            with self.subTest(field=field, value=value), self.assertRaises(verify.VerificationError):
                self.validate(bad)

    def test_expired_future_and_excessive_lifetime_records_are_refused(self):
        for times in ((NOW - timedelta(minutes=30), NOW), (NOW + timedelta(seconds=1), NOW + timedelta(minutes=30)),
                      (NOW - timedelta(minutes=2), NOW - timedelta(minutes=2)),
                      (NOW - timedelta(minutes=2), NOW + timedelta(minutes=59))):
            bad = {**self.record, "approved_at": times[0].isoformat(), "expires_at": times[1].isoformat()}
            with self.subTest(times=times), self.assertRaises(verify.VerificationError):
                self.validate(bad)

    def test_timestamp_format_and_clock_are_strict(self):
        for value in ("2026-10-10", "2026-10-10T12:00:00", "2026-10-10T20:00:00+08:00",
                      "2026-02-30T12:00:00Z", None, True, "2026-10-10T12:00:00Z\n"):
            for field in ("approved_at", "expires_at"):
                with self.subTest(field=field, value=value), self.assertRaises(verify.VerificationError):
                    self.validate({**self.record, field: value})
        for now in (NOW.replace(tzinfo=None), NOW.astimezone(timezone(timedelta(hours=8))), None):
            with self.subTest(now=now), self.assertRaises(verify.VerificationError):
                self.validate(now=now)
        self.assertEqual(approval.timestamp("2026-10-10T12:00:00Z"), NOW)

    def test_history_changed_gap_unknown_reordered_and_empty_pending_are_refused(self):
        for ids in (IDS, [], [IDS[1]], IDS[::-1], IDS + IDS[-1:], ["20260101000000_Unknown"]):
            bad = {**self.status, "migrations": ids}
            with self.subTest(ids=ids), self.assertRaises(verify.VerificationError):
                self.validate(status=bad)
        for field, value in (("pending", []), ("pending", IDS), ("pending", IDS[1:] * 2),
                             ("applied", IDS), ("applied", ["../bad"])):
            with self.subTest(field=field), self.assertRaises(verify.VerificationError):
                self.validate({**self.record, field: value})

    def test_snapshot_freshness_identity_and_exact_schema_are_checked(self):
        for field, value in (("checked_at", (NOW - timedelta(minutes=5, seconds=1)).isoformat()),
                             ("checked_at", (NOW + timedelta(seconds=1)).isoformat()),
                             ("database", "master"), ("server", "other"), ("login", "sa"),
                             ("schema", True), ("extra", "unsupported")):
            with self.subTest(field=field), self.assertRaises(verify.VerificationError):
                self.validate(status={**self.status, field: value})

    def test_time_boundaries_and_empty_applied_history(self):
        bad = copy.deepcopy(self.record)
        bad["approved_at"] = NOW.isoformat()
        bad["expires_at"] = (NOW + timedelta(hours=1)).isoformat()
        bad["backup"]["completed_at"] = (NOW - timedelta(hours=24)).isoformat()
        status = {**self.status, "checked_at": (NOW - timedelta(minutes=5)).isoformat()}
        self.assertTrue(self.validate(bad, status)["record_valid"])
        bad["applied"], bad["pending"] = [], IDS
        self.assertEqual(self.validate(bad, {**status, "migrations": []})["pending_count"], 2)

    def test_direct_validation_bounds_inputs(self):
        for body in (b"", "x" * (approval.MAX_RECORD_BYTES + 1), None):
            with self.subTest(body=str(body)[:10]), self.assertRaises(verify.VerificationError):
                approval.validate_approval(body, self.manifest, json.dumps(self.status), now=NOW)
            with self.subTest(status=str(body)[:10]), self.assertRaises(verify.VerificationError):
                approval.validate_approval(json.dumps(self.record), self.manifest, body, now=NOW)

    def cli_files(self):
        now = datetime.now(timezone.utc)
        record, status = self.root / "approval.json", self.root / "status.json"
        record.write_text(json.dumps(record_fixture(self.manifest, now)), encoding="utf-8")
        status.write_text(json.dumps(status_fixture(now)), encoding="utf-8")
        return record, status

    def cli(self, record, status):
        output = io.StringIO()
        with mock.patch("sys.stdout", output):
            code = approval.main(["--directory", str(self.directory), "--approval", str(record), "--status", str(status)])
        return code, output.getvalue()

    def test_cli_is_offline_read_only_and_does_not_echo_inputs(self):
        record, status = self.cli_files()
        before = {str(path): path.read_bytes() for path in self.root.rglob("*") if path.is_file()}
        code, output = self.cli(record, status)
        self.assertEqual(code, 0)
        self.assertIn('"execution_authorized":false', output)
        self.assertIn('"sql_executed":false', output)
        self.assertNotIn(self.manifest["sql_sha256"], output)
        self.assertNotIn("completed_at", output)
        self.assertEqual(before, {str(path): path.read_bytes() for path in self.root.rglob("*") if path.is_file()})

    def test_cli_rechecks_zip_before_record_comparison(self):
        record, status = self.cli_files()
        with (self.directory / "migrations.zip").open("ab") as file:
            file.write(b"tampered")
        with mock.patch.object(approval, "validate_approval") as validate:
            self.assertEqual(self.cli(record, status)[0], 1)
        validate.assert_not_called()

    def test_cli_json_errors_are_redacted(self):
        record, status = self.cli_files()
        for body in ("private-input-do-not-print", '{"schema":1,"schema":1}', '{"password":"private-input-do-not-print"}', "\ufeff{}"):
            record.write_text(body, encoding="utf-8")
            code, output = self.cli(record, status)
            self.assertEqual(code, 1)
            self.assertNotIn("private-input-do-not-print", output)
            self.assertNotIn(str(record), output)

    def test_cli_refuses_unknown_execute_option(self):
        with mock.patch("sys.stderr", io.StringIO()), self.assertRaises(SystemExit) as error:
            approval.main(["--execute"])
        self.assertEqual(error.exception.code, 2)

    def test_file_bounds_directory_and_missing_file(self):
        path = self.root / "input.json"
        for body in (b"", b"x" * (approval.MAX_RECORD_BYTES + 1)):
            path.write_bytes(body)
            with self.assertRaises(verify.VerificationError):
                approval.read_record(path)
        with self.assertRaises(verify.VerificationError):
            approval.read_record(self.root)
        with self.assertRaises(FileNotFoundError):
            approval.read_record(self.root / "missing.json")

    def test_untrusted_file_types_and_hardlinks_are_refused_before_open(self):
        path = self.root / "input.json"
        for mode, links in ((0o120777, 1), (0o010600, 1), (0o100600, 2)):
            info = mock.Mock(st_mode=mode, st_nlink=links, st_size=10)
            with mock.patch.object(Path, "lstat", return_value=info), mock.patch.object(Path, "open") as opened:
                with self.assertRaises(verify.VerificationError):
                    approval.read_record(path)
            opened.assert_not_called()


if __name__ == "__main__":
    unittest.main()
