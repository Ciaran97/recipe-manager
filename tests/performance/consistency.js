#!/usr/bin/env node
// Browser correctness probes accompanying MP-SYNC-01; no timing or native-coverage claim.
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import crypto from "node:crypto";
import { parseArgs } from "node:util";
import { createRequire } from "node:module";
import { loadFixture, seedFixture, Session } from "./fixture.js";
import { AngularActor, launchBrowser } from "./clients/angular.js";
import { Measurement } from "./measurement.js";
const { expect } = createRequire(
    new URL("../e2e/package.json", import.meta.url),
)("@playwright/test");
const { values } = parseArgs({
    options: { fixture: { type: "string" }, output: { type: "string" } },
});
assert.ok(values.fixture && values.output);
fs.mkdirSync(values.output, { mode: 0o700 });
const checks = {},
    failures = [];
let browser,
    actors = [];
try {
    const fixture = await loadFixture(values.fixture),
        state = await seedFixture(fixture);
    const memberSession = new Session(fixture.origin, fixture.ca);
    await memberSession.write("POST", "/api/v1/auth/login", state.member, 200);
    browser = await launchBrowser(fixture);
    const measurement = new Measurement();
    const primary = new AngularActor(
        browser,
        fixture,
        state,
        fixture.admin,
        measurement,
    );
    const sameUser = new AngularActor(
        browser,
        fixture,
        state,
        fixture.admin,
        measurement,
    );
    const member = new AngularActor(
        browser,
        fixture,
        state,
        state.member,
        measurement,
    );
    actors = [primary, sameUser, member];
    for (const actor of actors) {
        await actor.start();
        await actor.prepare("recipe");
    }
    const beforeOwn = await fixture.session.read("/api/v1/household/sync");
    const beforeOther = await memberSession.read("/api/v1/household/sync");
    assert.equal(
        (
            await fixture.session.request(
                "GET",
                "/api/v1/household/sync",
                undefined,
                undefined,
                beforeOwn.etag,
            )
        ).status,
        304,
    );
    assert.equal(
        (
            await memberSession.request(
                "GET",
                "/api/v1/household/sync",
                undefined,
                undefined,
                beforeOther.etag,
            )
        ).status,
        304,
    );
    const button = primary.page.getByRole("button", {
        name: /^(Add to favourites|Remove from favourites)$/,
    });
    const oldName = await button.innerText();
    const nextName =
        oldName === "Add to favourites"
            ? "Remove from favourites"
            : "Add to favourites";
    const accepted = primary.page.waitForResponse(
        (r) =>
            r.request().method() === "PUT" &&
            new URL(r.url()).pathname.endsWith("/favourite"),
    );
    await button.click();
    assert.equal((await accepted).status(), 200);
    await expect(
        sameUser.page.getByRole("button", { name: nextName, exact: true }),
    ).toBeVisible({ timeout: 30000 });
    const afterOwn = await fixture.session.request(
        "GET",
        "/api/v1/household/sync",
        undefined,
        undefined,
        beforeOwn.etag,
    );
    assert.equal(afterOwn.status, 200);
    assert.notEqual(afterOwn.etag, beforeOwn.etag);
    assert.equal(afterOwn.json.contentRevision, beforeOwn.json.contentRevision);
    assert.equal(
        (
            await fixture.session.request(
                "GET",
                "/api/v1/household/sync",
                undefined,
                undefined,
                afterOwn.etag,
            )
        ).status,
        304,
    );
    assert.equal(
        (
            await memberSession.request(
                "GET",
                "/api/v1/household/sync",
                undefined,
                undefined,
                beforeOther.etag,
            )
        ).status,
        304,
    );
    checks.personalCacheIsolation = true;
    checks.sameUserCrossDeviceFavourite = true;
    // A remote edit must not replace another client's unsaved form or bypass If-Match.
    await primary.page
        .getByRole("link", { name: "Edit recipe", exact: true })
        .click();
    const draft = "MP unsaved " + crypto.randomUUID();
    await primary.page.getByLabel("Title", { exact: true }).fill(draft);
    const remote = "MP accepted " + crypto.randomUUID();
    await member.write("recipe", remote);
    await expect(
        sameUser.page.getByRole("heading", { name: remote, exact: true }),
    ).toBeVisible({ timeout: 30000 });
    await expect(primary.page.getByLabel("Title", { exact: true })).toHaveValue(
        draft,
    );
    const stale = primary.page.waitForResponse(
        (r) =>
            r.request().method() === "PUT" &&
            new URL(r.url()).pathname === "/api/v1/recipes/" + state.recipeId,
    );
    await primary.page
        .getByRole("button", { name: "Save recipe", exact: true })
        .click();
    assert.equal((await stale).status(), 412);
    await expect(primary.page.getByRole("alert")).toBeVisible();
    await expect(primary.page.getByLabel("Title", { exact: true })).toHaveValue(
        draft,
    );
    assert.equal(
        (await fixture.session.read("/api/v1/recipes/" + state.recipeId)).json
            .title,
        remote,
    );
    checks.dirtyFormPreserved = true;
    checks.staleWriteRejected = true;
    primary.page.once("dialog", dialog => dialog.accept());
    await primary.page
        .getByRole("link", { name: "Cancel", exact: true })
        .first()
        .click();
    await expect(
        primary.page.getByRole("heading", { name: remote, exact: true }),
    ).toBeVisible();
    checks.acceptedStateConvergence = true;
} catch (error) {
    failures.push({ type: error.name });
    fs.writeFileSync(
        path.join(values.output, "private-failure.txt"),
        error.stack ?? String(error),
        { mode: 0o600 },
    );
} finally {
    for (const actor of actors) await actor.close().catch(() => {});
    if (browser) await browser.close();
    const pass =
        failures.length === 0 &&
        Object.keys(checks).length === 5 &&
        Object.values(checks).every(Boolean);
    fs.writeFileSync(
        path.join(values.output, "report.json"),
        JSON.stringify(
            {
                procedure: "MP-SYNC-01 correctness",
                client: "Angular",
                checks,
                failures,
                pass,
                fullAcceptancePass: false,
            },
            null,
            2,
        ),
        { mode: 0o600 },
    );
    if (!pass) process.exitCode = 1;
    console.log(
        "Browser cache/conflict correctness: " +
            pass +
            "; native coverage remains separate.",
    );
}
