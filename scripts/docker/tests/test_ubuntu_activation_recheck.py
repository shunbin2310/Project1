"""Offline guarded-reconciliation tests. No real env files, SQL or host services."""
import contextlib
import importlib.util
import io
import json
from pathlib import Path
import types
import unittest
from unittest.mock import Mock, patch

SPEC = importlib.util.spec_from_file_location('recheck', Path(__file__).resolve().parents[1] / 'recheck_ubuntu_activation.py')
r = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(r)
SHA = 'a' * 64


def fixture():
    state = dict(phase='activation-failed', tag=r.TARGET_TAG, old_nginx_sha256='b' * 64,
                 backup={'sha256': SHA, 'bytes': 123}, frozen_counts={'users': 1})
    manifest = dict(files_sha256={'ubuntu_production.py': r.HELPER_SHA, 'project1-docker.conf': 'c' * 64},
                    frontend_sha256='d' * 64,
                    images={s: {'reference': f'project1-ubuntu-{s}:{r.TARGET_TAG}'} for s in ('api', 'frontend')})
    m = types.SimpleNamespace(STATE_FILE=Path('/fixture/state.json'), STATE_DIR=Path('/fixture/state'),
                              OLD_SITE=Path('/fixture/site'), ROOT=Path('/fixture/root'))
    for name in ('backup_matches', 'old_files', 'old_stopped', 'site_contract', 'root_file', 'runtime_check',
                 'directory', 'write_new', 'save_state', 'web_check', 'systemctl', 'replace_file'):
        setattr(m, name, Mock())
    m.read_state = Mock(return_value=state)
    m.release = Mock(return_value=(Path('/sealed'), manifest))
    m.read_config = Mock(return_value={'public-fake-secret': 'never-echo-this-fixture'})
    m.compose = Mock(return_value='api\nfrontend')
    m.digest = Mock(side_effect=lambda path: SHA if path == m.STATE_FILE else ('c' * 64 if path == m.OLD_SITE else 'b' * 64))
    return m, state, manifest


class RecheckTests(unittest.TestCase):
    def run_fixture(self, m, apply=False):
        with patch.object(r, 'inspect_container'), patch.object(r.time, 'sleep'), contextlib.redirect_stdout(io.StringIO()):
            return r.reconcile(m, SHA, 123, apply)

    def test_preview_does_not_access_host_files_or_state(self):
        with patch.object(r, 'load_helper') as load, contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(0, r.main([]))
            load.assert_not_called()

    def test_backup_ack_required_before_state_access(self):
        for sha, count in ((None, 123), ('wrong', 123), (SHA, 0)):
            m, _, _ = fixture()
            with self.subTest(sha=sha, count=count), self.assertRaises(r.CheckError):
                r.reconcile(m, sha, count, True)
            m.read_state.assert_not_called()

    def test_only_specific_failed_activation_is_eligible(self):
        for phase in ('active', 'configured', 'frozen', 'backup-ready', 'rolled-back', 'active-pending-verification'):
            m, state, _ = fixture()
            state['phase'] = phase
            with self.subTest(phase=phase), self.assertRaises(r.CheckError):
                self.run_fixture(m, True)
            m.release.assert_not_called()
        m, state, _ = fixture()
        state['tag'] = 'prod-20261019t000000z-00000000'
        with self.assertRaises(r.CheckError):
            self.run_fixture(m, True)

    def test_check_mode_performs_checks_without_writes_or_service_mutation(self):
        m, state, _ = fixture()
        self.run_fixture(m)
        for name in ('directory', 'write_new', 'save_state', 'systemctl', 'replace_file'):
            getattr(m, name).assert_not_called()
        self.assertEqual('activation-failed', state['phase'])
        self.assertEqual(6, m.web_check.call_count)
        self.assertEqual(2, m.runtime_check.call_count)
        m.backup_matches.assert_called_once_with(state, SHA, 123)

    def test_apply_preserves_original_failure_before_recording_pending_not_active(self):
        m, state, _ = fixture()
        events = []
        m.write_new.side_effect = lambda *args: events.append('audit')
        m.save_state.side_effect = lambda *args: events.append('state')
        self.run_fixture(m, True)
        self.assertEqual(['audit', 'state'], events)
        self.assertEqual(state, json.loads(m.write_new.call_args.args[1]))
        self.assertEqual('activation-failed', state['phase'])
        updated = m.save_state.call_args.args[0]
        self.assertEqual('active-pending-verification', updated['phase'])
        self.assertEqual('undetermined', updated['activation_recheck']['initial_failure_cause'])
        self.assertEqual(state['backup'], updated['backup'])
        self.assertEqual(state['frozen_counts'], updated['frozen_counts'])
        m.systemctl.assert_not_called()
        m.replace_file.assert_not_called()
        self.assertTrue(all(call.args[1] == 'ps' for call in m.compose.call_args_list))

    def test_any_failed_guard_prevents_audit_and_state_writes(self):
        for name in ('release', 'backup_matches', 'old_files', 'old_stopped', 'site_contract', 'runtime_check', 'web_check'):
            m, _, _ = fixture()
            getattr(m, name).side_effect = RuntimeError('never-echo-this-fixture')
            with self.subTest(name=name), self.assertRaises(r.CheckError) as error:
                self.run_fixture(m, True)
            self.assertNotIn('never-echo-this-fixture', str(error.exception))
            m.write_new.assert_not_called()
            m.save_state.assert_not_called()

    def test_wrong_helper_or_nginx_or_extra_service_is_rejected(self):
        for change in ('helper', 'nginx', 'services'):
            m, _, manifest = fixture()
            if change == 'helper':
                manifest['files_sha256']['ubuntu_production.py'] = 'e' * 64
            elif change == 'nginx':
                manifest['files_sha256']['project1-docker.conf'] = 'e' * 64
            else:
                m.compose.return_value = 'api\nfrontend\nother'
            with self.subTest(change=change), self.assertRaises(r.CheckError):
                self.run_fixture(m, True)
            m.save_state.assert_not_called()

    def test_state_change_during_recheck_is_not_overwritten(self):
        m, _, _ = fixture()
        original = m.digest.side_effect
        calls = []
        def changing(path):
            if path == m.STATE_FILE:
                calls.append(path)
                return SHA if len(calls) == 1 else 'e' * 64
            return original(path)
        m.digest.side_effect = changing
        with self.assertRaises(r.CheckError):
            self.run_fixture(m, True)
        m.write_new.assert_not_called()
        m.save_state.assert_not_called()

    def test_failed_audit_write_never_advances_state(self):
        m, _, _ = fixture()
        m.write_new.side_effect = OSError('disk full')
        with self.assertRaises(OSError):
            self.run_fixture(m, True)
        m.save_state.assert_not_called()

    def test_running_image_fingerprint_must_match_not_just_tag(self):
        metadata = {'reference': f'project1-ubuntu-api:{r.TARGET_TAG}'}
        row = '|'.join(['sha256:' + SHA, metadata['reference'], 'true', 'false', 'healthy', r.PROJECT, 'api'])
        m = types.SimpleNamespace(docker=Mock(side_effect=[row, 'actual-bits', 'other-bits']))
        with self.assertRaises(r.CheckError):
            r.inspect_container(m, 'api', metadata)

    def test_container_tag_health_labels_and_restart_are_required(self):
        metadata = {'reference': f'project1-ubuntu-api:{r.TARGET_TAG}'}
        baseline = ['sha256:' + SHA, metadata['reference'], 'true', 'false', 'healthy', r.PROJECT, 'api']
        for index, value in ((1, 'old-image'), (2, 'false'), (3, 'true'), (4, 'unhealthy'), (5, 'other-project'), (6, 'other-service')):
            fields = list(baseline)
            fields[index] = value
            m = types.SimpleNamespace(docker=Mock(return_value='|'.join(fields)))
            with self.subTest(index=index), self.assertRaises(r.CheckError):
                r.inspect_container(m, 'api', metadata)
            self.assertEqual(1, m.docker.call_count)

    def test_main_uses_existing_locks_and_redacts_unexpected_errors(self):
        m, _, _ = fixture()
        m.locks = Mock(return_value=contextlib.nullcontext())
        with patch.object(r, 'load_helper', return_value=m), patch.object(r, 'reconcile', side_effect=OSError('secret-fixture')), \
                contextlib.redirect_stderr(io.StringIO()) as output:
            self.assertEqual(1, r.main(['--check', '--backup-sha256', SHA, '--backup-bytes', '123']))
        m.locks.assert_called_once()
        self.assertNotIn('secret-fixture', output.getvalue())


if __name__ == '__main__':
    unittest.main()
