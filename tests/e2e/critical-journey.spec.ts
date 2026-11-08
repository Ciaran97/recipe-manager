import { test, expect } from "@playwright/test";
import fs from "node:fs";
import path from "node:path";
import { authenticatedHousehold, manualRecipe, write } from "./fixtures";

test("paste, organise, plan explicit leftovers, shop and reconcile a reviewed recipe snapshot", async ({
  page,
}) => {
  await authenticatedHousehold(page);
  const runtime = process.env.RECIPE_MANAGER_E2E_RUNTIME;
  if (!runtime) throw new Error("Use the isolated import acceptance fixture.");
  const settings = await page.request.get("/api/v1/household/settings");
  const household = await settings.json();
  expect(
    (
      await write(
        page,
        "PUT",
        "/api/v1/household/settings",
        {
          name: household.name,
          timezone: household.timezone,
          ai: {
            enabled: true,
            provider: "OpenAICompatible",
            baseUrl: fs
              .readFileSync(path.join(runtime, "ai-origin"), "utf8")
              .trim(),
            model: "deterministic-fixture",
            apiKey: null,
            clearApiKey: false,
          },
        },
        settings.headers()["etag"],
      )
    ).status(),
  ).toBe(200);
  let riceId = "";
  for (const canonicalName of ["Rice", "Salt"]) {
    const result = await write(page, "POST", "/api/v1/ingredients", {
      canonicalName,
    });
    expect(result.status()).toBe(201);
    if (canonicalName === "Rice") riceId = (await result.json()).id;
  }
  for (const [kind, name] of [
    ["collections", "Weeknight"],
    ["tags", "Comfort"],
  ])
    expect(
      (await write(page, "POST", "/api/v1/" + kind, { name })).status(),
    ).toBe(201);
  const wednesday = await write(page, "POST", "/api/v1/recipes", {
    ...manualRecipe(riceId),
    title: "Wednesday rice",
    ingredients: [
      {
        position: 0,
        ingredientId: riceId,
        quantityText: "0.2",
        kind: "numeric",
        unit: "kg",
        uncertain: false,
      },
    ],
  });
  expect(wednesday.status()).toBe(201);
  const wednesdayId = (await wednesday.json()).id;
  await page.goto("/recipes/import");
  await page.getByLabel("Import method").selectOption("text");
  await page
    .getByLabel("Recipe text", { exact: true })
    .fill(fs.readFileSync(path.join(runtime, "ai-recipe-text"), "utf8"));
  await page
    .getByRole("button", { name: "Import for review", exact: true })
    .click();
  await expect(
    page.getByRole("heading", { name: "Review imported recipe", exact: true }),
  ).toBeVisible();
  await page.getByLabel("Title", { exact: true }).fill("Monday rice");
  await page.getByLabel("Cup and spoon standard").selectOption("AU");
  await page.getByLabel("Quantity 1", { exact: true }).fill("150");
  await page.getByLabel("Unit 1", { exact: true }).selectOption("g");
  await page.getByLabel("Unit 2", { exact: true }).selectOption("item");
  await page
    .getByRole("button", { name: "Choose collections and tags", exact: true })
    .click();
  for (const name of ["Weeknight", "Comfort"])
    await page.getByRole("checkbox", { name, exact: true }).check();
  await page.getByLabel(/I have reviewed/).check();
  await page.getByRole("button", { name: "Save recipe", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "Monday rice", exact: true }),
  ).toBeVisible();
  const mondayId = page.url().split("/").at(-1)!;
  const saved = await (
    await page.request.get("/api/v1/recipes/" + mondayId)
  ).json();
  expect(saved.collectionIds).toHaveLength(1);
  expect(saved.tagIds).toHaveLength(1);
  await page.goto("/meal-planner");
  await page.getByLabel("Week containing", { exact: true }).fill("2026-11-02");
  await page.getByLabel("Week containing", { exact: true }).press("Tab");
  for (const [recipeId, date, prepared] of [
    [mondayId, "2026-11-02", "4"],
    [wednesdayId, "2026-11-04", "2"],
  ]) {
    await page
      .getByRole("button", { name: "Cook a recipe", exact: true })
      .click();
    await page
      .getByRole("combobox", { name: "Recipe", exact: true })
      .selectOption(recipeId);
    await page.getByLabel("Meal date", { exact: true }).fill(date);
    await page
      .getByLabel("Portions to prepare", { exact: true })
      .fill(prepared);
    await page
      .getByLabel("Portions to eat at this meal", { exact: true })
      .fill("2");
    await page.getByRole("button", { name: "Save meal", exact: true }).click();
    await expect(
      page.getByRole("status").filter({ hasText: "Meal plan updated." }),
    ).toBeVisible();
  }
  const calendar = await (
    await page.request.get("/api/v1/meals?from=2026-11-02&to=2026-11-04")
  ).json();
  const monday = calendar.items.find(
    (x: { date: string }) => x.date === "2026-11-02",
  );
  await page
    .getByRole("button", { name: "Schedule leftovers", exact: true })
    .click();
  await page
    .getByRole("combobox", { name: "Source cooking event", exact: true })
    .selectOption(monday.cookingEvent.id);
  await page
    .getByLabel("Leftover meal date", { exact: true })
    .fill("2026-11-03");
  await page.getByLabel("Planned leftover portions", { exact: true }).fill("2");
  await page
    .getByRole("button", { name: "Schedule leftover meal", exact: true })
    .click();
  await expect(
    page.getByRole("status").filter({ hasText: "Meal plan updated." }),
  ).toBeVisible();
  await page.goto("/shopping-lists");
  await page
    .getByRole("button", { name: "New shopping list", exact: true })
    .click();
  await page.getByLabel("List name", { exact: true }).fill("Critical week");
  await page.getByLabel("From date", { exact: true }).fill("2026-11-02");
  await page.getByLabel("Through date", { exact: true }).fill("2026-11-04");
  await page
    .getByRole("button", { name: "Create shopping list", exact: true })
    .click();
  const rice = page
    .locator("[data-shopping-item]")
    .filter({ hasText: "500 g" });
  await expect(rice).toBeVisible();
  await expect(page.locator("[data-shopping-item]")).toHaveCount(2);
  await expect(
    page.locator("[data-shopping-item]").filter({ hasText: "to taste" }),
  ).toContainText("Amount unresolved");
  const listId = (
    await (await page.request.get("/api/v1/shopping-lists")).json()
  ).items[0].id;
  expect(
    (await (await page.request.get("/api/v1/meals/" + monday.id)).json())
      .cookingEvent.actualPrepared,
  ).toBeNull();
  await rice.getByRole("checkbox").click();
  await expect(rice.getByRole("checkbox")).toBeChecked();
  await rice.getByRole("button", { name: /^Edit / }).click();
  await page.getByLabel("Shopping quantity", { exact: true }).fill("600");
  await page
    .getByRole("button", { name: "Save item changes", exact: true })
    .click();
  await expect(page.getByRole("heading", { name: "Edit shopping item", exact: true })).toBeHidden();
  await page
    .getByRole("button", { name: "Add manual item", exact: true })
    .click();
  await page.getByLabel("Item name", { exact: true }).fill("Bread");
  await page
    .getByRole("combobox", { name: "Amount type", exact: true })
    .selectOption("numeric");
  await page.getByLabel("Manual item quantity", { exact: true }).fill("1");
  await page
    .getByRole("button", { name: "Save manual item", exact: true })
    .click();
  await page.goto("/recipes/" + mondayId + "/edit");
  await page.getByLabel("Quantity 1", { exact: true }).fill("175");
  await page.getByLabel(/I have reviewed/).check();
  await page.getByRole("button", { name: "Save recipe", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "Monday rice", exact: true }),
  ).toBeVisible();
  const frozen = await (
    await page.request.get("/api/v1/meals/" + monday.id)
  ).json();
  expect(frozen.cookingEvent.recipeSnapshot.ingredients[0].quantityText).toBe(
    "150",
  );
  await page.goto("/meal-planner");
  await page.getByLabel("Week containing", { exact: true }).fill("2026-11-02");
  await page.getByLabel("Week containing", { exact: true }).press("Tab");
  await page
    .locator(`[data-meal-id="${monday.id}"]`)
    .getByRole("button", { name: /^Cooking and leftovers/ })
    .click();
  await page
    .getByRole("button", { name: "Review latest recipe", exact: true })
    .click();
  await page
    .getByRole("button", { name: "Apply reviewed recipe refresh", exact: true })
    .click();
  await expect(
    page.getByRole("status").filter({ hasText: "Meal plan updated." }),
  ).toBeVisible();
  await page.goto("/shopping-lists");
  await page.getByRole("button", { name: /^Critical week / }).click();
  await expect(
    page.locator("[data-shopping-item]").filter({ hasText: "600 g" }),
  ).toBeVisible();
  await page
    .getByRole("button", { name: "Review updated requirements", exact: true })
    .click();
  const previewed = page.waitForResponse(response => response.request().method() === "POST" && response.url().endsWith("/reconciliation/preview"));
  await page
    .getByRole("button", { name: "Preview requirements", exact: true })
    .click();
  expect((await previewed).status()).toBe(200);
  await expect(page.getByRole("button", { name: "Preview requirements", exact: true })).toBeEnabled();
  await expect(
    page.locator("[data-review-key]").filter({ hasText: "550 g" }),
  ).toContainText("600 g");
  const reviewed = page.getByRole("checkbox", {
    name: "I reviewed the latest requirements and choices",
    exact: true,
  });
  if (await reviewed.count()) await reviewed.check();
  await page
    .getByRole("button", { name: "Apply reviewed requirements", exact: true })
    .click();
  await expect(
    page
      .getByRole("status")
      .filter({ hasText: "Reviewed requirements applied." }),
  ).toBeVisible();
  const final = await (
    await page.request.get("/api/v1/shopping-lists/" + listId)
  ).json();
  const overridden = final.items.find(
    (x: { hasOverride: boolean }) => x.hasOverride,
  );
  expect(overridden.baselineExact).toEqual({
    numerator: "550",
    denominator: "1",
  });
  expect(overridden.effectiveExact).toEqual({
    numerator: "600",
    denominator: "1",
  });
  expect(overridden.purchased).toBe(true);
  expect(final.items.find((x: { manual: boolean }) => x.manual).label).toBe(
    "Bread",
  );
  expect(final.summary.unresolvedCount).toBe(1);
});
