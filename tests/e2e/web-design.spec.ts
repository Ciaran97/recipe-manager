import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import fs from 'node:fs';
import { authenticatedHousehold, manualRecipe, write } from './fixtures';

test('appearance, responsive navigation and reference-inspired recipe browsing', async ({
  page,
}, testInfo) => {
  test.setTimeout(120_000);
  await page.emulateMedia({ colorScheme: 'light' });
  await authenticatedHousehold(page);
  const ingredient = await (
    await write(page, 'POST', '/api/v1/ingredients', { canonicalName: 'Design fixture ingredient' })
  ).json();
  const names = ['Breakfast', 'Lunch', 'Dinner', 'Sides', 'Desserts', 'Weekend cooking'];
  const collections: { id: string; name: string }[] = [];
  for (const name of names) {
    const response = await write(page, 'POST', '/api/v1/collections', { name });
    expect(response.status()).toBe(201);
    collections.push(await response.json());
  }
  const titles = [
    'Lemon & herb roast chicken',
    'Roasted tomato pasta',
    'Green goddess salad',
    'Sunday morning pancakes',
    'Crispy mushroom toast',
    'One-pot vegetable curry',
    'Slow-cooked beef ragu',
    'Baked lemon cheesecake',
  ];
  for (const [index, title] of titles.entries()) {
    const response = await write(page, 'POST', '/api/v1/recipes', {
      ...manualRecipe(ingredient.id),
      title,
      prepMinutes: 10,
      cookMinutes: 15 + index * 5,
      collectionIds: [collections[index % collections.length].id],
    });
    expect(response.status()).toBe(201);
    if (index === 0) {
      const recipe = await response.json();
      expect(
        (
          await write(
            page,
            'PUT',
            `/api/v1/recipes/${recipe.id}/favourite`,
            { favourite: true },
            response.headers()['etag'],
          )
        ).status(),
      ).toBe(200);
    }
  }
  await page.goto('/recipes');
  await page.setViewportSize({ width: 1600, height: 1100 });
  await expect(page.getByRole('heading', { name: 'All recipes', exact: true })).toBeVisible();
  await expect(page.locator('.recipe-card')).toHaveCount(8);
  await expect(
    page
      .getByRole('navigation', { name: 'Main navigation' })
      .getByRole('link', { name: 'Breakfast', exact: true }),
  ).toBeVisible();
  fs.mkdirSync('/tmp/recipe-manager-design-previews', { recursive: true });
  for (const theme of ['light', 'dark']) {
    await page.getByRole('combobox', { name: 'Appearance', exact: true }).selectOption(theme);
    await expect(page.locator('html')).toHaveAttribute('data-theme', theme);
    const result = await new AxeBuilder({ page })
      .withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa'])
      .analyze();
    expect(
      result.violations.map((v) => ({
        id: v.id,
        targets: v.nodes.map((n) => ({ target: n.target, summary: n.failureSummary })),
      })),
    ).toEqual([]);
    const screenshot = `/tmp/recipe-manager-design-previews/recipes-${theme}-desktop.png`;
    await page.screenshot({ path: screenshot, fullPage: true });
    await testInfo.attach(`Recipes ${theme}`, { path: screenshot, contentType: 'image/png' });
  }
  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  await page.getByRole('link', { name: 'Favourites', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Favourites', exact: true })).toBeVisible();
  await expect(page.locator('.recipe-card')).toHaveCount(1);
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('link', { name: 'Breakfast', exact: true })
    .click();
  await expect(page.getByRole('heading', { name: 'Breakfast', exact: true })).toBeVisible();
  await expect(page.locator('.recipe-card')).toHaveCount(2);
  await page.getByRole('link', { name: 'Recipes', exact: true }).click();
  await page.getByRole('searchbox', { name: 'Search all recipes', exact: true }).fill('cheesecake');
  await page.getByRole('searchbox', { name: 'Search all recipes', exact: true }).press('Enter');
  await expect(page.locator('.recipe-card')).toHaveCount(1);
  await expect(
    page.getByRole('link', { name: 'Baked lemon cheesecake', exact: true }),
  ).toBeVisible();
  await page.goto('/recipes');
  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByRole('button', { name: 'Toggle navigation', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Navigation', exact: true })).toBeVisible();
  await expect(
    page
      .getByRole('dialog', { name: 'Navigation', exact: true })
      .getByRole('button', { name: 'Close navigation', exact: true }),
  ).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('button', { name: 'Toggle navigation', exact: true })).toBeFocused();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  for (const theme of ['light', 'dark']) {
    await page.getByRole('combobox', { name: 'Appearance', exact: true }).selectOption(theme);
    await page.screenshot({
      path: `/tmp/recipe-manager-design-previews/recipes-${theme}-mobile.png`,
    });
  }
  await page.getByRole('combobox', { name: 'Appearance', exact: true }).selectOption('system');
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
  await page.emulateMedia({ colorScheme: 'dark' });
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
});
