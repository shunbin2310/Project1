"""Explicit reconciliation of ONE reviewed, already-running failed activation.

No up/down/restart/reload, SQL writes, env edits or migration. --check is read-only;
--apply additionally preserves the failed state and records pending verification.
Install root-owned at /usr/local/libexec/project1-docker-recheck.py and run -I -B.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import secrets
import stat
import sys
import time

INSTALLED = Path('/usr/local/libexec/project1-docker-recheck.py')
HELPER = Path('/usr/local/libexec/project1-docker.py')
HELPER_SHA = '09e2a057a08c2b10b65d77ea3aaa45c6ecdc97d8a0162b58fdd952ea8bf058df'
TARGET_TAG = 'prod-20261019t131132z-d37cfd3d'
PROJECT = 'project1-ubuntu-production'


class CheckError(RuntimeError):
    pass


def require(ok, message):
    if not ok:
        raise CheckError(message)


def digest(path):
    value = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            value.update(block)
    return value.hexdigest()


def trusted_file(path):
    for parent in path.parents:
        info = parent.lstat()
        require(stat.S_ISDIR(info.st_mode) and info.st_uid == 0 and not info.st_mode & 0o022,
                'Untrusted administrative path.')
    info = path.lstat()
    require(stat.S_ISREG(info.st_mode) and info.st_uid == 0 and info.st_nlink == 1
            and not info.st_mode & 0o022, 'Untrusted administrative file.')


def load_helper():
    require(sys.platform == 'linux' and os.geteuid() == 0 and sys.flags.isolated == 1
            and Path(__file__).absolute() == INSTALLED, 'Run only the root-installed recheck with sudo python3 -I -B.')
    trusted_file(INSTALLED)
    trusted_file(HELPER)
    require(digest(HELPER) == HELPER_SHA, 'Installed cutover helper is not the reviewed version.')
    sys.dont_write_bytecode = True
    spec = importlib.util.spec_from_file_location('reviewed_cutover', HELPER)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    module.check_host()
    return module


def gate(label, action):
    try:
        value = action()
    except Exception:
        raise CheckError(label + ' failed; raw details withheld. No automatic retry.') from None
    print(label + ': PASS')
    return value


def inspect_container(m, service, metadata, name=None):
    name = name or f'{PROJECT}-{service}-1'
    template = ('{{.Image}}|{{.Config.Image}}|{{.State.Running}}|{{.State.Restarting}}|'
                '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}|'
                '{{index .Config.Labels "com.docker.compose.project"}}|'
                '{{index .Config.Labels "com.docker.compose.service"}}')
    fields = m.docker('inspect', '--type', 'container', '--format', template, name).split('|')
    require(len(fields) == 7 and re.fullmatch(r'sha256:[0-9a-f]{64}', fields[0]), 'Unexpected container metadata.')
    require(fields[1:] == [metadata['reference'], 'true', 'false', 'healthy', PROJECT, service],
            'Container reference/health/project/service differs from reviewed deployment.')
    # Some image stores expose a config ID for a container and an index ID for a
    # tagged image. Compare the resolved runnable layers/config, not unlike IDs.
    fingerprint = '{{json .RootFS.Layers}}|{{json .Config}}|{{.Os}}|{{.Architecture}}'
    actual = m.docker('image', 'inspect', '--format', fingerprint, fields[0])
    expected = m.docker('image', 'inspect', '--format', fingerprint, metadata['reference'])
    require(actual == expected, 'Running container image content differs from reviewed reference.')


def reconcile(m, sha256, size, apply=False):
    require(re.fullmatch(r'[0-9a-f]{64}', sha256 or '') and isinstance(size, int) and size > 0,
            'Provide the off-server verified backup SHA256 and byte count.')
    state = m.read_state()
    require(state['phase'] == 'activation-failed' and state['tag'] == TARGET_TAG,
            'Only the reviewed failed activation is eligible; do not reset another phase/tag.')
    state_hash = m.digest(m.STATE_FILE)
    folder, manifest = gate('Sealed release and loaded images', lambda: m.release(state))
    require(manifest['files_sha256']['ubuntu_production.py'] == HELPER_SHA, 'Release helper differs from reviewed version.')
    gate('Verified backup acknowledgement and retained backup', lambda: m.backup_matches(state, sha256, size))
    gate('Retained legacy files', lambda: m.old_files(state))
    gate('Legacy API stopped/disabled', m.old_stopped)
    gate('Nginx syntax, ownership and site link', m.site_contract)
    require(m.digest(m.OLD_SITE) == manifest['files_sha256']['project1-docker.conf'], 'Live Nginx site differs from sealed production site.')
    m.root_file(m.ROOT / 'old-nginx.conf', private=True)
    require(m.digest(m.ROOT / 'old-nginx.conf') == state['old_nginx_sha256'], 'Legacy Nginx snapshot changed.')
    config = m.read_config(state['tag'])  # Server-local only; never printed or saved by this tool.
    gate('Original database permissions/schema/no Pending mail', lambda: m.runtime_check(config, manifest))
    require(set(m.compose(folder, 'ps', '--status', 'running', '--services').split()) == {'api', 'frontend'},
            'Expected exactly two running production services.')
    for service in ('api', 'frontend'):
        gate(service + ' running image/health/labels', lambda s=service: inspect_container(m, s, manifest['images'][s]))
    for sample in range(3):
        gate(f'Docker URL sample {sample + 1}', lambda: m.web_check(8084, manifest['frontend_sha256']))
        gate(f'Original URL sample {sample + 1}', lambda: m.web_check(80, manifest['frontend_sha256']))
        if sample < 2:
            time.sleep(1)
    gate('Final database check', lambda: m.runtime_check(config, manifest))
    gate('Final legacy API check', m.old_stopped)
    require(m.digest(m.STATE_FILE) == state_hash, 'State changed during reconciliation; do not overwrite it.')
    if not apply:
        print('Read-only checks passed. Failed state unchanged; business verification is NOT complete.')
        return
    history = m.STATE_DIR / 'history'
    m.directory(history)
    audit = history / f'activation-failed-{TARGET_TAG}-{secrets.token_hex(8)}.json'
    m.write_new(audit, (json.dumps(state, sort_keys=True, indent=2) + '\n').encode())
    updated = dict(state, phase='active-pending-verification', activation_recheck={
        'previous_phase': 'activation-failed', 'audit_file': str(audit),
        'checked_utc': time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()),
        'initial_failure_cause': 'undetermined',
    })
    m.save_state(updated)
    print('Recorded active-pending-verification; original failure preserved. Initial failure cause remains undetermined.')
    print('No application restart, Nginx change, env edit, migration or business-data write. Verify workflow/PDF/Gmail before finalize.')


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    group = parser.add_mutually_exclusive_group()
    group.add_argument('--check', action='store_true')
    group.add_argument('--apply', action='store_true')
    parser.add_argument('--backup-sha256')
    parser.add_argument('--backup-bytes', type=int)
    args = parser.parse_args(argv)
    if not (args.check or args.apply):
        print('Preview only. --check verifies without state changes; --apply repeats checks and records pending verification. No service changes.')
        return 0
    try:
        m = load_helper()
        with m.locks():
            reconcile(m, args.backup_sha256, args.backup_bytes, args.apply)
        return 0
    except CheckError as error:
        print('STOP: ' + str(error), file=sys.stderr)
    except Exception:
        print('STOP: file/lock/state/response check failed; details withheld. Check status; do not blindly retry.', file=sys.stderr)
    return 1


if __name__ == '__main__':
    sys.exit(main())
