import { test, expect } from '@playwright/test';
import { authenticatedHousehold, manualRecipe, write } from './fixtures';

test('planned cooking generates an editable shopping snapshot with independent unknown amounts and reviewed reconciliation', async ({ page, browser }) => {
  await authenticatedHousehold(page);
  const ingredientResponse = await write(page, 'POST', '/api/v1/ingredients', { canonicalName: 'Salt' });
  expect(ingredientResponse.status()).toBe(201); const ingredientId = (await ingredientResponse.json()).id;
  const recipeResponse = await write(page, 'POST', '/api/v1/recipes', {
    ...manualRecipe(ingredientId), title: 'Salt recipe', acknowledgeUncertainIngredients: true,
    ingredients: [
      { position: 0, ingredientId, quantityText: '5', kind: 'numeric', unit: 'g', uncertain: false },
      { position: 1, ingredientId, quantityText: 'to taste', kind: 'unknown', unit: 'g', uncertain: true },
    ],
  });
  expect(recipeResponse.status()).toBe(201); const recipeId = (await recipeResponse.json()).id;
  const slots = await (await page.request.get('/api/v1/meal-slots')).json(); const slotId = slots.items.find((slot: { name: string }) => slot.name === 'Dinner').id;
  const cooking = await write(page, 'POST', '/api/v1/meals/cooking', { recipeId, date: '2026-11-02', slotId, plannedPrepared: '4', plannedEaten: '2' });
  expect(cooking.status()).toBe(201); const meal = await cooking.json();
  expect((await write(page, 'POST', '/api/v1/meals/leftovers', { sourceCookingEventId: meal.cookingEvent.id, sourceRevision: meal.cookingEvent.revision, date: '2026-11-03', slotId, plannedPortions: '2' })).status()).toBe(201);
  await page.goto('/shopping-lists');
  await page.getByRole('button', { name: 'New shopping list', exact: true }).click();
  await page.getByLabel('List name', { exact: true }).fill('Monday shopping');
  await page.getByLabel('From date', { exact: true }).fill('2026-11-02'); await page.getByLabel('Through date', { exact: true }).fill('2026-11-04');
  await page.getByRole('button', { name: 'Create shopping list', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Monday shopping', exact: true })).toBeVisible();
  const known = page.locator('[data-shopping-item]').filter({ hasText: '10 g' });
  const unknown = page.locator('[data-shopping-item]').filter({ hasText: 'to taste' });
  await expect(page.locator('[data-shopping-item]')).toHaveCount(2); await expect(unknown).toContainText('Amount unresolved');
  await known.getByRole('checkbox').click(); await expect(known.getByRole('checkbox')).toBeChecked();
  await expect(unknown.getByRole('checkbox')).not.toBeChecked(); await expect(unknown).toBeVisible();
  await known.getByRole('button', { name: /^Edit / }).click();
  await page.getByLabel('Shopping quantity', { exact: true }).fill('12.5');
  await page.getByRole('button', { name: 'Save item changes', exact: true }).click();
  const overridden = page.locator('[data-shopping-item]').filter({ hasText: '12.5 g' });
  await expect(overridden).toContainText('Your quantity'); await expect(overridden.getByRole('checkbox')).toBeChecked();
  await page.getByRole('button', { name: 'Add manual item', exact: true }).click();
  await page.getByLabel('Item name', { exact: true }).fill('Bread'); await page.getByRole('combobox', { name: 'Amount type', exact: true }).selectOption('numeric');
  await page.getByLabel('Manual item quantity', { exact: true }).fill('1');
  await page.getByRole('button', { name: 'Save manual item', exact: true }).click();
  await expect(page.locator('[data-shopping-item]').filter({ hasText: 'Bread' })).toContainText('Manual item');
  const currentMeal = await page.request.get(`/api/v1/meals/${meal.id}`);
  expect((await write(page, 'PUT', `/api/v1/meals/${meal.id}/cooking`, { recipeId, date: '2026-11-02', slotId, plannedPrepared: '6', plannedEaten: '2' }, currentMeal.headers()['etag'])).status()).toBe(200);
  await expect(overridden).toContainText('12.5 g');
  await page.getByRole('button', { name: 'Review updated requirements', exact: true }).click();
  const previewed = page.waitForResponse(response => response.request().method() === 'POST' && response.url().endsWith('/reconciliation/preview'));
  const previewButton = page.getByRole('button', { name: 'Preview requirements', exact: true });
  await previewButton.click();
  expect((await previewed).status()).toBe(200);
  await expect(previewButton).toBeEnabled();
  const preserved = page.locator('[data-review-key]').filter({ hasText: '15 g' });
  await expect(preserved).toContainText('12.5 g');
  await expect(preserved.getByRole('combobox', { name: 'Requirement decision', exact: true })).toHaveValue('keepUser');
  const reviewed = page.getByRole('checkbox', { name: 'I reviewed the latest requirements and choices', exact: true });
  if (await reviewed.count()) await reviewed.check();
  await page.getByRole('button', { name: 'Apply reviewed requirements', exact: true }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Reviewed requirements applied.' })).toBeVisible();
  await expect(overridden.getByRole('checkbox')).toBeChecked(); await expect(unknown).toBeVisible();
  const lists = await (await page.request.get('/api/v1/shopping-lists')).json();
  const detail = await (await page.request.get(`/api/v1/shopping-lists/${lists.items[0].id}`)).json();
  const row = detail.items.find((item: { hasOverride: boolean }) => item.hasOverride);
  expect(row.baselineExact).toEqual({ numerator: '15', denominator: '1' }); expect(row.effectiveExact).toEqual({ numerator: '25', denominator: '2' });
  expect(detail.summary.unresolvedCount).toBe(1); expect(detail.items.find((item: { manual: boolean }) => item.manual).label).toBe('Bread');
  const otherContext = await browser.newContext({ ignoreHTTPSErrors: true, baseURL: 'https://localhost:4201' });
  try {
    const other = await otherContext.newPage();
    expect((await write(other, 'POST', '/api/v1/auth/login', { email: 'admin@example.test', password: 'e2e-admin-test-passphrase' })).status()).toBe(200);
    await other.goto('/shopping-lists');
    await other.getByRole('button', { name: /^Monday shopping / }).click();
    const otherBread = other.locator('[data-shopping-item]').filter({ hasText: 'Bread' });
    await expect(otherBread.getByRole('checkbox')).not.toBeChecked();
    await otherBread.getByRole('checkbox').click();
    await expect(otherBread.getByRole('checkbox')).toBeChecked();
    await expect(page.locator('[data-shopping-item]').filter({ hasText: 'Bread' }).getByRole('checkbox')).toBeChecked({ timeout: 2000 });
    // A remote update cannot replace a local draft or silently advance its validator.
    await page.getByRole('button', { name: 'Add manual item', exact: true }).click();
    await page.getByLabel('Item name', { exact: true }).fill('Keep this draft');
    await otherBread.getByRole('checkbox').click();
    await expect(otherBread.getByRole('checkbox')).not.toBeChecked();
    await expect(page.getByText('This list changed on another device.', { exact: false })).toBeVisible({ timeout: 2000 });
    await expect(page.getByLabel('Item name', { exact: true })).toHaveValue('Keep this draft');
    page.once('dialog', dialog => dialog.accept());
    await page.getByRole('button', { name: 'Cancel editing', exact: true }).click();
  } finally { await otherContext.close(); }
  const ingredientPath = `/api/v1/ingredients/${ingredientId}`;
  const originalIngredient = await page.request.get(ingredientPath);
  const corrected = await write(page, 'PUT', ingredientPath, { canonicalName: 'Corrected salt' }, originalIngredient.headers()['etag']);
  expect(corrected.status()).toBe(200);
  expect((await write(page, 'DELETE', ingredientPath, undefined, corrected.headers()['etag'])).status()).toBe(409);
  await page.getByRole('button', { name: 'Refresh list', exact: true }).click();
  await expect(page.locator('[data-shopping-item]').filter({ hasText: '12.5 g' })).toContainText('Salt');
  await expect(page.locator('[data-shopping-item]').filter({ hasText: '12.5 g' })).not.toContainText('Corrected salt');

});
