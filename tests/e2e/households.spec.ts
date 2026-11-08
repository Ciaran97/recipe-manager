import { test, expect, type Page } from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const runtime = process.env.RECIPE_MANAGER_E2E_RUNTIME ?? path.join(path.dirname(fileURLToPath(import.meta.url)), '.runtime');
const administrator = { email: 'admin@example.test', password: 'e2e-admin-test-passphrase' };
async function login(page: Page) {
  await page.goto('/login');
  await page.getByLabel('Email', { exact: true }).fill(administrator.email);
  await page.getByLabel('Password', { exact: true }).fill(administrator.password);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page).toHaveURL(/\/recipes$/);
}

test('setup, invitation, shared membership permissions and all-client logout', async ({ page, browser }) => {
  const setup = await page.request.get('/api/v1/setup');
  expect((await setup.json()).setupRequired).toBe(true);
  {
    await page.goto('/');
    await expect(page.getByRole('heading', { name: 'Create your household' })).toBeVisible();
    await page.getByLabel('Household name', { exact: true }).fill('E2E household');
    await page.getByLabel('Household time zone', { exact: true }).fill('Australia/Perth');
    await page.getByLabel('Operator setup secret').fill(fs.readFileSync(path.join(runtime, 'setup-token'), 'utf8').trim());
    await page.getByLabel('Email', { exact: true }).fill(administrator.email);
    await page.getByLabel('Password', { exact: true }).fill(administrator.password);
    await page.getByLabel('Confirm password', { exact: true }).fill(administrator.password);
    await page.getByRole('button', { name: 'Create household', exact: true }).click();
    await expect(page).toHaveURL(/\/recipes$/);
  }
  await page.getByRole('link', { name: 'Settings', exact: true }).click();
  await page.getByRole('button', { name: 'Create invitation', exact: true }).click();
  const link = await page.getByLabel('Invitation link', { exact: true }).inputValue();
  expect(new URL(link).origin).toBe('https://localhost:4201');
  const memberContext = await browser.newContext({ ignoreHTTPSErrors: true });
  const member = await memberContext.newPage();
  await member.goto(link);
  await expect(member).toHaveURL('https://localhost:4201/invite');
  await member.getByLabel('Your name (optional)').fill('Member Two');
  await member.getByLabel('Email', { exact: true }).fill('member@example.test');
  await member.getByLabel('Password', { exact: true }).fill('e2e-member-test-passphrase');
  await member.getByLabel('Confirm password', { exact: true }).fill('e2e-member-test-passphrase');
  await member.getByRole('button', { name: 'Join household', exact: true }).click();
  await expect(member).toHaveURL(/\/recipes$/);
  await member.getByRole('link', { name: 'Settings', exact: true }).click();
  await expect(member.getByRole('button', { name: 'Save profile', exact: true })).toBeVisible();
  await expect(member.getByRole('button', { name: 'Create invitation', exact: true })).toHaveCount(0);
  const csrf = await (await member.request.get('/api/v1/auth/csrf')).json();
  expect((await member.request.post('/api/v1/household/invitations', { data: {}, headers: { 'X-XSRF-TOKEN': csrf.token } })).status()).toBe(403);
  await page.getByRole('button', { name: 'Reload and discard unsaved changes' }).click();
  const row = page.getByRole('listitem').filter({ hasText: 'Member Two' });
  await row.getByRole('button', { name: /Remove access/ }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Member access revoked' })).toBeVisible();
  await member.reload();
  await expect(member).toHaveURL(/\/login$/);
  const secondContext = await browser.newContext({ ignoreHTTPSErrors: true });
  const second = await secondContext.newPage();
  await login(second);
  await page.getByRole('button', { name: 'Sign out all sessions', exact: true }).click();
  await expect(page).toHaveURL(/\/login$/);
  await second.reload();
  await expect(second).toHaveURL(/\/login$/);
  await memberContext.close(); await secondContext.close();
});
