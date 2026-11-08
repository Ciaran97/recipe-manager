#!/usr/bin/env python3
"""Opt-in real update test; uses a disposable local registry and uniquely owned Compose project.

Usage: python3 scripts/test-update-server-docker.py recipe-manager:implementation
Requires an already-built, host-compatible application image. Never uses deploy/.env.
Preserves stopped test volumes and private artifacts for inspection; removes only its registry.
"""
import importlib.util
import os
import re
from pathlib import Path
import secrets
import shutil
import subprocess
import sys
import tempfile
import time

SOURCE = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('updater', SOURCE / 'scripts/update-server.py')
updater = importlib.util.module_from_spec(spec)
spec.loader.exec_module(updater)


def run(*args):
    return subprocess.check_output(args, text=True).strip()


def main():
    if len(sys.argv) != 2:
        raise SystemExit(__doc__)
    os.umask(0o077)
    os.environ.pop('RECIPE_MANAGER_IMAGE', None)
    project = 'rm-validation-' + secrets.token_hex(4)
    root = Path(tempfile.mkdtemp(prefix=project + '-'))
    deploy = root / 'deploy'
    deploy.mkdir()
    shutil.copy2(SOURCE / 'deploy/compose.yaml', deploy / 'compose.yaml')
    compose = ['docker', 'compose', '--env-file', str(deploy / '.env'), '-f', str(deploy / 'compose.yaml'), '-f', str(deploy / 'compose.home.yaml')]
    registry = project + '-registry'
    try:
        run('python3', str(SOURCE / 'scripts/configure-self-hosting.py'), 'https://localhost', '--directory', str(deploy), '--project', project)
        with (deploy / '.env').open('a') as output:
            output.write(f"RECIPE_MANAGER_IMAGE='{sys.argv[1]}'\n")
        (deploy / 'compose.home.yaml').write_text('services:\n  app:\n    ports: !override\n      - "127.0.0.1::8080"\n')
        subprocess.run([*compose, 'up', '-d', '--no-build', '--wait'], check=True)
        run(*compose, 'exec', '-T', 'db', 'psql', '-U', 'recipe_manager', '-d', 'recipe_manager', '-c', "CREATE TABLE update_probe (value text); INSERT INTO update_probe VALUES ('retained');")
        run(*compose, 'exec', '-T', 'app', '/bin/sh', '-c', 'printf retained > /var/lib/recipe-manager/images/update-probe')
        # Bind inside the daemon's network namespace. Docker Desktop's host-side
        # loopback port forwarding is not necessarily reachable from its daemon VM.
        run('docker', 'run', '-d', '--name', registry, '--network', 'host',
            '-e', 'REGISTRY_HTTP_ADDR=127.0.0.1:0', 'registry:2')
        port = None
        for _ in range(100):
            logs = subprocess.run(['docker', 'logs', registry], capture_output=True, text=True, check=True)
            match = re.search(r'listening on 127\.0\.0\.1:(\d+)', logs.stdout + logs.stderr)
            if match:
                port = match[1]
                break
            time.sleep(0.1)
        if port is None:
            raise RuntimeError('Local registry did not start')
        image = f'127.0.0.1:{port}/recipe-manager:test'
        run('docker', 'tag', sys.argv[1], image)
        run('docker', 'push', image)
        with updater.update_lock(root):
            updater.update(root, image, root / 'backups')
        value = run(*compose, 'exec', '-T', 'db', 'psql', '-U', 'recipe_manager', '-d', 'recipe_manager', '-Atc', 'SELECT value FROM update_probe')
        assert value == 'retained', value
        assert run(*compose, 'exec', '-T', 'app', 'cat', '/var/lib/recipe-manager/images/update-probe') == 'retained'
        backup, = (root / 'backups').iterdir()
        assert (backup / 'images/update-probe').read_text() == 'retained'
        assert (backup / 'backup-complete').is_file()
        # Restore the generated dump into a separate database, never over the running app database.
        run(*compose, 'exec', '-T', 'db', 'createdb', '-U', 'recipe_manager', 'update_restore_probe')
        with (backup / 'database.dump').open('rb') as source:
            subprocess.run([*compose, 'exec', '-T', 'db', 'pg_restore', '-U', 'recipe_manager', '-d', 'update_restore_probe', '--exit-on-error'], stdin=source, check=True)
        assert run(*compose, 'exec', '-T', 'db', 'psql', '-U', 'recipe_manager', '-d', 'update_restore_probe', '-Atc', 'SELECT value FROM update_probe') == 'retained'
        print('PASS: real pull, backup, migration, readiness, data preservation and database restore.')
    finally:
        if (deploy / '.env').exists() and (deploy / 'compose.home.yaml').exists():
            subprocess.run([*compose, 'stop'], check=False, stdout=subprocess.DEVNULL)
        subprocess.run(['docker', 'rm', '-f', registry], check=False, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        print(f'Private test artifacts and stopped volumes retained: {root} ({project})')


if __name__ == '__main__':
    main()
