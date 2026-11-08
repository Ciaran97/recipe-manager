import { test, expect } from "@playwright/test";
import https from "node:https";
import fs from "node:fs";
import { timingSafeEqual } from "node:crypto";
import { write } from "./fixtures";

type Input = {
  command: string;
  origin?: "native" | "web";
  recipeId?: string;
  from?: string;
  to?: string;
};
type Journey = {
  origin: string;
  title: string;
  from: string;
  to: string;
  recipeId: string;
  wednesdayId: string;
  listId?: string;
  itemId?: string;
  phase: string;
};

// A fixture-only, loopback TLS controller. Every mutation below uses the real Angular UI.
// The native driver performs the complementary SwiftUI/share-extension operations.
test("continuous critical journeys cross both native and Angular clients", async ({
  page,
}) => {
  test.setTimeout(900_000);
  const runtime = process.env.RECIPE_MANAGER_CROSS_RUNTIME;
  const token = process.env.RECIPE_MANAGER_CROSS_TOKEN;
  const ready = process.env.RECIPE_MANAGER_CROSS_READY;
  if (!runtime || !token || !ready)
    throw new Error(
      "Run scripts/test-ios-households.sh CriticalJourneyUITests.",
    );
  let finish!: () => void;
  let fail!: (error: Error) => void;
  const completed = new Promise<void>((resolve, reject) => {
    finish = resolve;
    fail = reject;
  });
  let busy = false;
  let state: Journey | undefined;
  const finished = new Set<string>();
  page.on("dialog", (dialog) => dialog.accept());
  async function execute(input: Input): Promise<Record<string, unknown>> {
    if (input.command === "begin") {
      if (
        state ||
        !["native", "web"].includes(input.origin ?? "") ||
        finished.has(input.origin!)
      )
        throw new Error("Invalid journey transition.");
      for (const date of [input.from, input.to])
        if (!/^\d{4}-\d{2}-\d{2}$/.test(date ?? ""))
          throw new Error("Invalid date.");
      expect(
        (
          await write(page, "POST", "/api/v1/auth/login", {
            email: "admin@example.test",
            password: "e2e-admin-test-passphrase",
          })
        ).status(),
      ).toBe(200);
      const title =
        input.origin === "native"
          ? "Native critical rice"
          : "Web critical rice";
      let recipeId = input.recipeId;
      if (input.origin === "web") {
        await page.goto("/recipes/import");
        await page.getByLabel("Import method").selectOption("text");
        await page
          .getByLabel("Recipe text", { exact: true })
          .fill(fs.readFileSync(runtime + "/ai-recipe-text", "utf8"));
        await page
          .getByRole("button", { name: "Import for review", exact: true })
          .click();
        await expect(
          page.getByRole("heading", {
            name: "Review imported recipe",
            exact: true,
          }),
        ).toBeVisible();
        await page.getByLabel("Title", { exact: true }).fill(title);
        await page.getByLabel("Cup and spoon standard").selectOption("AU");
        await page.getByLabel("Quantity 1", { exact: true }).fill("150");
        await page.getByLabel("Unit 1", { exact: true }).selectOption("g");
        await page.getByLabel("Unit 2", { exact: true }).selectOption("item");
        await page
          .getByRole("button", {
            name: "Choose collections and tags",
            exact: true,
          })
          .click();
        for (const name of ["Weeknight", "Comfort"])
          await page.getByRole("checkbox", { name, exact: true }).check();
        await page.getByLabel(/I have reviewed/).check();
        await page
          .getByRole("button", { name: "Save recipe", exact: true })
          .click();
        await expect(
          page.getByRole("heading", { name: title, exact: true }),
        ).toBeVisible();
        recipeId = page.url().split("/").at(-1);
      } else {
        if (!/^[0-9a-f-]{36}$/i.test(recipeId ?? ""))
          throw new Error("Invalid recipe identifier.");
        await page.goto("/recipes/" + recipeId);
        await expect(
          page.getByRole("heading", { name: title, exact: true }),
        ).toBeVisible();
      }
      const recipe = await (
        await page.request.get("/api/v1/recipes/" + recipeId)
      ).json();
      expect(recipe.collectionIds).toHaveLength(1);
      expect(recipe.tagIds).toHaveLength(1);
      expect(recipe.ingredients[0].quantityText).toBe("150");
      await page.goto("/recipes/new");
      await page
        .getByLabel("Title", { exact: true })
        .fill(title + " Wednesday");
      await page.getByLabel("Default servings", { exact: true }).fill("2");
      await page
        .getByRole("combobox", { name: "Ingredient name 1", exact: true })
        .fill("Rice");
      await page.getByRole("option", { name: /^Rice/ }).click();
      await page.getByLabel("Quantity 1", { exact: true }).fill("0.2");
      await page.getByLabel("Unit 1", { exact: true }).selectOption("kg");
      await page.getByLabel("Step 1", { exact: true }).fill("Cook the rice.");
      await page
        .getByRole("button", { name: "Save recipe", exact: true })
        .click();
      await expect(
        page.getByRole("heading", { name: title + " Wednesday", exact: true }),
      ).toBeVisible();
      state = {
        origin: input.origin!,
        title,
        from: input.from!,
        to: input.to!,
        recipeId: recipeId!,
        wednesdayId: page.url().split("/").at(-1)!,
        phase: "planned-next",
      };
      return { recipeId: state.recipeId, wednesdayId: state.wednesdayId };
    }
    if (!state) throw new Error("No active journey.");
    if (input.command === "shopping" && state.phase === "planned-next") {
      const calendar = await (
        await page.request.get(
          `/api/v1/meals?from=${state.from}&to=${state.to}`,
        )
      ).json();
      expect(calendar.items).toHaveLength(3);
      expect(
        calendar.items.filter((x: { type: string }) => x.type === "leftovers"),
      ).toHaveLength(1);
      for (const meal of calendar.items.filter(
        (x: { type: string }) => x.type === "cook",
      ))
        expect(meal.cookingEvent.actualPrepared).toBeNull();
      await page.goto("/shopping-lists");
      await page
        .getByRole("button", { name: "New shopping list", exact: true })
        .click();
      await page
        .getByLabel("List name", { exact: true })
        .fill(state.title + " shopping");
      await page.getByLabel("From date", { exact: true }).fill(state.from);
      await page.getByLabel("Through date", { exact: true }).fill(state.to);
      await page
        .getByRole("button", { name: "Create shopping list", exact: true })
        .click();
      await expect(
        page.locator("[data-shopping-item]").filter({ hasText: "500 g" }),
      ).toBeVisible();
      await expect(page.locator("[data-shopping-item]")).toHaveCount(2);
      await expect(
        page.locator("[data-shopping-item]").filter({ hasText: "to taste" }),
      ).toContainText("Amount unresolved");
      const index = await (
        await page.request.get("/api/v1/shopping-lists")
      ).json();
      state.listId = index.items.find(
        (x: { name: string }) => x.name === state!.title + " shopping",
      ).id;
      const detail = await (
        await page.request.get("/api/v1/shopping-lists/" + state.listId)
      ).json();
      state.itemId = detail.items.find(
        (x: { unresolved: boolean }) => !x.unresolved,
      ).id;
      state.phase = "native-edits-next";
      return { listId: state.listId, itemId: state.itemId };
    }
    if (
      input.command === "observe-and-edit" &&
      state.phase === "native-edits-next"
    ) {
      const rice = page.locator(`[data-shopping-item="${state.itemId}"]`);
      await expect(rice).toContainText("600 g");
      await expect(rice.getByRole("checkbox")).toBeChecked();
      await expect(
        page.locator("[data-shopping-item]").filter({ hasText: "Bread" }),
      ).toContainText("Manual item");
      await page.goto("/recipes/" + state.recipeId + "/edit");
      if (state.origin === "web")
        await expect(page.getByLabel("Notes", { exact: true })).toHaveValue(
          "Reviewed on iOS",
        );
      await page.getByLabel("Quantity 1", { exact: true }).fill("175");
      await page.getByLabel(/I have reviewed/).check();
      await page
        .getByRole("button", { name: "Save recipe", exact: true })
        .click();
      await expect(
        page.getByRole("heading", { name: state.title, exact: true }),
      ).toBeVisible();
      state.phase = "native-refresh-next";
      return {};
    }
    if (
      input.command === "reconcile" &&
      state.phase === "native-refresh-next"
    ) {
      await page.goto("/shopping-lists");
      await page
        .getByRole("button", {
          name: new RegExp("^" + state.title + " shopping "),
        })
        .click();
      await expect(
        page.locator(`[data-shopping-item="${state.itemId}"]`),
      ).toContainText("600 g");
      await page
        .getByRole("button", {
          name: "Review updated requirements",
          exact: true,
        })
        .click();
      await page
        .getByRole("button", { name: "Preview requirements", exact: true })
        .click();
      await expect(
        page.locator("[data-review-key]").filter({ hasText: "550 g" }),
      ).toContainText("600 g");
      const reviewed = page.getByRole("checkbox", {
        name: "I reviewed the latest requirements and choices",
        exact: true,
      });
      if (await reviewed.count()) await reviewed.check();
      await page
        .getByRole("button", {
          name: "Apply reviewed requirements",
          exact: true,
        })
        .click();
      await expect(
        page
          .getByRole("status")
          .filter({ hasText: "Reviewed requirements applied." }),
      ).toBeVisible();
      state.phase = "native-observer-next";
      return {};
    }
    if (input.command === "done" && state.phase === "native-observer-next") {
      finished.add(state.origin);
      state = undefined;
      return { complete: finished.size === 2 };
    }
    throw new Error("Invalid journey command or phase.");
  }
  const server = https.createServer(
    {
      cert: fs.readFileSync(runtime + "/localhost.pem"),
      key: fs.readFileSync(runtime + "/localhost.key"),
    },
    async (request, response) => {
      const actual = Buffer.from(
          String(request.headers["x-test-control-token"] ?? ""),
        ),
        expected = Buffer.from(token);
      if (
        actual.length !== expected.length ||
        !timingSafeEqual(actual, expected)
      ) {
        response.writeHead(403).end();
        return;
      }
      if (request.method === "GET" && request.url === "/ready") {
        response.writeHead(200).end();
        return;
      }
      if (request.method !== "POST" || request.url !== "/step" || busy) {
        response.writeHead(409).end();
        return;
      }
      busy = true;
      try {
        const chunks: Buffer[] = [];
        let length = 0;
        for await (const chunk of request) {
          length += chunk.length;
          if (length > 4096) throw new Error("Controller request too large.");
          chunks.push(Buffer.from(chunk));
        }
        const result = await execute(
          JSON.parse(Buffer.concat(chunks).toString("utf8")),
        );
        response
          .writeHead(200, { "Content-Type": "application/json" })
          .end(JSON.stringify(result));
        if (result.complete === true) finish();
      } catch (error) {
        response.writeHead(500).end();
        fail(
          error instanceof Error
            ? error
            : new Error("Critical controller failed."),
        );
      } finally {
        busy = false;
      }
    },
  );
  try {
    await new Promise<void>((resolve, reject) => {
      server.once("error", reject);
      server.listen(7291, "127.0.0.1", resolve);
    });
    fs.writeFileSync(ready, "ready", { mode: 0o600 });
    await completed;
  } finally {
    await new Promise<void>((resolve) => server.close(() => resolve()));
  }
});
