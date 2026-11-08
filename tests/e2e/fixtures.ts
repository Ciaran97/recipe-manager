import { expect, type Page, type APIResponse } from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export async function write(page: Page, method: 'POST' | 'PUT' | 'DELETE', url: string, data?: unknown, etag?: string): Promise<APIResponse> {
  const csrf = await (await page.request.get('/api/v1/auth/csrf')).json();
  return page.request.fetch(url, { method, data, headers: { 'X-XSRF-TOKEN': csrf.token, ...(etag ? { 'If-Match': etag } : {}) } });
}
export async function authenticatedHousehold(page: Page): Promise<void> {
  const runtime = process.env.RECIPE_MANAGER_E2E_RUNTIME ?? path.join(path.dirname(fileURLToPath(import.meta.url)), '.runtime');
  expect((await (await page.request.get('/api/v1/setup')).json()).setupRequired).toBe(true);
  const result = await write(page, 'POST', '/api/v1/setup', {
    setupToken: fs.readFileSync(path.join(runtime, 'setup-token'), 'utf8').trim(), email: 'admin@example.test',
    password: 'e2e-admin-test-passphrase', householdName: 'Feature household', timezone: 'Australia/Perth',
  });
  expect(result.status()).toBe(201);
}
export function manualRecipe(ingredientId: string) {
  return { title: 'Pepper salad', defaultServings: '2', measurementStandard: 'AU', groups: [],
    ingredients: [{ position: 0, ingredientId, quantityText: '1', kind: 'numeric', unit: 'item', uncertain: false }],
    instructions: [{ position: 0, text: 'Slice and serve.' }], collectionIds: [], tagIds: [], acknowledgeUncertainIngredients: false };
}
