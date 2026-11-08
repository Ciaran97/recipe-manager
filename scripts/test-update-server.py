#!/usr/bin/env python3
"""Exercise update failure boundaries without accessing Docker or a household installation."""
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('updater', Path(__file__).with_name('update-server.py'))
updater = importlib.util.module_from_spec(spec)
spec.loader.exec_module(updater)
IMAGE = 'ghcr.io/ciaran97/recipe-manager:v0.1.0-beta.1'
PINNED = 'ghcr.io/ciaran97/recipe-manager@sha256:' + 'a' * 64


class UpdateTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        (self.root / 'deploy').mkdir()
        self.env = self.root / 'deploy/.env'
        self.env.write_text("RECIPE_MANAGER_PUBLIC_ORIGIN='https://recipe.example.com'\n")
        (self.root / 'deploy/compose.home.yaml').write_text('services: {}\n')
        self.secret = self.root / 'database-password'
        self.secret.write_text('private-fixture')
        self.calls = []
        self.failure = None
        self.addCleanup(os.umask, os.umask(0o077))
        self.environment = patch.dict(os.environ)
        self.environment.start()
        self.addCleanup(self.environment.stop)

    def fake_run(self, *args, **kwargs):
        self.calls.append(args)
        if self.failure and self.failure in args:
            raise subprocess.CalledProcessError(1, args)
        if 'config' in args:
            return json.dumps({'secrets': {'postgres_password': {'file': str(self.secret)}}})
        if 'ps' in args:
            return 'old-container'
        if args[:2] == ('docker', 'inspect'):
            return 'sha256:old-image'
        if args[:3] == ('docker', 'image', 'inspect'):
            return json.dumps([PINNED])
        if args[:2] == ('docker', 'cp'):
            Path(args[-1]).mkdir()
        if kwargs.get('output') is not None:
            kwargs['output'].write(b'fixture archive')
        return ''

    def execute(self):
        with patch.object(updater, 'run', side_effect=self.fake_run), patch.object(updater.subprocess, 'run') as process:
            updater.update(self.root, IMAGE, self.root / 'backups')
            return process

    def started(self):
        return any('up' in args for args in self.calls)

    def test_success_keeps_backup_and_pins_app_and_migrator(self):
        self.execute()
        self.assertTrue(self.started())
        self.assertIn(PINNED, self.env.read_text())
        backup, = (self.root / 'backups').iterdir()
        self.assertNotIn(PINNED, (backup / 'install.env').read_text())
        self.assertEqual((backup / 'secrets/postgres_password').read_text(), 'private-fixture')
        self.assertTrue((backup / 'application-image.tar').stat().st_size)
        self.assertTrue((backup / 'backup-complete').exists())
        for args in self.calls:
            if 'compose' in args:
                self.assertIn(str(self.root / 'deploy/compose.home.yaml'), args)
        self.assertEqual(os.environ['RECIPE_MANAGER_IMAGE'], PINNED)

    def test_pull_failure_does_not_stop_app(self):
        self.failure = 'pull'
        with self.assertRaises(subprocess.CalledProcessError):
            self.execute()
        self.assertFalse(any('stop' in args for args in self.calls))

    def test_backup_failure_prevents_migration_and_restart(self):
        self.failure = 'pg_dump'
        with self.assertRaises(subprocess.CalledProcessError):
            self.execute()
        self.assertFalse(self.started())
        self.assertFalse(any('migrator' in args for args in self.calls))
        self.assertNotIn('RECIPE_MANAGER_IMAGE', self.env.read_text())

    def test_archive_verification_failure_prevents_migration(self):
        def fail_validation(*args, **kwargs):
            if 'pg_restore' in args[0]:
                raise subprocess.CalledProcessError(1, args[0])
        with patch.object(updater, 'run', side_effect=self.fake_run), patch.object(updater.subprocess, 'run', side_effect=fail_validation):
            with self.assertRaises(subprocess.CalledProcessError):
                updater.update(self.root, IMAGE, self.root / 'backups')
        self.assertFalse(any('migrator' in args for args in self.calls))
        self.assertFalse(self.started())

    def test_migration_failure_leaves_old_configuration_and_no_restart(self):
        self.failure = 'migrator'
        with self.assertRaises(subprocess.CalledProcessError):
            self.execute()
        self.assertFalse(self.started())
        self.assertNotIn('RECIPE_MANAGER_IMAGE', self.env.read_text())

    def test_readiness_failure_stops_app_and_keeps_new_pin_for_recovery(self):
        self.failure = 'up'
        with patch.object(updater, 'run', side_effect=self.fake_run), patch.object(updater.subprocess, 'run') as process:
            with self.assertRaises(subprocess.CalledProcessError):
                updater.update(self.root, IMAGE, self.root / 'backups')
        self.assertEqual(process.call_args.args[0][-2:], ['stop', 'app'])
        self.assertIn(PINNED, self.env.read_text())

    def test_concurrent_update_is_refused(self):
        with updater.update_lock(self.root):
            with self.assertRaises(FileExistsError):
                with updater.update_lock(self.root):
                    self.fail('Concurrent updater acquired the lock')
        self.assertFalse((self.root / 'deploy/.update-lock').exists())


if __name__ == '__main__':
    unittest.main()
