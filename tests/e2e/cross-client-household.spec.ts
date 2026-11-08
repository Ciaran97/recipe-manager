import { test, expect } from '@playwright/test';

test('native profile update is visible through Angular and the same household API', async ({ page }) => {
  test.skip(process.env.RECIPE_MANAGER_EXTERNAL_E2E !== '1', 'Runs after the native journey in its owned disposable fixture.');
  await page.goto('/login');
  await page.getByLabel('Email', { exact: true }).fill('admin@example.test');
  await page.getByLabel('Password', { exact: true }).fill('e2e-admin-test-passphrase');
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page).toHaveURL(/\/recipes$/);
  await page.getByRole('link', { name: 'Settings', exact: true }).click();
  await expect(page.getByRole('listitem').filter({ hasText: 'Updated on iOS' })).toBeVisible();
});
