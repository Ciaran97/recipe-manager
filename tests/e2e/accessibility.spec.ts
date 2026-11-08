import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import fs from "node:fs";
import { authenticatedHousehold, manualRecipe, write } from "./fixtures";

test("core screens support 320px and desktop layouts, labelled controls and keyboard entry", async ({
  page,
}, testInfo) => {
  test.setTimeout(240_000);
  await authenticatedHousehold(page);
  page.on("dialog", (dialog) => dialog.accept());
  const ingredient = await write(page, "POST", "/api/v1/ingredients", {
    canonicalName: "Rice",
  });
  expect(ingredient.status()).toBe(201);
  const ingredientId = (await ingredient.json()).id;
  const longTitle = "Weeknight rice " + "ExtraLongRecipeName".repeat(8);
  const recipe = await write(page, "POST", "/api/v1/recipes", {
    ...manualRecipe(ingredientId),
    title: longTitle,
  });
  expect(recipe.status()).toBe(201);
  const recipeId = (await recipe.json()).id;
  const slots = await (await page.request.get("/api/v1/meal-slots")).json();
  expect(
    (
      await write(page, "POST", "/api/v1/meals/cooking", {
        recipeId,
        date: "2026-11-02",
        slotId: slots.items.find((x: { name: string }) => x.name === "Dinner")
          .id,
        plannedPrepared: "4",
        plannedEaten: "2",
      })
    ).status(),
  ).toBe(201);
  const list = await write(page, "POST", "/api/v1/shopping-lists/generate", {
    name: "Accessibility shopping",
    selection: { kind: "recipes", entries: [{ recipeId, servings: "4" }] },
  });
  expect(list.status()).toBe(201);
  const listId = (await list.json()).id;
  expect(
    (
      await write(
        page,
        "POST",
        `/api/v1/shopping-lists/${listId}/items`,
        {
          label: "Household item " + "VeryLongShoppingLabel".repeat(6),
          quantityText: "1",
          kind: "numeric",
          unit: "item",
        },
        list.headers()["etag"],
      )
    ).status(),
  ).toBe(201);
  const screens = [
    "/recipes",
    `/recipes/${recipeId}`,
    "/recipes/new",
    "/recipes/import",
    "/recipes/organisers",
    "/ingredients",
    "/meal-planner",
    "/shopping-lists",
    "/settings",
  ];
  for (const theme of ["light", "dark"]) {
    await page.goto("/recipes");
    await page
      .getByRole("combobox", { name: "Appearance", exact: true })
      .selectOption(theme);
    for (const width of [320, 1280]) {
      await page.setViewportSize({ width, height: 960 });
      for (const screen of screens) {
        await page.goto(screen);
        await expect(page.locator("main")).toBeVisible();
        if (screen === "/shopping-lists")
          await page
            .getByRole("button", { name: /^Accessibility shopping/ })
            .click();
        if (screen === "/meal-planner") {
          await page
            .getByLabel("Week containing", { exact: true })
            .fill("2026-11-02");
          await page
            .getByLabel("Week containing", { exact: true })
            .press("Tab");
        }
        await expect(
          page.getByText("Loading your household…", { exact: true }),
        ).toHaveCount(0);
        await expect(
          page.locator("main").getByRole("heading").first(),
        ).toBeVisible();
        if (screen === "/recipes/organisers")
          await expect(
            page.getByRole("button", { name: "Create", exact: true }),
          ).toBeVisible();
        const results = await new AxeBuilder({ page })
          .withTags(["wcag2a", "wcag2aa", "wcag21aa", "wcag22aa"])
          .analyze();
        expect(
          results.violations.map((x) => ({
            id: x.id,
            impact: x.impact,
            nodes: x.nodes.map((n) => n.target),
          })),
          `${screen} ${theme} at ${width}px`,
        ).toEqual([]);
        expect(
          await page.evaluate(
            () => document.documentElement.scrollWidth <= window.innerWidth + 1,
          ),
          `${screen} ${theme} horizontal overflow at ${width}px`,
        ).toBe(true);
        fs.mkdirSync(testInfo.outputDir, { recursive: true, mode: 0o700 });
        fs.chmodSync(testInfo.outputDir, 0o700);
        const screenshot = testInfo.outputPath(
          screen.replaceAll("/", "_") + "-" + theme + "-" + width + ".png",
        );
        await page.screenshot({ path: screenshot, fullPage: true });
        fs.chmodSync(screenshot, 0o600);
      }
    }
  }
  await page.goto("/recipes");
  await page.keyboard.press("Tab");
  await expect(
    page.getByRole("link", { name: "Skip to content", exact: true }),
  ).toBeFocused();
  await page.keyboard.press("Enter");
  await expect(page.locator("main")).toBeFocused();
  const create = page.getByRole("link", { name: "New recipe", exact: true });
  for (
    let index = 0;
    index < 20 &&
    !(await create.evaluate((element) => element === document.activeElement));
    index++
  )
    await page.keyboard.press("Tab");
  await expect(create).toBeFocused();
  await page.keyboard.press("Enter");
  await expect(page).toHaveURL(/\/recipes\/new$/);
  await expect(page.getByLabel("Title", { exact: true })).toBeVisible();
});
