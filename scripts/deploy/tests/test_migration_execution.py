"""Portable execution ordering/guards. No network, SQL or production files."""

from contextlib import contextmanager
import copy
from datetime import timedelta
import json
import os
from pathlib import Path
import stat
import sys
import unittest
from unittest import mock

from test_migration_approval_ledger import FixtureCase
from test_migration_approval import NOW
from test_migration_precheck import IDS
import migration_approval_ledger as ledger
import migration_execution as execution
from migration_target import PRODUCTION_TARGET, ci_target


class MemoryLedger:
    """Test double only; real Linux durability is tested separately and in CI workflow."""
    target = PRODUCTION_TARGET

    def __init__(self, case):
        self.events = case.events
        self.state = case.registered()
        self.claim_failure = None
        self.finish_failure = False

    def inspect(self):
        return copy.deepcopy(ledger.validate_state(self.state))

    def claim(self, identifier, manifest, status, *, now):
        if self.claim_failure == "before":
            raise OSError("Synthetic persistence failure")
        self.state, receipt = ledger.claim_record(self.state, identifier, manifest, status, now=now)
        self.events.append("claimed")
        if self.claim_failure == "after":
            raise OSError("Synthetic directory flush failure")
        return receipt

    def finish(self, receipt, phase, *, now):
        if self.finish_failure:
            raise OSError("Synthetic finish flush failure")
        self.state = ledger.finish_record(self.state, receipt, phase, now=now)
        self.events.append(phase)


class FakeServices:
    target = PRODUCTION_TARGET

    def __init__(self, case):
        self.case = case
        self.events = case.events
        self.snapshot = json.loads(case.status)
        self.package_value = execution.VerifiedSql.from_directory(case.root / "verified")
        self.clock = NOW
        self.online_count = 0
        self.applied = False
        self.faults = {}
        self.evidence = {**case.record["backup"], "server": self.target.server,
                         "database": self.target.database, "checked_at": NOW.isoformat(),
                         "checksum_valid": True, "restore_verified": True, "copy_only": True}
        del self.evidence["verified"]

    def event(self, name):
        self.events.append(name)
        if name in self.faults:
            raise self.faults[name]

    def now(self):
        return self.clock

    def package(self):
        self.event("package")
        return self.package_value

    def online_check(self, manifest):
        self.online_count += 1
        self.event("online" + str(self.online_count))
        if manifest != self.case.manifest:
            raise ValueError("Changed provenance")

    @contextmanager
    def session(self):
        self.event("locked")
        try:
            yield self
        finally:
            self.event("closed")

    def status(self):
        self.event("history")
        return json.dumps({**self.snapshot, "checked_at": self.clock.isoformat()}).encode()

    def assert_before(self):
        self.event("schema_before")

    def backup_evidence(self, approval):
        self.event("backup")
        return copy.deepcopy(self.evidence)

    def apply(self, sql, sql_sha256):
        self.event("sql")
        self.received_sql = sql
        self.applied = True
        self.snapshot["migrations"] = IDS

    def assert_after(self):
        self.event("schema_after")


class ExecutionTests(FixtureCase):
    def setUp(self):
        super().setUp()
        self.events = []
        self.store = MemoryLedger(self)
        self.services = FakeServices(self)

    def run_execution(self):
        return execution.execute(self.identifier, self.store, self.services)

    def phase(self):
        return self.store.inspect()["entries"][self.identifier]["phase"]

    def test_success_uses_exact_bytes_durable_claim_before_sql_and_completion_under_lock(self):
        result = self.run_execution()
        self.assertEqual(result["phase"], "succeeded")
        self.assertTrue(result["sql_executed"])
        self.assertFalse(result["production_enabled"])
        self.assertEqual(self.services.received_sql, self.services.package_value.sql)
        for first, last in (("locked", "claimed"), ("backup", "claimed"), ("claimed", "sql"),
                            ("sql", "schema_after"), ("schema_after", "succeeded"), ("succeeded", "closed")):
            self.assertLess(self.events.index(first), self.events.index(last))
        before = list(self.events)
        with self.assertRaises(execution.ExecutionError):
            self.run_execution()
        self.assertEqual(self.events, before)  # No SQL, queries or replacement package on replay.

    def test_bad_backup_identity_hash_time_or_verification_never_claims_or_executes(self):
        for field, value in (("server", "other"), ("database", "other"), ("sha256", "c" * 64),
                             ("completed_at", NOW.isoformat()), ("checksum_valid", False),
                             ("restore_verified", "true"), ("copy_only", 1),
                             ("checked_at", (NOW - timedelta(minutes=6)).isoformat()),
                             ("checked_at", (NOW + timedelta(seconds=1)).isoformat())):
            original = copy.deepcopy(self.services.evidence)
            self.services.evidence[field] = value
            with self.subTest(field=field, value=value), self.assertRaises(execution.ExecutionError):
                self.run_execution()
            self.assertEqual(self.phase(), "approved")
            self.assertNotIn("sql", self.events)
            self.services.evidence = original

    def test_backup_declaration_from_approval_is_not_independent_evidence(self):
        self.services.evidence = self.record["backup"]
        with self.assertRaises(execution.ExecutionError):
            self.run_execution()
        self.assertEqual(self.phase(), "approved")

    def test_preclaim_provenance_schema_backup_and_changed_history_fail_closed(self):
        for event in ("online1", "schema_before", "backup"):
            self.services = FakeServices(self)
            self.services.faults[event] = RuntimeError("DO NOT ECHO credential=synthetic")
            with self.subTest(event=event), self.assertRaises(execution.ExecutionError) as error:
                self.run_execution()
            self.assertNotIn("credential", str(error.exception))
            self.assertEqual(self.phase(), "approved")
        self.services = FakeServices(self)
        self.services.snapshot["migrations"] = IDS
        with self.assertRaises(execution.ExecutionError):
            self.run_execution()
        self.assertEqual(self.phase(), "approved")
        self.assertNotIn("sql", self.events)

    def test_changed_bytes_fail_before_online_check_or_lock(self):
        self.services.package_value = execution.VerifiedSql(self.manifest, b"changed")
        with self.assertRaises(execution.ExecutionError):
            self.run_execution()
        self.assertEqual(self.events, ["package"])
        self.assertEqual(self.phase(), "approved")

    def test_after_claim_provenance_or_sql_failure_blocks_retry_and_redacts_errors(self):
        for event in ("online2", "sql", "schema_after"):
            with self.subTest(event=event):
                self.store = MemoryLedger(self)
                self.services = FakeServices(self)
                self.services.faults[event] = RuntimeError("DO NOT ECHO SQL/password")
                with self.assertRaises(execution.ExecutionError) as error:
                    self.run_execution()
                self.assertNotIn("password", str(error.exception))
                self.assertEqual(self.phase(), "failed")
                before = list(self.events)
                with self.assertRaises(execution.ExecutionError):
                    self.run_execution()
                self.assertEqual(before, self.events)

    def test_wrong_post_history_cannot_be_recorded_as_success(self):
        original = self.services.apply
        def apply(sql, digest):
            original(sql, digest)
            self.services.snapshot["migrations"] = IDS[:1]
        self.services.apply = apply
        with self.assertRaises(execution.ExecutionError):
            self.run_execution()
        self.assertEqual(self.phase(), "failed")

    def test_approval_expiring_during_second_provenance_check_does_not_start_sql(self):
        original = self.services.online_check
        def online(manifest):
            original(manifest)
            if self.services.online_count == 2:
                self.services.clock += timedelta(hours=1)
        self.services.online_check = online
        with self.assertRaises(execution.ExecutionError):
            self.run_execution()
        self.assertEqual(self.phase(), "failed")
        self.assertNotIn("sql", self.events)

    def test_claim_persistence_failure_never_starts_sql(self):
        for point, phase in (("before", "approved"), ("after", "claimed")):
            self.store = MemoryLedger(self)
            self.store.claim_failure = point
            with self.subTest(point=point), self.assertRaises(execution.ExecutionError):
                self.run_execution()
            self.assertEqual(self.phase(), phase)
            self.assertNotIn("sql", self.events)

    def test_changed_claim_fingerprint_never_starts_sql_or_clears_the_claim(self):
        original = self.store.claim
        def claim(*args, **kwargs):
            receipt = original(*args, **kwargs)
            return {**receipt, "approval_sha256": "d" * 64}
        self.store.claim = claim
        with self.assertRaises(execution.ExecutionError):
            self.run_execution()
        self.assertEqual(self.phase(), "claimed")
        self.assertNotIn("sql", self.events)

    def test_completion_persistence_failure_keeps_claim_and_never_retries_committed_sql(self):
        self.store.finish_failure = True
        with self.assertRaises(execution.ExecutionError):
            self.run_execution()
        self.assertEqual(self.phase(), "claimed")
        self.assertEqual(self.events.count("sql"), 1)
        with self.assertRaises(execution.ExecutionError):
            self.run_execution()
        self.assertEqual(self.events.count("sql"), 1)

    def test_interrupt_is_recorded_and_session_closes_without_retry(self):
        self.services.faults["sql"] = KeyboardInterrupt()
        with self.assertRaises(execution.ExecutionError):
            self.run_execution()
        self.assertEqual(self.phase(), "interrupted")
        self.assertIn("closed", self.events)
        with self.assertRaises(execution.ExecutionError):
            self.run_execution()

    def test_empty_or_unsupported_range_is_not_an_execution_profile(self):
        record = copy.deepcopy(self.record)
        record.update(applied=[], pending=IDS)
        snapshot = json.loads(self.status)
        snapshot["migrations"] = []
        self.store.state = ledger.register_record(ledger.empty_state(), json.dumps(record),
                                                  self.manifest, json.dumps(snapshot), now=NOW)
        with self.assertRaises(execution.ExecutionError):
            self.run_execution()
        self.assertEqual(self.events, [])

    def test_ci_target_is_separate_and_existing_default_validator_rejects_ci_records(self):
        database = "Project1CiMigration_" + "a" * 32 + "_upgrade"
        login = "Project1CiExecutor_" + "a" * 32
        environment = lambda name: "true"
        target = ci_target(database, login, environment)
        record = {**self.record, "server": target.server, "database": target.database}
        snapshot = {**json.loads(self.status), "server": target.server,
                    "database": target.database, "login": target.login}
        with self.assertRaises(ledger.LedgerError):
            ledger.register_record(ledger.empty_state(), json.dumps(record), self.manifest,
                                   json.dumps(snapshot), now=NOW)
        state = ledger.register_record(ledger.empty_state(), json.dumps(record), self.manifest,
                                       json.dumps(snapshot), now=NOW, target=target)
        with self.assertRaises(ledger.LedgerError):
            ledger.validate_state(state)  # Production cannot open a CI-target ledger.
        ledger.validate_state(state, target=target)
        for db, account, env in (("Project1Db", login, environment), (database, "sa", environment),
                                 (database, login, lambda name: "false")):
            with self.assertRaises(Exception):
                ci_target(db, account, env)

    def test_new_approval_cannot_bypass_an_unresolved_attempt(self):
        self.services.faults["sql"] = RuntimeError("Synthetic failure")
        with self.assertRaises(execution.ExecutionError):
            self.run_execution()
        record = {**self.record, "approval_id": "c" * 32}
        self.store.state = ledger.register_record(self.store.state, json.dumps(record), self.manifest,
                                                  self.status, now=NOW)
        before = list(self.events)
        with self.assertRaises(execution.ExecutionError):
            execution.execute(record["approval_id"], self.store, self.services)
        self.assertEqual(before, self.events)

    def test_test_host_packages_exact_sql_without_claiming_real_online_provenance(self):
        from ci_execution_workflow import package_fixture
        import base64
        settings = {"sql": base64.b64encode(self.services.package_value.sql).decode(),
                    "commit": self.manifest["commit"], "run_id": self.manifest["run_id"],
                    "run_attempt": self.manifest["run_attempt"], "migrations": IDS}
        candidate = execution.VerifiedSql.from_directory(package_fixture(self.root, settings))
        self.assertEqual(candidate.sql, self.services.package_value.sql)

    def test_coordinator_has_no_install_cli_secrets_or_service_control(self):
        source = Path(execution.__file__).read_text()
        for forbidden in ("import subprocess", "argparse", "secrets.json", "/etc/project1", "systemctl"):
            self.assertNotIn(forbidden, source)


@unittest.skipUnless(sys.platform == "linux", "Actual ledger fsync/flock workflow requires Linux")
class LinuxExecutionTests(FixtureCase):
    def setUp(self):
        super().setUp()
        self.events = []
        self.services = FakeServices(self)
        self.ledger_root = self.root / "execution-ledger"
        self.ledger_root.mkdir(mode=0o700)
        uid = os.getuid()
        def owner(actual):
            if actual != uid:
                raise ledger.LedgerError("Unexpected sandbox owner")
        def directory(path):
            if path != self.ledger_root:
                raise ledger.LedgerError("Unexpected sandbox path")
            info = path.lstat()
            owner(info.st_uid)
            if not stat.S_ISDIR(info.st_mode) or stat.S_IMODE(info.st_mode) != 0o700:
                raise ledger.LedgerError("Invalid sandbox directory")
        for patch in (mock.patch.object(os, "geteuid", return_value=0),
                      mock.patch.object(ledger, "_require_owner", side_effect=owner),
                      mock.patch.object(ledger, "_trusted_directory", side_effect=directory)):
            patch.start()
            self.addCleanup(patch.stop)
        self.store = ledger.ApprovalLedger(self.ledger_root)
        self.store.initialize()
        self.store.register(json.dumps(self.record), self.manifest, self.status, now=NOW)

    def test_persisted_claim_precedes_sql_and_reopened_success_cannot_replay(self):
        original = self.services.apply
        def apply(sql, digest):
            reopened = ledger.ApprovalLedger(self.ledger_root)
            self.assertEqual(reopened.inspect()["entries"][self.identifier]["phase"], "claimed")
            original(sql, digest)
        self.services.apply = apply
        execution.execute(self.identifier, self.store, self.services)
        reopened = ledger.ApprovalLedger(self.ledger_root)
        self.assertEqual(reopened.inspect()["entries"][self.identifier]["phase"], "succeeded")
        with self.assertRaises(execution.ExecutionError):
            execution.execute(self.identifier, reopened, self.services)
        self.assertEqual(self.events.count("sql"), 1)

    def test_completion_write_failure_leaves_durable_claim_not_automatic_retry(self):
        original = self.store._write
        def write(directory, state):
            if state["entries"][self.identifier]["phase"] != "claimed":
                raise OSError("Synthetic completion write failure")
            original(directory, state)
        with mock.patch.object(self.store, "_write", side_effect=write), self.assertRaises(execution.ExecutionError):
            execution.execute(self.identifier, self.store, self.services)
        reopened = ledger.ApprovalLedger(self.ledger_root)
        self.assertEqual(reopened.inspect()["entries"][self.identifier]["phase"], "claimed")
        with self.assertRaises(execution.ExecutionError):
            execution.execute(self.identifier, reopened, self.services)
        self.assertEqual(self.events.count("sql"), 1)

    def test_persisted_failure_blocks_a_different_newly_registered_approval(self):
        self.services.faults["sql"] = RuntimeError("Synthetic SQL failure")
        with self.assertRaises(execution.ExecutionError):
            execution.execute(self.identifier, self.store, self.services)
        record = {**self.record, "approval_id": "c" * 32}
        self.store.register(json.dumps(record), self.manifest, self.status, now=NOW)
        with self.assertRaises(execution.ExecutionError):
            execution.execute(record["approval_id"], self.store, self.services)
        self.assertEqual(self.events.count("sql"), 1)
