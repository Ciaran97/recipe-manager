import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const directory = path.dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
const selected = args.filter(arg => arg.endsWith('.spec.ts'));
const options = args.filter(arg => !selected.includes(arg));
const external = process.env.RECIPE_MANAGER_EXTERNAL_E2E === '1';
const files = selected.length ? selected : fs.readdirSync(directory).filter(name => name.endsWith('.spec.ts') && !name.startsWith('cross-client')).sort();
for (const file of files) {
  if (path.basename(file) !== file || !fs.existsSync(path.join(directory, file))) throw new Error('Select an existing E2E spec filename.');
  // Each feature gets an installation of its own. No production reset endpoint or shared test data.
  const runtime = external ? undefined : fs.mkdtempSync(path.join(os.tmpdir(), 'recipe-manager-browser-e2e-'));
  if (runtime) fs.chmodSync(runtime, 0o700);
  try {
    const result = spawnSync(process.execPath, [path.join(directory, 'node_modules/@playwright/test/cli.js'), 'test', file, ...options], {
      cwd: directory, stdio: 'inherit', env: { ...process.env, ...(runtime ? { RECIPE_MANAGER_E2E_RUNTIME: runtime } : {}) },
    });
    if (result.status !== 0) { process.exitCode = result.status ?? 1; break; }
  } finally { if (runtime) fs.rmSync(runtime, { recursive: true, force: true }); }
}
