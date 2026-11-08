#!/usr/bin/env python3
"""Back up and update an existing external-proxy installation from a GHCR pre-release."""
import argparse
from contextlib import contextmanager
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def run(*args, output=None, live=False):
    result = subprocess.run(list(args), check=True, stdout=None if live else (output or subprocess.PIPE), text=output is None)
    return result.stdout.strip() if output is None and not live else None


@contextmanager
def update_lock(root):
    lock = root / 'deploy' / '.update-lock'
    lock.mkdir(mode=0o700)  # Atomic; refuse concurrent updates, including while pulling.
    try:
        yield
    finally:
        lock.rmdir()


def update(root, image, backup_directory):
    os.umask(0o077)
    env_file = root / 'deploy' / '.env'
    if not env_file.is_file():
        raise ValueError('No existing installation: deploy/.env is required.')
    # This updater deliberately leaves separately managed Caddy/cloudflared alone.
    compose = ['docker', 'compose', '--env-file', str(env_file), '-f', str(root / 'deploy/compose.yaml')]
    home = root / 'deploy/compose.home.yaml'
    if home.exists():
        compose += ['-f', str(home)]
    # Installations using the bundled TLS overlay follow the manual upgrade guide.
    configuration = json.loads(run(*compose, 'config', '--format', 'json'))
    old_container = run(*compose, 'ps', '-q', 'app')
    if not old_container or '\n' in old_container:
        raise ValueError('Exactly one running app is required before updating.')
    old_image = run('docker', 'inspect', old_container, '--format', '{{.Image}}')
    run('docker', 'pull', image, live=True)
    digests = json.loads(run('docker', 'image', 'inspect', image, '--format', '{{json .RepoDigests}}'))
    repository = image.rsplit(':', 1)[0]
    pinned = next(value for value in digests if value.startswith(repository + '@sha256:'))
    # Allocate a unique private destination before stopping writes.
    backup_directory.mkdir(parents=True, exist_ok=True, mode=0o700)
    backup = Path(tempfile.mkdtemp(prefix=datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ-'), dir=backup_directory))
    print(f'Backup: {backup}', flush=True)
    run(*compose, 'stop', 'app')
    try:
        with (backup / 'database.dump').open('wb') as output:
            run(*compose, 'exec', '-T', 'db', 'pg_dump', '-U', 'recipe_manager', '-d', 'recipe_manager', '-Fc', output=output)
        # Verify archive readability without writing to the live database.
        with (backup / 'database.dump').open('rb') as source:
            subprocess.run([*compose, 'exec', '-T', 'db', 'pg_restore', '--list'], stdin=source, stdout=subprocess.DEVNULL, check=True)
        for name in ('images', 'keys'):
            run('docker', 'cp', f'{old_container}:/var/lib/recipe-manager/{name}', str(backup / name))
        shutil.copy2(env_file, backup / 'install.env')
        if home.exists():
            shutil.copy2(home, backup / 'compose.home.yaml')
        # Retain the exact old manifest and all file-backed secrets, including custom paths.
        (backup / 'compose.json').write_text(json.dumps(configuration, indent=2) + '\n')
        secret_dir = backup / 'secrets'
        secret_dir.mkdir()
        for name, secret in configuration['secrets'].items():
            shutil.copy2(secret['file'], secret_dir / name)
        (backup / 'application-image-id').write_text(old_image + '\n')
        with (backup / 'application-image.tar').open('wb') as output:
            run('docker', 'image', 'save', old_image, output=output)
        (backup / 'backup-complete').write_text('Archive readability checked; perform isolated restore drills separately.\n')
        # Both commands use the same immutable image; never build on the server.
        os.environ['RECIPE_MANAGER_IMAGE'] = pinned
        run(*compose, 'run', '--rm', '--no-deps', 'migrator', live=True)
        # Persist selection before startup so future Compose runs cannot select the old schema's image.
        lines = [line for line in env_file.read_text().splitlines() if not line.startswith('RECIPE_MANAGER_IMAGE=')]
        temporary = env_file.with_name('.env.update')
        temporary.write_text('\n'.join(lines) + f"\nRECIPE_MANAGER_IMAGE='{pinned}'\n")
        temporary.chmod(0o600)
        temporary.replace(env_file)
        run(*compose, 'up', '-d', '--no-deps', '--no-build', '--wait', 'app', live=True)
    except BaseException:
        # A failed readiness check may leave a running, unhealthy app; pause it too.
        subprocess.run([*compose, 'stop', 'app'], check=False)
        print(f'Update failed; app left stopped. Preserve {backup}. Do not automatically roll back a migrated database.', flush=True)
        raise
    print(f'Update ready: {pinned}\nBackup retained: {backup}', flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('version', help='Published pre-release tag, for example v0.1.0-beta.1')
    parser.add_argument('--backup-directory', type=Path, default=Path.home() / '.local/share/recipe-manager/backups')
    args = parser.parse_args()
    if not re.fullmatch(r'[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}', args.version) or args.version == 'latest':
        parser.error('Choose an explicit published release tag; latest is not supported.')
    os.chdir(ROOT)
    try:
        with update_lock(ROOT):
            update(ROOT, 'ghcr.io/ciaran97/recipe-manager:' + args.version, args.backup_directory.resolve())
    except (OSError, ValueError, StopIteration, subprocess.CalledProcessError) as error:
        raise SystemExit(f'Update stopped: {error}') from error


if __name__ == '__main__':
    main()
