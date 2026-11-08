#!/usr/bin/env node
// SC-004 real Angular UI probe. A client result never substitutes for missing iOS coverage.
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { performance } from "node:perf_hooks";
import { parseArgs } from "node:util";
import { createRequire } from "node:module";
import { loadFixture, seedFixture } from "./fixture.js";
import { AngularActor, launchBrowser } from "./clients/angular.js";
import { Measurement, percentile95 } from "./measurement.js";
const { expect } = createRequire(
    new URL("../e2e/package.json", import.meta.url),
)("@playwright/test");
const { values } = parseArgs({
    options: {
        fixture: { type: "string" },
        output: { type: "string" },
        samples: { type: "string", default: "100" },
        warmup: { type: "string", default: "9" },
        smoke: { type: "boolean", default: false },
    },
});
assert.ok(values.fixture && values.output);
const samples = Number(values.samples),
    warmup = Number(values.warmup);
assert.ok(
    Number.isInteger(samples) &&
        samples > 0 &&
        Number.isInteger(warmup) &&
        warmup >= 3,
);
if (!values.smoke) assert.ok(samples >= 100);
fs.mkdirSync(values.output, { mode: 0o700 });
const durations = { search: [], calendar: [] },
    failures = [];
let browser, actor;
try {
    const fixture = await loadFixture(values.fixture),
        state = await seedFixture(fixture);
    browser = await launchBrowser(fixture);
    actor = new AngularActor(
        browser,
        fixture,
        state,
        fixture.admin,
        new Measurement(),
    );
    await actor.start();
    const page = actor.page;
    for (let index = -warmup; index < samples; index++) {
        await page
            .getByRole("link", { name: "Recipes", exact: true })
            .first()
            .click();
        const title =
            "MP synthetic recipe " + ((index + warmup) % 2 ? "0498" : "0499");
        await page
            .getByLabel("Search recipes and ingredients", { exact: true })
            .fill(title);
        const response = page.waitForResponse(
            (r) =>
                r.request().method() === "GET" &&
                new URL(r.url()).pathname === "/api/v1/recipes" &&
                new URL(r.url()).searchParams.get("q") === title,
        );
        let start = performance.now();
        await page
            .getByRole("button", { name: "Search recipes", exact: true })
            .click();
        assert.equal((await response).status(), 200);
        await expect(
            page.getByRole("link", { name: title, exact: true }),
        ).toBeVisible();
        if (index >= 0) durations.search.push(performance.now() - start);
        start = performance.now();
        await page
            .getByRole("link", { name: "Meal Planner", exact: true })
            .first()
            .click();
        await expect(
            page.getByLabel("Weekly meal calendar", { exact: true }),
        ).toBeVisible();
        await expect(
            page.locator(`[data-meal-id="${state.mealId}"]`),
        ).toBeVisible();
        await expect(
            page.getByText("Loading meal plan…", { exact: true }),
        ).toBeHidden();
        if (index >= 0) durations.calendar.push(performance.now() - start);
        if (index >= 0 && (index + 1) % 25 === 0)
            console.log(`${index + 1} warmed search/calendar pairs rendered`);
    }
} catch (error) {
    failures.push({ type: error.name });
    fs.writeFileSync(
        path.join(values.output, "private-failure.txt"),
        error.stack ?? String(error),
        { mode: 0o600 },
    );
} finally {
    if (actor) await actor.close().catch(() => {});
    if (browser) await browser.close();
    const workflows = Object.entries(durations).map(
        ([workflow, durationsMs]) => ({
            workflow,
            count: durationsMs.length,
            p95Ms: percentile95(durationsMs),
            durationsMs,
            pass:
                durationsMs.length >= 100 && percentile95(durationsMs) <= 2000,
        }),
    );
    const report = {
        criterion: "SC-004",
        client: "Angular",
        clock: "single controller monotonic clock",
        datasetRecipes: 500,
        users: 2,
        samples,
        warmup,
        smoke: values.smoke,
        workflows,
        failures,
        clientTimingPass:
            !values.smoke && !failures.length && workflows.every((w) => w.pass),
        fullAcceptancePass: false,
    };
    fs.writeFileSync(
        path.join(values.output, "report.json"),
        JSON.stringify(report, null, 2),
        { mode: 0o600 },
    );
    if (failures.length || (!values.smoke && !report.clientTimingPass))
        process.exitCode = 1;
    console.log(
        "Search/calendar client probe recorded; native and reference-network validation remain separate gates.",
    );
}
