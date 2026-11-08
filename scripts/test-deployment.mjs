#!/usr/bin/env node
// Only operates on an owned, disposable loopback validation installation. Never resets data.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import https from 'node:https';
import crypto from 'node:crypto';
import { deflateSync, crc32 } from 'node:zlib';
import { createRequire } from 'node:module';

const directory = path.resolve(process.argv[2] ?? '');
const configuration = fs.readFileSync(path.join(directory, '.env'), 'utf8');
assert.match(configuration, /^COMPOSE_PROJECT_NAME='rm-validation-[a-f0-9]{8}'$/m, 'Use an owned disposable validation project.');
const origin = configuration.match(/^RECIPE_MANAGER_PUBLIC_ORIGIN='([^']+)'$/m)?.[1];
assert.ok(origin && new URL(origin).protocol === 'https:' && new URL(origin).hostname === 'localhost', 'Only a verified loopback TLS fixture is supported.');
const ca = fs.readFileSync(path.join(directory, 'root.crt'));
const recordFile = path.join(directory, 'verification.json');
const cookies = new Map();
async function request(method, resource, body, etag, token, mediaType = 'application/json') {
  assert.ok(resource.startsWith('/'));
  const bytes = body === undefined ? undefined : Buffer.isBuffer(body) ? body : Buffer.from(JSON.stringify(body));
  return new Promise((resolve, reject) => {
    const outgoing = https.request(new URL(resource, origin), { method, ca, timeout: 15000,
      headers: { 'Accept-Encoding': resource.startsWith('/api/') ? 'gzip, deflate, br' : 'identity', Origin: origin, Cookie: [...cookies.values()].join('; '), ...(bytes ? { 'Content-Type': mediaType, 'Content-Length': bytes.length } : {}), ...(etag ? { 'If-Match': etag } : {}), ...(token ? { 'X-XSRF-TOKEN': token } : {}), ...(method === 'POST' ? { 'Idempotency-Key': crypto.randomUUID() } : {}) } }, response => {
      if (resource.startsWith('/api/') && response.headers['content-encoding']) {
        response.resume(); reject(new Error('API compression must not rewrite opaque ETag validators.')); return;
      }
      for (const value of response.headers['set-cookie'] ?? []) { const pair = value.split(';')[0]; cookies.set(pair.slice(0, pair.indexOf('=')), pair); }
      const chunks = []; response.on('data', chunk => chunks.push(chunk)); response.on('end', () => {
        const bytes = Buffer.concat(chunks); const text = bytes.toString('utf8'); let json; try { json = JSON.parse(text); } catch { /* Static HTML is expected for the client. */ }
        resolve({ status: response.statusCode, headers: response.headers, text, json, bytes });
      });
    });
    outgoing.on('timeout', () => outgoing.destroy(new Error('Deployment request timed out.'))); outgoing.on('error', reject);
    if (bytes) outgoing.write(bytes); outgoing.end();
  });
}
async function read(resource, status = 200) { const result = await request('GET', resource); assert.equal(result.status, status, resource); return result; }
async function write(method, resource, body, status = 200, etag, mediaType = 'application/json') {
  const csrf = await read('/api/v1/auth/csrf');
  const result = await request(method, resource, body, etag, csrf.json.token, mediaType);
  assert.equal(result.status, status, method + ' ' + resource + ' ' + (result.json?.code ?? '')); return result;
}
await read('/health/live'); await read('/health/ready');
assert.match((await read('/recipes')).text, /<app-root/);
if (process.argv.includes('--verify')) {
  const record = JSON.parse(fs.readFileSync(recordFile, 'utf8'));
  if (process.argv.includes('--restore-session')) {
    assert.ok(record.cookies?.length, 'Save a test session before restarting.');
    for (const [name, value] of record.cookies) cookies.set(name, value);
    await read('/api/v1/auth/me');
  }
  await write('POST', '/api/v1/auth/login', { email: record.email, password: record.password });
  if (process.argv.includes('--exercise-routing')) {
    await write('POST', '/api/v1/auth/register', {}, 404);
    const slotId = (await read('/api/v1/meal-slots')).json.items.find(x => x.name === 'Dinner').id;
    const temporary = await write('POST', '/api/v1/meals/other', { date: '2026-11-05', slotId, label: 'Disposable routing check' }, 201);
    await write('DELETE', '/api/v1/meals/' + temporary.json.id, undefined, 204, temporary.headers.etag);
    await read('/api/v1/meals/' + temporary.json.id, 404);
  }
  if (process.argv.includes('--exercise-protection')) {
    const settings = await read('/api/v1/household/settings');
    await write('PUT', '/api/v1/household/settings', { name: settings.json.name, timezone: settings.json.timezone, ai: {
      enabled: false, provider: 'OpenAICompatible', model: 'disabled-fixture', baseUrl: 'https://provider.example.test/v1', apiKey: crypto.randomBytes(32).toString('hex'), clearApiKey: false
    } }, 200, settings.headers.etag);
    record.providerProtected = true;
  }
  if (record.providerProtected) { const settings = (await read('/api/v1/household/settings')).json; assert.equal(settings.ai.enabled, false); assert.equal(settings.ai.hasApiKey, true); }

  if (process.argv.includes('--exercise-image')) {
    const chunk = (name, data) => { const type = Buffer.from(name), size = Buffer.alloc(4), checksum = Buffer.alloc(4); size.writeUInt32BE(data.length); checksum.writeUInt32BE(crc32(Buffer.concat([type, data]))); return Buffer.concat([size, type, data, checksum]); };
    const header = Buffer.alloc(13); header.writeUInt32BE(2); header.writeUInt32BE(2, 4); header[8] = 8; header[9] = 6;
    const row = Buffer.from([0, 60, 120, 60, 255, 60, 120, 60, 255]);
    const png = Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', header), chunk('IDAT', deflateSync(Buffer.concat([row, row]))), chunk('IEND', Buffer.alloc(0))]);
    const boundary = 'fixture-' + crypto.randomUUID();
    const multipart = Buffer.concat([Buffer.from('--' + boundary + '\r\nContent-Disposition: form-data; name="file"; filename="fixture.png"\r\nContent-Type: image/png\r\n\r\n'), png, Buffer.from('\r\n--' + boundary + '--\r\n')]);
    const image = (await write('POST', '/api/v1/images', multipart, 201, undefined, 'multipart/form-data; boundary=' + boundary)).json;
    const current = await read('/api/v1/recipes/' + record.recipeId), value = current.json;
    await write('PUT', '/api/v1/recipes/' + record.recipeId, {
      title: value.title, defaultServings: value.defaultServings, measurementStandard: value.measurementStandard,
      groups: value.groups.map(({ id, title, position }) => ({ id, title, position })),
      ingredients: value.ingredients.map(({ id, position, ingredientId, quantityText, kind, unit, preparation, groupId, uncertain }) => ({ id, position, ingredientId, quantityText, kind, unit, preparation, groupId, uncertain })),
      instructions: value.instructions.map(({ id, position, text }) => ({ id, position, text })), collectionIds: value.collectionIds, tagIds: value.tagIds,
      imageAssetId: image.id, acknowledgeUncertainIngredients: true
    }, 200, current.headers.etag);
    const stored = await read('/api/v1/images/' + image.id); assert.equal(stored.headers['content-type'], 'image/png');
    record.imageId = image.id; record.imageHash = crypto.createHash('sha256').update(stored.bytes).digest('hex');
    fs.writeFileSync(recordFile, JSON.stringify(record), { mode: 0o600 });
  }
  const recipe = (await read('/api/v1/recipes/' + record.recipeId)).json; assert.equal(recipe.title, 'Deployment rice');
  if (record.imageId) { assert.equal(recipe.imageAssetId, record.imageId); assert.equal(crypto.createHash('sha256').update((await read('/api/v1/images/' + record.imageId)).bytes).digest('hex'), record.imageHash); }
  if (process.argv.includes('--signalr')) {
    const { HubConnectionBuilder, LogLevel } = createRequire(import.meta.url)('../src/RecipeManager.Web/node_modules/@microsoft/signalr');
    const hub = new HubConnectionBuilder().withUrl(origin + '/hubs/shopping', { headers: { Cookie: [...cookies.values()].join('; '), Origin: origin } }).configureLogging(LogLevel.None).build();
    let received; const notification = new Promise(resolve => { received = resolve; }); hub.on('HouseholdChanged', () => received());
    try {
      await hub.start(); await hub.invoke('SubscribeHousehold');
      const list = await read('/api/v1/shopping-lists/' + record.listId);
      await write('PUT', '/api/v1/shopping-lists/' + record.listId + '/items/' + record.manualId, { purchased: true }, 200, list.headers.etag);
      let timer;
      try { await Promise.race([notification, new Promise((_, reject) => { timer = setTimeout(() => reject(new Error('No household notification through TLS proxy.')), 10000); })]); } finally { clearTimeout(timer); }
    } finally { await hub.stop(); }
    console.log('Verified authenticated SignalR household notification through the TLS proxy.');
  }
  const list = (await read('/api/v1/shopping-lists/' + record.listId)).json;
  assert.equal(list.items.find(x => x.id === record.purchasedId).purchased, true);
  assert.equal(list.items.find(x => x.id === record.manualId).label, 'Bread');
  assert.equal((await read('/api/v1/cooking-events/' + record.sourceId + '/leftovers')).json.availablePortions, '0');
  record.cookies = [...cookies]; fs.writeFileSync(recordFile, JSON.stringify(record), { mode: 0o600 });
  console.log('Verified HTTPS, login, recipes, leftovers and manual/purchased shopping state after restart/restore.');
} else {
  assert.equal((await read('/api/v1/setup')).json.setupRequired, true, 'Refuse to write into an existing installation.');
  assert.equal(fs.existsSync(recordFile), false, 'Never overwrite validation credentials.');
  const email = 'deployment@example.test', password = crypto.randomBytes(36).toString('base64url');
  await write('POST', '/api/v1/setup', { setupToken: fs.readFileSync(path.join(directory, 'secrets/production/setup-token'), 'utf8').trim(), email, password, householdName: 'Disposable deployment', timezone: 'Australia/Perth' }, 201);
  const rice = (await write('POST', '/api/v1/ingredients', { canonicalName: 'Rice' }, 201)).json;
  const salt = (await write('POST', '/api/v1/ingredients', { canonicalName: 'Salt' }, 201)).json;
  const recipeBody = (title, ingredients) => ({ title, defaultServings: '2', measurementStandard: 'AU', groups: [], ingredients, instructions: [{ position: 0, text: 'Cook and serve.' }], collectionIds: [], tagIds: [], acknowledgeUncertainIngredients: true });
  const recipe = (await write('POST', '/api/v1/recipes', recipeBody('Deployment rice', [
    { position: 0, ingredientId: rice.id, quantityText: '150', kind: 'numeric', unit: 'g' },
    { position: 1, ingredientId: rice.id, quantityText: 'as needed', kind: 'unknown', unit: 'g', uncertain: true },
    { position: 2, ingredientId: salt.id, quantityText: 'to taste', kind: 'unknown', unit: 'item', uncertain: true }]), 201)).json;
  const second = (await write('POST', '/api/v1/recipes', recipeBody('Wednesday rice', [{ position: 0, ingredientId: rice.id, quantityText: '0.2', kind: 'numeric', unit: 'kg' }]), 201)).json;
  const slotId = (await read('/api/v1/meal-slots')).json.items.find(x => x.name === 'Dinner').id;
  const cooking = (await write('POST', '/api/v1/meals/cooking', { recipeId: recipe.id, date: '2026-11-02', slotId, plannedPrepared: '4', plannedEaten: '2' }, 201)).json;
  const balance = (await read('/api/v1/cooking-events/' + cooking.cookingEvent.id + '/leftovers')).json;
  await write('POST', '/api/v1/meals/leftovers', { sourceCookingEventId: cooking.cookingEvent.id, sourceRevision: balance.sourceRevision, date: '2026-11-03', slotId, plannedPortions: '2' }, 201);
  await write('POST', '/api/v1/meals/cooking', { recipeId: second.id, date: '2026-11-04', slotId, plannedPrepared: '2', plannedEaten: '2' }, 201);
  const generated = await write('POST', '/api/v1/shopping-lists/generate', { name: 'Deployment shopping', selection: { kind: 'mealRange', from: '2026-11-02', to: '2026-11-04' }, basis: 'planned' }, 201);
  const known = generated.json.items.find(x => x.ingredientId === rice.id);
  assert.deepEqual(known.effectiveExact, { numerator: '500', denominator: '1' }); assert.equal(known.additionalAmountUnresolved, true);
  assert.equal(generated.json.items.find(x => x.ingredientId === salt.id).unresolved, true);
  const listPath = '/api/v1/shopping-lists/' + generated.json.id;
  const purchased = await write('PUT', listPath + '/items/' + known.id, { purchased: true }, 200, generated.headers.etag);
  await write('POST', listPath + '/items', { label: 'Bread', quantityText: '1', kind: 'numeric', unit: 'item' }, 201, purchased.headers.etag);
  const after = (await read(listPath)).json; const manualId = after.items.find(x => x.manual).id;
  fs.writeFileSync(recordFile, JSON.stringify({ email, password, recipeId: recipe.id, sourceId: cooking.cookingEvent.id, listId: generated.json.id, purchasedId: known.id, manualId, cookies: [...cookies] }), { mode: 0o600, flag: 'wx' });
  console.log('Verified deployed HTTPS setup, manual recipes, explicit leftovers, exact 500 g consolidation, visible unknowns and editable shopping snapshots with AI disabled.');
}
