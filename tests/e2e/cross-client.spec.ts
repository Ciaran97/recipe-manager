import { test, expect } from '@playwright/test';
import https from 'node:https';
import fs from 'node:fs';
import { timingSafeEqual } from 'node:crypto';
import { write } from './fixtures';

// Started only by the signed native fixture. This bounded, localhost-only controller drives a
// real Angular browser; it provides no application endpoints, reset operations or domain mocks.
test('Angular and native shopping edits converge through the shared backend', async ({ page }) => {
  test.setTimeout(420_000);
  const runtime = process.env.RECIPE_MANAGER_CROSS_RUNTIME;
  const token = process.env.RECIPE_MANAGER_CROSS_TOKEN;
  const ready = process.env.RECIPE_MANAGER_CROSS_READY;
  if (!runtime || !token || !ready) throw new Error('Run the signed CrossClientUITests fixture.');
  let finish!: () => void;
  let fail!: (error: Error) => void;
  const completed = new Promise<void>((resolve, reject) => { finish = resolve; fail = reject; });
  let busy = false;
  let opened = false;
  let commitAt: number | undefined;
  const samples: number[] = [];
  const server = https.createServer({ cert: fs.readFileSync(runtime + '/localhost.pem'), key: fs.readFileSync(runtime + '/localhost.key') }, async (request, response) => {
    const supplied = Buffer.from(String(request.headers['x-test-control-token'] ?? ''));
    const expected = Buffer.from(token);
    if (supplied.length !== expected.length || !timingSafeEqual(supplied, expected)) { response.writeHead(403).end(); return; }
    if (request.method === 'GET' && request.url === '/ready') { response.writeHead(200).end(); return; }
    if (request.method !== 'POST' || request.url !== '/step' || busy) { response.writeHead(409).end(); return; }
    busy = true;
    try {
      const chunks: Buffer[] = []; let length = 0;
      for await (const chunk of request) { length += chunk.length; if (length > 4096) throw new Error('Controller request too large.'); chunks.push(Buffer.from(chunk)); }
      const input = JSON.parse(Buffer.concat(chunks).toString('utf8')) as { command: string; listId?: string; itemId?: string; purchased?: boolean };
      if (input.command !== 'done' && (!/^[0-9a-f-]{36}$/i.test(input.listId ?? '') || !/^[0-9a-f-]{36}$/i.test(input.itemId ?? ''))) throw new Error('Invalid fixture resource identifiers.');
      if (input.command === 'open') {
        expect((await write(page, 'POST', '/api/v1/auth/login', { email: 'admin@example.test', password: 'e2e-admin-test-passphrase' })).status()).toBe(200);
        await page.goto('/shopping-lists');
        await page.getByRole('button', { name: /^Cross-client shopping / }).click();
        await expect(page.locator(`[data-shopping-item="${input.itemId}"]`)).toBeVisible();
        opened = true;
      } else if (input.command === 'expect') {
        if (!opened || typeof input.purchased !== 'boolean') throw new Error('Observer is not ready.');
        const checkbox = page.locator(`[data-shopping-item="${input.itemId}"]`).getByRole('checkbox');
        if (input.purchased) await expect(checkbox).toBeChecked({ timeout: 2000 });
        else await expect(checkbox).not.toBeChecked({ timeout: 2000 });
      } else if (input.command === 'purchase') {
        if (!opened || typeof input.purchased !== 'boolean') throw new Error('Writer is not ready.');
        const checkbox = page.locator(`[data-shopping-item="${input.itemId}"]`).getByRole('checkbox');
        expect(await checkbox.isChecked()).not.toBe(input.purchased);
        const acknowledged = page.waitForResponse(result => result.request().method() === 'PUT' && result.url().endsWith(`/shopping-lists/${input.listId}/items/${input.itemId}`));
        await checkbox.click();
        expect((await acknowledged).status()).toBe(200); commitAt = performance.now();
        if (input.purchased) await expect(checkbox).toBeChecked(); else await expect(checkbox).not.toBeChecked();
      } else if (input.command === 'native-observed') {
        if (commitAt === undefined) throw new Error('No Angular write acknowledgement to measure.');
        samples.push(performance.now() - commitAt); commitAt = undefined;
      } else if (input.command === 'done') {
        if (samples.length < 1) throw new Error('No bidirectional observer result.');
        await test.info().attach('angular-to-native-observer-latency', { body: JSON.stringify({ milliseconds: samples, clock: 'controller monotonic', scope: 'incremental fixture; not the MP-SYNC-01 p95 gate' }), contentType: 'application/json' });
        response.writeHead(200, { 'Content-Type': 'application/json' }).end('{}'); finish(); return;
      } else throw new Error('Unknown fixture command.');
      response.writeHead(200, { 'Content-Type': 'application/json' }).end('{}');
    } catch (error) {
      response.writeHead(500).end(); fail(error instanceof Error ? error : new Error('Controller failed.'));
    } finally { busy = false; }
  });
  try {
    await new Promise<void>((resolve, reject) => { server.once('error', reject); server.listen(7291, '127.0.0.1', resolve); });
    fs.writeFileSync(ready, 'ready', { mode: 0o600 });
    await completed;
  } finally { await new Promise<void>(resolve => server.close(() => resolve())); }
});
