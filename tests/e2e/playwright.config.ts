import { defineConfig, devices } from '@playwright/test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const directory = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(directory, '../..');
const runtime = process.env.RECIPE_MANAGER_E2E_RUNTIME ?? path.join(directory, '.runtime');
const quote = (value: string) => "'" + value.replaceAll("'", "'\\'" + "'") + "'";

export default defineConfig({
  testDir: directory, testMatch: '*.spec.ts', fullyParallel: false, workers: 1, timeout: 90_000,
  expect: { timeout: 15_000 },
  use: { baseURL: 'https://localhost:4201', ignoreHTTPSErrors: true, trace: 'retain-on-failure' },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: process.env.RECIPE_MANAGER_EXTERNAL_E2E === '1' ? undefined : [
    { command: `dotnet run --project tests/RecipeManager.E2EHost --no-launch-profile -- ${quote(runtime)} 7285`, cwd: root,
      url: 'https://localhost:7285/api/v1/setup', ignoreHTTPSErrors: true, reuseExistingServer: false, timeout: 120_000 },
    { command: `npm start --prefix src/RecipeManager.Web -- --port 4201 --ssl-cert ${quote(path.join(runtime, 'localhost.pem'))} --ssl-key ${quote(path.join(runtime, 'localhost.key'))}`, cwd: root,
      url: 'https://localhost:4201', ignoreHTTPSErrors: true, reuseExistingServer: false, timeout: 120_000,
      env: { RECIPE_MANAGER_API_ORIGIN: 'https://localhost:7285', NODE_EXTRA_CA_CERTS: path.join(runtime, 'localhost.pem') } },
  ],
});
