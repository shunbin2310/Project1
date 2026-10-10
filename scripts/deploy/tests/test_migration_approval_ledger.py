"""Sandbox bookkeeping tests only; no real root installation, network or SQL.

Pure transitions are portable. Linux cases use real temp files, flock/fsync and
separate processes; ONLY sandbox ownership/ancestor checks and effective UID are
adapted to the unprivileged test runner. Production code has no such override.
"""

import copy
from datetime import timedelta
import hashlib
import json
import multiprocessing
import os
from pathlib import Path
import stat
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest import mock

SCRIPTS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SCRIPTS))
import migration_approval_ledger as ledger
import verify_migration_artifact as verify
from test_migration_approval import NOW, record_fixture, status_fixture
from test_migration_precheck import FakeClient, IDS
from test_manual_cd import RUN_ID, SHA


class FixtureCase(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        for target in ("subprocess.run", "socket.create_connection", "urllib.request.urlopen"):
            patch = mock.patch(target, side_effect=AssertionError("External access is forbidden."))
            patch.start()
            self.addCleanup(patch.stop)
        self.manifest = verify.verify(FakeClient(), RUN_ID, SHA, self.root / "verified", 1)
        self.record = record_fixture(self.manifest)
        self.identifier = self.record["approval_id"]
        self.status = json.dumps(status_fixture())

    def registered(self, identifier=None, state=None):
        record = copy.deepcopy(self.record)
        if identifier is not None:
            record["approval_id"] = identifier
        return ledger.register_record(ledger.empty_state() if state is None else state,
                                      json.dumps(record), self.manifest, self.status, now=NOW)

    def claim(self, state, identifier=None, now=NOW, manifest=None, status=None):
        return ledger.claim_record(state, self.identifier if identifier is None else identifier,
                                   self.manifest if manifest is None else manifest,
                                   self.status if status is None else status, now=now)


class TransitionTests(FixtureCase):
    def test_registration_is_immutable_and_hash_binds_whole_record(self):
        original = ledger.empty_state()
        state = self.registered(state=original)
        self.assertEqual(original, ledger.empty_state())
        self.assertEqual(state["entries"][self.identifier]["phase"], "approved")
        self.assertEqual(state["entries"][self.identifier]["approval_sha256"],
                         hashlib.sha256(ledger.canonical(self.record)).hexdigest())
        self.assertEqual(ledger.decode_state(ledger.canonical(state)), state)

    def test_claim_and_terminal_states_never_allow_same_id_again(self):
        for outcome in ("succeeded", "failed", "interrupted"):
            with self.subTest(outcome=outcome):
                state = self.registered()
                before = copy.deepcopy(state)
                claimed, receipt = self.claim(state)
                self.assertEqual(state, before)
                self.assertFalse(receipt["execution_authorized"])
                self.assertFalse(receipt["sql_executed"])
                with self.assertRaises(ledger.LedgerError):
                    self.claim(claimed)
                completed = ledger.finish_record(claimed, receipt, outcome, now=NOW + timedelta(seconds=1))
                self.assertEqual(completed["entries"][self.identifier]["phase"], outcome)
                with self.assertRaises(ledger.LedgerError):
                    self.claim(completed)
                with self.assertRaises(ledger.LedgerError):
                    ledger.finish_record(completed, receipt, outcome, now=NOW + timedelta(seconds=2))

    def test_duplicate_registration_cannot_replace_approved_or_consumed_id(self):
        state = self.registered()
        claimed, receipt = self.claim(state)
        states = (state, claimed, ledger.finish_record(claimed, receipt, "succeeded", now=NOW))
        for prior in states:
            for mutate in (False, True):
                bad = copy.deepcopy(self.record)
                if mutate:
                    bad["backup"]["sha256"] = "c" * 64
                before = ledger.canonical(prior)
                with self.subTest(phase=prior["entries"][self.identifier]["phase"], mutate=mutate), \
                        self.assertRaises(ledger.LedgerError):
                    ledger.register_record(prior, json.dumps(bad), self.manifest, self.status, now=NOW)
                self.assertEqual(ledger.canonical(prior), before)

    def test_all_unresolved_outcomes_block_other_approvals(self):
        second = "b" * 32
        state = self.registered(second, self.registered())
        claimed, receipt = self.claim(state)
        for blocked in (claimed, ledger.finish_record(claimed, receipt, "failed", now=NOW),
                        ledger.finish_record(claimed, receipt, "interrupted", now=NOW)):
            with self.subTest(phase=blocked["entries"][self.identifier]["phase"]), \
                    self.assertRaises(ledger.LedgerError):
                self.claim(blocked, second)
        # Registration of a new admin record does not resolve or bypass a blocked claim.
        more = self.registered("c" * 32, claimed)
        with self.assertRaises(ledger.LedgerError):
            self.claim(more, "c" * 32)

    def test_success_allows_a_different_approval_not_reuse_of_old_one(self):
        state = self.registered("b" * 32, self.registered())
        claimed, receipt = self.claim(state)
        completed = ledger.finish_record(claimed, receipt, "succeeded", now=NOW)
        next_state, next_receipt = self.claim(completed, "b" * 32)
        self.assertEqual(next_state["entries"][self.identifier]["phase"], "succeeded")
        self.assertNotEqual(receipt["attempt_id"], next_receipt["attempt_id"])
        self.assertFalse(next_receipt["execution_authorized"])

    def test_claim_uses_stored_record_and_revalidates_package_history_and_expiry(self):
        state = self.registered()
        for field, value in (("commit", "d" * 40), ("run_id", RUN_ID + 1),
                             ("run_attempt", 2), ("sql_sha256", "d" * 64)):
            manifest = {**self.manifest, field: value}
            with self.subTest(field=field), self.assertRaises(ledger.LedgerError):
                self.claim(state, manifest=manifest)
        bad_zip = copy.deepcopy(self.manifest)
        bad_zip["artifact"]["sha256"] = "d" * 64
        with self.assertRaises(ledger.LedgerError):
            self.claim(state, manifest=bad_zip)
        for status in ({**status_fixture(), "migrations": IDS},
                       {**status_fixture(), "database": "master"},
                       {**status_fixture(), "checked_at": (NOW - timedelta(minutes=6)).isoformat()}):
            with self.subTest(status=status), self.assertRaises(ledger.LedgerError):
                self.claim(state, status=json.dumps(status))
        with self.assertRaises(ledger.LedgerError):
            self.claim(state, now=NOW + timedelta(hours=1))
        self.assertEqual(state["entries"][self.identifier]["phase"], "approved")

    def test_registration_refuses_unreviewed_missing_backup_and_bad_json(self):
        for body in ("not-json", '{"schema":1,"schema":1}', '{}', '[]'):
            with self.subTest(body=body), self.assertRaises(ledger.LedgerError):
                ledger.register_record(ledger.empty_state(), body, self.manifest, self.status, now=NOW)
        for field in ("backup", "confirmations"):
            bad = copy.deepcopy(self.record)
            bad[field] = {}
            with self.subTest(field=field), self.assertRaises(ledger.LedgerError):
                ledger.register_record(ledger.empty_state(), json.dumps(bad), self.manifest, self.status, now=NOW)

    def test_receipt_cannot_complete_other_attempt_or_changed_content(self):
        claimed, receipt = self.claim(self.registered())
        for field, value in (("approval_id", "b" * 32), ("attempt_id", "b" * 32),
                             ("approval_sha256", "b" * 64), ("execution_authorized", True),
                             ("execution_authorized", 0), ("sql_executed", True), ("extra", "bad")):
            with self.subTest(field=field), self.assertRaises(ledger.LedgerError):
                ledger.finish_record(claimed, {**receipt, field: value}, "succeeded", now=NOW)
        for key in receipt:
            bad = {k: v for k, v in receipt.items() if k != key}
            with self.subTest(key=key), self.assertRaises(ledger.LedgerError):
                ledger.finish_record(claimed, bad, "succeeded", now=NOW)

    def test_rearming_and_invalid_outcomes_are_not_supported(self):
        claimed, receipt = self.claim(self.registered())
        for outcome in ("approved", "claimed", "retry", "recovered", "", None, True, []):
            with self.subTest(outcome=outcome), self.assertRaises(ledger.LedgerError):
                ledger.finish_record(claimed, receipt, outcome, now=NOW)

    def test_backward_and_naive_clocks_are_refused_but_completion_can_follow_expiry(self):
        state = self.registered()
        for now in (NOW - timedelta(seconds=1), NOW.replace(tzinfo=None), None):
            with self.subTest(now=now), self.assertRaises(ledger.LedgerError):
                self.claim(state, now=now)
        claimed, receipt = self.claim(state)
        with self.assertRaises(ledger.LedgerError):
            ledger.finish_record(claimed, receipt, "succeeded", now=NOW - timedelta(seconds=1))
        done = ledger.finish_record(claimed, receipt, "succeeded", now=NOW + timedelta(hours=2))
        self.assertEqual(done["entries"][self.identifier]["phase"], "succeeded")

    def test_missing_malformed_and_path_like_ids_are_refused(self):
        for identifier in ("b" * 32, "../path", "A" * 32, "a" * 31, True, None):
            with self.subTest(identifier=identifier), self.assertRaises(ledger.LedgerError):
                ledger.claim_record(self.registered(), identifier, self.manifest, self.status, now=NOW)

    def test_tampered_records_hashes_phases_and_timelines_are_refused(self):
        state = self.registered()
        variants = []
        for field, value in (("phase", "retry"), ("phase", []), ("approval_sha256", "b" * 64),
                             ("attempt_id", "b" * 32), ("claimed_at", NOW.isoformat()),
                             ("completed_at", NOW.isoformat()), ("registered_at", "yesterday")):
            bad = copy.deepcopy(state)
            bad["entries"][self.identifier][field] = value
            variants.append(bad)
        for field, value in (("commit", "d" * 40), ("sql_sha256", "d" * 64), ("approval_id", "b" * 32),
                             ("pending", []), ("backup", {}), ("confirmations", {"sql_reviewed": True})):
            bad = copy.deepcopy(state)
            bad["entries"][self.identifier]["approval"][field] = value
            variants.append(bad)
        for bad in variants:
            with self.subTest(bad=bad), self.assertRaises(ledger.LedgerError):
                ledger.validate_state(bad)
        claimed, _ = self.claim(state)
        for field, value in (("claimed_at", (NOW - timedelta(seconds=1)).isoformat()),
                             ("claimed_at", (NOW + timedelta(hours=1)).isoformat()),
                             ("completed_at", NOW.isoformat()), ("attempt_id", None)):
            bad = copy.deepcopy(claimed)
            bad["entries"][self.identifier][field] = value
            with self.subTest(field=field), self.assertRaises(ledger.LedgerError):
                ledger.validate_state(bad)

    def test_invalid_ledger_shape_duplicates_constants_and_size_are_refused(self):
        for body in (b"", b"not-json", b'{"schema":1,"schema":1,"entries":{}}', b'[]',
                     b'{"schema":true,"entries":{}}', b'{"schema":1,"entries":[],"extra":true}',
                     b'{"schema":NaN,"entries":{}}'):
            with self.subTest(body=body), self.assertRaises(ledger.LedgerError):
                ledger.decode_state(body)
        with mock.patch.object(ledger, "MAX_LEDGER_BYTES", 10), self.assertRaises(ledger.LedgerError):
            ledger.decode_state(b"x" * 11)
        with self.assertRaises(ledger.LedgerError):
            ledger.canonical({"value": float("nan")})

    def test_contradictory_active_claims_are_refused(self):
        first, _ = self.claim(self.registered())
        second, _ = self.claim(self.registered("b" * 32), "b" * 32)
        first["entries"].update(second["entries"])
        with self.assertRaises(ledger.LedgerError):
            ledger.validate_state(first)

    def test_entry_limit_never_prunes_consumed_ids(self):
        with mock.patch.object(ledger, "MAX_ENTRIES", 1):
            state, receipt = self.claim(self.registered())
            completed = ledger.finish_record(state, receipt, "succeeded", now=NOW)
            with self.assertRaises(ledger.LedgerError):
                self.registered("b" * 32, completed)
            self.assertEqual(list(completed["entries"]), [self.identifier])

    def test_fingerprint_is_canonical_not_json_whitespace(self):
        state = self.registered()
        reverse = dict(reversed(list(self.record.items())))
        self.assertEqual(ledger.approval_digest(reverse), ledger.approval_digest(self.record))
        self.assertEqual(ledger.decode_state(json.dumps(state, indent=4).encode()), state)


class StorageGuardTests(unittest.TestCase):
    def test_non_linux_and_non_root_operations_fail_before_file_access(self):
        store = ledger.ApprovalLedger(Path("/uninstalled-ledger"))
        for method in (store.initialize, store.inspect):
            with mock.patch.object(sys, "platform", "win32"), mock.patch.object(os, "open") as opened:
                with self.assertRaises(ledger.LedgerError):
                    method()
                opened.assert_not_called()
            with mock.patch.object(sys, "platform", "linux"), \
                    mock.patch.object(os, "geteuid", return_value=1001, create=True), mock.patch.object(os, "open") as opened:
                with self.assertRaises(ledger.LedgerError):
                    method()
                opened.assert_not_called()

    def test_private_file_guard_checks_owner_mode_type_and_links(self):
        for mode, uid, links in ((stat.S_IFREG | 0o600, 1001, 1), (stat.S_IFREG | 0o644, 0, 1),
                                 (stat.S_IFLNK | 0o600, 0, 1), (stat.S_IFIFO | 0o600, 0, 1),
                                 (stat.S_IFREG | 0o600, 0, 2)):
            with self.subTest(mode=mode, uid=uid, links=links), self.assertRaises(ledger.LedgerError):
                ledger._private_file(SimpleNamespace(st_mode=mode, st_uid=uid, st_nlink=links))
        ledger._private_file(SimpleNamespace(st_mode=stat.S_IFREG | 0o600, st_uid=0, st_nlink=1))

    def test_ancestor_guard_checks_every_component_without_real_file_access(self):
        path = Path(tempfile.gettempdir()) / "ledger-guard-fixture"
        for component in (path, path.parent, path.parents[-1]):
            for bad in (SimpleNamespace(st_mode=stat.S_IFDIR | 0o700, st_uid=1001),
                        SimpleNamespace(st_mode=stat.S_IFLNK | 0o700, st_uid=0),
                        SimpleNamespace(st_mode=stat.S_IFDIR | 0o777, st_uid=0)):
                def lstat(current):
                    if current == component:
                        return bad
                    return SimpleNamespace(st_mode=stat.S_IFDIR | (0o700 if current == path else 0o755), st_uid=0)
                with self.subTest(component=component, bad=bad), mock.patch.object(Path, "lstat", lstat), \
                        self.assertRaises(ledger.LedgerError):
                    ledger._trusted_directory(path)
        def safe(current):
            return SimpleNamespace(st_mode=stat.S_IFDIR | (0o700 if current == path else 0o755), st_uid=0)
        with mock.patch.object(Path, "lstat", safe):
            ledger._trusted_directory(path)

    def test_relative_and_dotdot_directories_are_refused(self):
        for path in (Path("relative"), Path(tempfile.gettempdir()) / ".." / "ledger"):
            with self.subTest(path=path), mock.patch.object(Path, "lstat") as read, self.assertRaises(ledger.LedgerError):
                ledger._trusted_directory(path)
            read.assert_not_called()

    def test_no_cli_and_no_production_directory_contract(self):
        source = (SCRIPTS / "migration_approval_ledger.py").read_text(encoding="utf-8")
        self.assertNotIn('if __name__ == "__main__"', source)
        self.assertNotIn("/etc/project1", source)
        self.assertNotIn("/var/lib/project1", source)
        self.assertNotIn("subprocess", source)


@unittest.skipUnless(sys.platform == "linux", "Real dir_fd, flock, permissions and fsync require Linux")
class LinuxStorageTests(FixtureCase):
    def setUp(self):
        super().setUp()
        self.store_root = self.root / "ledger"
        self.store_root.mkdir(mode=0o700)
        self.store = ledger.ApprovalLedger(self.store_root)
        uid = os.getuid()
        def sandbox_owner(owner):
            if owner != uid:
                raise ledger.LedgerError("Unexpected sandbox owner.")
        def sandbox_directory(path):
            # Sandbox ancestors (/tmp, runner home) are intentionally not root-owned.
            if path != self.store_root:
                raise ledger.LedgerError("Unexpected sandbox directory.")
            info = path.lstat()
            sandbox_owner(info.st_uid)
            if not stat.S_ISDIR(info.st_mode) or stat.S_IMODE(info.st_mode) != 0o700:
                raise ledger.LedgerError("Unsafe sandbox directory.")
        for patch in (mock.patch.object(os, "geteuid", return_value=0),
                      mock.patch.object(ledger, "_require_owner", side_effect=sandbox_owner),
                      mock.patch.object(ledger, "_trusted_directory", side_effect=sandbox_directory)):
            patch.start()
            self.addCleanup(patch.stop)
        self.store.initialize()

    def register(self, identifier=None):
        record = copy.deepcopy(self.record)
        if identifier is not None:
            record["approval_id"] = identifier
        self.store.register(json.dumps(record), self.manifest, self.status, now=NOW)

    def claim_store(self, identifier=None):
        return self.store.claim(self.identifier if identifier is None else identifier,
                                self.manifest, self.status, now=NOW)

    def test_real_persistence_modes_and_reopen_refuse_replay(self):
        self.register()
        receipt = self.claim_store()
        self.store.finish(receipt, "succeeded", now=NOW)
        reopened = ledger.ApprovalLedger(self.store_root)
        self.assertEqual(reopened.inspect()["entries"][self.identifier]["phase"], "succeeded")
        with self.assertRaises(ledger.LedgerError):
            reopened.claim(self.identifier, self.manifest, self.status, now=NOW)
        for name in ("ledger.lock", "ledger.json"):
            self.assertEqual(stat.S_IMODE((self.store_root / name).stat().st_mode), 0o600)
        self.assertFalse(list(self.store_root.glob(".ledger-*")))

    def test_missing_files_are_never_reinitialized_or_recreated(self):
        self.register()
        for name in ("ledger.lock", "ledger.json"):
            path = self.store_root / name
            body = path.read_bytes()
            path.unlink()
            with self.assertRaises(FileNotFoundError):
                self.claim_store()
            self.assertFalse(path.exists())
            with self.assertRaises(ledger.LedgerError):
                self.store.initialize()
            path.write_bytes(body)
            path.chmod(0o600)

    def test_corrupted_record_blocks_operations_and_is_retained(self):
        self.register()
        path = self.store_root / "ledger.json"
        path.write_bytes(b'{"schema":1,"entries":')
        for action in (self.store.inspect, self.claim_store, lambda: self.register("b" * 32)):
            with self.assertRaises(ledger.LedgerError):
                action()
            self.assertEqual(path.read_bytes(), b'{"schema":1,"entries":')

    def test_symlink_fifo_hardlink_and_public_files_are_refused(self):
        path = self.store_root / "ledger.json"
        body = path.read_bytes()
        target = self.store_root / "target"
        target.write_bytes(body)
        target.chmod(0o600)
        for kind in ("symlink", "fifo", "hardlink", "public"):
            path.unlink()
            if kind == "symlink":
                path.symlink_to(target)
            elif kind == "fifo":
                os.mkfifo(path, 0o600)
            elif kind == "hardlink":
                os.link(target, path)
            else:
                path.write_bytes(body)
                path.chmod(0o644)
            with self.subTest(kind=kind), self.assertRaises((ledger.LedgerError, OSError)):
                self.store.inspect()
        self.assertEqual(target.read_bytes(), body)

    def test_lock_is_private_regular_and_not_replaced(self):
        path = self.store_root / "ledger.lock"
        for kind in ("symlink", "fifo", "hardlink", "public", "content"):
            path.unlink()
            target = self.store_root / ("lock-target-" + kind)
            target.write_bytes(b"")
            target.chmod(0o600)
            if kind == "symlink":
                path.symlink_to(target)
            elif kind == "fifo":
                os.mkfifo(path, 0o600)
            elif kind == "hardlink":
                os.link(target, path)
            else:
                path.write_bytes(b"unexpected" if kind == "content" else b"")
                path.chmod(0o644 if kind == "public" else 0o600)
            with self.subTest(kind=kind), self.assertRaises((ledger.LedgerError, OSError)):
                self.store.inspect()

    def test_nonblocking_lock_prevents_second_request(self):
        self.register()
        with self.store._locked():
            with self.assertRaises(ledger.LedgerError):
                self.claim_store()
        self.assertEqual(self.store.inspect()["entries"][self.identifier]["phase"], "approved")
        self.claim_store()

    def test_two_real_processes_can_only_claim_once(self):
        self.register()
        # fork inherits only the explicit sandbox metadata adaptations; no real root.
        context = multiprocessing.get_context("fork")
        start = context.Event()
        result = context.Queue()
        def worker():
            start.wait(5)
            try:
                result.put(("claimed", self.claim_store()["attempt_id"]))
            except ledger.LedgerError:
                result.put(("refused", None))
        children = [context.Process(target=worker) for _ in range(2)]
        try:
            for child in children:
                child.start()
            start.set()
            values = [result.get(timeout=10) for _ in children]
            for child in children:
                child.join(timeout=10)
                self.assertEqual(child.exitcode, 0)
            self.assertEqual(sorted(value[0] for value in values), ["claimed", "refused"])
            self.assertEqual(self.store.inspect()["entries"][self.identifier]["phase"], "claimed")
        finally:
            for child in children:
                if child.is_alive():
                    child.terminate()
                child.join(timeout=5)
            result.close()

    def test_claim_survives_process_exit_without_completion_and_blocks_next(self):
        self.register()
        self.register("b" * 32)
        context = multiprocessing.get_context("fork")
        def abandoned():
            self.claim_store()
            os._exit(0)  # No finish call or application cleanup.
        child = context.Process(target=abandoned)
        try:
            child.start()
            child.join(timeout=10)
            self.assertEqual(child.exitcode, 0)
            with self.assertRaises(ledger.LedgerError):
                self.claim_store("b" * 32)
            self.assertEqual(self.store.inspect()["entries"][self.identifier]["phase"], "claimed")
        finally:
            if child.is_alive():
                child.terminate()
            child.join(timeout=5)

    def test_write_failure_before_replace_returns_no_receipt_and_leaves_old_state(self):
        self.register()
        before = (self.store_root / "ledger.json").read_bytes()
        with mock.patch.object(os, "replace", side_effect=OSError("fake interruption")), self.assertRaises(OSError):
            self.claim_store()
        self.assertEqual((self.store_root / "ledger.json").read_bytes(), before)
        self.assertFalse(list(self.store_root.glob(".ledger-*")))

    def test_directory_fsync_failure_returns_no_receipt_but_retains_claim(self):
        self.register()
        real_fsync = os.fsync
        def fail_directory(descriptor):
            if stat.S_ISDIR(os.fstat(descriptor).st_mode):
                raise OSError("fake directory flush failure")
            return real_fsync(descriptor)
        with mock.patch.object(os, "fsync", side_effect=fail_directory), self.assertRaises(OSError):
            self.claim_store()
        self.assertEqual(self.store.inspect()["entries"][self.identifier]["phase"], "claimed")
        with self.assertRaises(ledger.LedgerError):
            self.claim_store()

    def test_file_fsync_failure_prevents_replace_and_claim_return(self):
        self.register()
        before = (self.store_root / "ledger.json").read_bytes()
        with mock.patch.object(os, "fsync", side_effect=OSError("fake file flush failure")), \
                mock.patch.object(os, "replace") as replace, self.assertRaises(OSError):
            self.claim_store()
        replace.assert_not_called()
        self.assertEqual((self.store_root / "ledger.json").read_bytes(), before)

    def test_flush_order_is_file_then_replace_then_directory(self):
        self.register()
        events = []
        real_fsync, real_replace = os.fsync, os.replace
        def fsync(descriptor):
            events.append("directory" if stat.S_ISDIR(os.fstat(descriptor).st_mode) else "file")
            return real_fsync(descriptor)
        def replace(*args, **kwargs):
            events.append("replace")
            return real_replace(*args, **kwargs)
        with mock.patch.object(os, "fsync", side_effect=fsync), mock.patch.object(os, "replace", side_effect=replace):
            self.claim_store()
        self.assertEqual(events, ["file", "replace", "directory"])

    def test_partial_initialization_is_not_silently_repaired(self):
        (self.store_root / "ledger.json").unlink()
        with self.assertRaises(ledger.LedgerError):
            self.store.initialize()
        self.assertTrue((self.store_root / "ledger.lock").exists())
        self.assertFalse((self.store_root / "ledger.json").exists())

    def test_changed_lock_inode_is_refused_before_read(self):
        real_stat = os.stat
        def stat_changed(path, *args, **kwargs):
            info = real_stat(path, *args, **kwargs)
            if path == "ledger.lock":
                return SimpleNamespace(st_dev=info.st_dev, st_ino=info.st_ino + 1)
            return info
        with mock.patch.object(os, "stat", side_effect=stat_changed), \
                mock.patch.object(self.store, "_read") as read, self.assertRaises(ledger.LedgerError):
            self.store.inspect()
        read.assert_not_called()

    def test_actual_directory_symlink_or_public_mode_is_refused(self):
        self.store_root.chmod(0o755)
        with self.assertRaises(ledger.LedgerError):
            self.store.inspect()
        self.store_root.chmod(0o700)
        moved = self.root / "moved"
        self.store_root.rename(moved)
        self.store_root.symlink_to(moved)
        with self.assertRaises(ledger.LedgerError):
            self.store.inspect()

    def test_real_ancestor_guard_refuses_links_wrong_owner_or_writable_directory(self):
        # Original production ancestor check with fabricated metadata, no /etc access.
        original = self.directory_guard_original
        path = Path("/sandbox/ledger")
        for bad in (SimpleNamespace(st_mode=stat.S_IFLNK | 0o700, st_uid=os.getuid()),
                    SimpleNamespace(st_mode=stat.S_IFDIR | 0o777, st_uid=os.getuid()),
                    SimpleNamespace(st_mode=stat.S_IFDIR | 0o755, st_uid=123456)):
            def lstat(current):
                if current == Path("/sandbox"):
                    return bad
                return SimpleNamespace(st_mode=stat.S_IFDIR | (0o700 if current == path else 0o755), st_uid=os.getuid())
            with self.subTest(bad=bad), mock.patch.object(Path, "lstat", lstat), self.assertRaises(ledger.LedgerError):
                original(path)

    directory_guard_original = staticmethod(ledger._trusted_directory)


if __name__ == "__main__":
    unittest.main()
