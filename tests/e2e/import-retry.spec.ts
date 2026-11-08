import { test, expect } from '@playwright/test';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { authenticatedHousehold, write } from './fixtures';

test('day-six client retry resolves a durable handoff after a lost commit response and removed draft', async ({ page }) => {
  await authenticatedHousehold(page);
  const runtime = process.env.RECIPE_MANAGER_E2E_RUNTIME;
  if (!runtime) throw new Error('Use the isolated import fixture.');
  const baseUrl = fs.readFileSync(path.join(runtime, 'ai-origin'), 'utf8').trim();
  const text = fs.readFileSync(path.join(runtime, 'ai-recipe-text'), 'utf8');
  const settingsResponse = await page.request.get('/api/v1/household/settings'); const settings = await settingsResponse.json();
  expect((await write(page, 'PUT', '/api/v1/household/settings', { name: settings.name, timezone: settings.timezone, ai: { enabled: true, provider: 'OpenAICompatible', baseUrl, model: 'deterministic-fixture', clearApiKey: false } }, settingsResponse.headers()['etag'])).status()).toBe(200);
  for (const canonicalName of ['Rice', 'Salt']) expect((await write(page, 'POST', '/api/v1/ingredients', { canonicalName })).status()).toBe(201);
  const handoffId = crypto.randomUUID(); const capturedAt = new Date();
  expect((await write(page, 'POST', '/api/v1/import-handoffs', { handoffId, capturedAt: capturedAt.toISOString(), inputHash: crypto.createHash('sha256').update('text\0' + text).digest('hex') })).status()).toBe(201);
  const extracted = await write(page, 'POST', '/api/v1/imports/text', { text, handoffId }); expect(extracted.status()).toBe(201);
  const draft = (await extracted.json()).draft;
  await page.goto('/recipes/import?draft=' + draft.id);
  await expect(page.getByRole('heading', { name: 'Review imported recipe', exact: true })).toBeVisible();
  await page.getByLabel('Title', { exact: true }).fill('Recovered day-six rice');
  await page.getByLabel('Cup and spoon standard').selectOption('AU'); await page.getByLabel('Unit 2', { exact: true }).selectOption('item'); await page.getByLabel(/I have reviewed/).check();
  let committedId = ''; let handoffCommits = 0; let unboundCommits = 0; let lateDraftReads = 0; let committed = false;
  await page.route('**/api/v1/imports/*/commit', async route => { unboundCommits++; await route.continue(); });
  await page.route('**/api/v1/imports/' + draft.id, async route => { if (committed && route.request().method() === 'GET') lateDraftReads++; await route.continue(); });
  await page.route('**/api/v1/import-handoffs/' + handoffId + '/commit', async route => {
    handoffCommits++; const response = await route.fetch(); expect(response.status()).toBe(201); committedId = (await response.json()).recipeId; committed = true; await route.abort('failed');
  }, { times: 1 });
  await page.getByRole('button', { name: 'Save recipe', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Check previous save', exact: true })).toBeVisible();
  const committedDraft = await page.request.get('/api/v1/imports/' + draft.id);
  expect(committedDraft.status()).toBe(200);
  expect((await write(page, 'DELETE', '/api/v1/imports/' + draft.id, undefined, committedDraft.headers()['etag'])).status()).toBe(204);
  expect((await page.request.get('/api/v1/imports/' + draft.id)).status()).toBe(410); // Exercise the same missing-draft condition as cleanup.
  const latestSettings = await page.request.get('/api/v1/household/settings'); const current = await latestSettings.json();
  expect((await write(page, 'PUT', '/api/v1/household/settings', { name: current.name, timezone: current.timezone, ai: { enabled: false, provider: 'OpenAICompatible', baseUrl, model: 'deterministic-fixture', clearApiKey: false } }, latestSettings.headers()['etag'])).status()).toBe(200);
  // The UI sees day six. Server-side time/cleanup and receipt retention are covered separately
  // by the PostgreSQL MutableClock acceptance tests; this does not pretend to advance that clock.
  await page.clock.setSystemTime(new Date(capturedAt.getTime() + 6 * 86400_000));
  await page.getByRole('button', { name: 'Check previous save', exact: true }).click();
  await expect(page).toHaveURL(new RegExp('/recipes/' + committedId + '$'));
  await expect(page.getByRole('heading', { name: 'Recovered day-six rice', exact: true })).toBeVisible();
  expect(handoffCommits).toBe(1); expect(unboundCommits).toBe(0); expect(lateDraftReads).toBe(0);
  const recipes = (await (await page.request.get('/api/v1/recipes')).json()).items;
  expect(recipes).toHaveLength(1); expect(recipes[0].id).toBe(committedId);
  const receipt = await (await page.request.get('/api/v1/import-handoffs/' + handoffId)).json(); expect(receipt.result.recipeId).toBe(committedId);
});
