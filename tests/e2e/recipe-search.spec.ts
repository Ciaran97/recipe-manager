import { test, expect } from '@playwright/test';
import { authenticatedHousehold, write, manualRecipe } from './fixtures';

test('shared organisers, multiple assignments, alias filters and safe organiser deletion', async ({ page }) => {
  await authenticatedHousehold(page);
  const ingredientResponse = await write(page, 'POST', '/api/v1/ingredients', { canonicalName: 'Rice' });
  expect(ingredientResponse.status()).toBe(201); const ingredient = await ingredientResponse.json();
  expect((await write(page, 'POST', `/api/v1/ingredients/${ingredient.id}/aliases`, { name: 'Wholegrain rice' }, ingredientResponse.headers()['etag'])).status()).toBe(200);
  await page.goto('/recipes'); await page.getByRole('link', { name: 'Collections and tags', exact: true }).click();
  await page.getByLabel('Name', { exact: true }).fill('Weeknight'); await page.getByRole('button', { name: 'Create', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Rename Weeknight', exact: true })).toBeVisible();
  await page.getByLabel('Name', { exact: true }).fill('WEEKNIGHT'); await page.getByRole('button', { name: 'Create', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('name'); await expect(page.getByLabel('Name', { exact: true })).toHaveValue('WEEKNIGHT');
  await page.getByLabel('Name', { exact: true }).fill('Weekend'); await page.getByRole('button', { name: 'Create', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Rename Weekend', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Tags', exact: true }).click();
  for (const name of ['Comfort', 'Fresh']) {
    await page.getByLabel('Name', { exact: true }).fill(name); await page.getByRole('button', { name: 'Create', exact: true }).click();
    await expect(page.getByRole('button', { name: `Rename ${name}`, exact: true })).toBeVisible();
  }
  await page.goto('/recipes/new'); await page.getByLabel('Title', { exact: true }).fill('Organised rice');
  await page.getByRole('combobox', { name: 'Ingredient name 1', exact: true }).fill('Rice'); await page.getByRole('option', { name: /^Rice/ }).click();
  await page.getByLabel('Quantity 1', { exact: true }).fill('1'); await page.getByLabel('Step 1', { exact: true }).fill('Cook rice.');
  await page.getByRole('button', { name: 'Choose collections and tags', exact: true }).click();
  for (const name of ['Weeknight', 'Weekend', 'Comfort', 'Fresh']) await page.getByRole('checkbox', { name, exact: true }).check();
  await page.getByRole('button', { name: 'Save recipe', exact: true }).click(); await expect(page.getByRole('heading', { name: 'Organised rice', exact: true })).toBeVisible();
  const id = page.url().split('/').at(-1); const saved = await (await page.request.get(`/api/v1/recipes/${id}`)).json();
  expect(saved.collectionIds).toHaveLength(2); expect(saved.tagIds).toHaveLength(2);
  expect((await write(page, 'POST', '/api/v1/recipes', { ...manualRecipe(ingredient.id), title: 'Unfiled rice' })).status()).toBe(201);
  await page.goto('/recipes'); await page.getByRole('button', { name: 'Filter by collections and tags', exact: true }).click();
  await page.getByRole('checkbox', { name: 'Weeknight', exact: true }).check(); await page.getByRole('checkbox', { name: 'Comfort', exact: true }).check();
  await page.getByLabel('Search recipes and ingredients').fill('WHOLEGRAIN'); await page.getByRole('button', { name: 'Search recipes', exact: true }).click();
  await expect(page.getByRole('link', { name: 'Organised rice', exact: true })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Unfiled rice', exact: true })).toHaveCount(0);
  await page.getByRole('link', { name: 'Collections and tags', exact: true }).click();
  page.once('dialog', dialog => dialog.accept()); await page.getByRole('button', { name: 'Delete Weeknight', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Rename Weeknight', exact: true })).toHaveCount(0);
  const retained = await (await page.request.get(`/api/v1/recipes/${id}`)).json(); expect(retained.title).toBe('Organised rice'); expect(retained.collectionIds).toHaveLength(1); expect(retained.tagIds).toHaveLength(2);
});
