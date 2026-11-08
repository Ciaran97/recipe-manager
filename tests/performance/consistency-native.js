// Real native UI plus two Angular sessions; backend remains authoritative for all assertions.
import assert from "node:assert/strict";
import crypto from "node:crypto";
import { createRequire } from "node:module";
import { AngularActor } from "./clients/angular.js";
import { Session } from "./fixture.js";
export async function verifyNativeConsistency(
    subject,
    fixture,
    state,
    browser,
    measurement,
) {
    const sameCredentials = subject.credentials;
    const otherCredentials =
        sameCredentials.email === fixture.admin.email
            ? state.member
            : fixture.admin;
    const same = new AngularActor(
        browser,
        fixture,
        state,
        sameCredentials,
        measurement,
    );
    const other = new AngularActor(
        browser,
        fixture,
        state,
        otherCredentials,
        measurement,
    );
    const ownSession = new Session(fixture.origin, fixture.ca),
        otherSession = new Session(fixture.origin, fixture.ca);
    const checks = {};
    try {
        await ownSession.write(
            "POST",
            "/api/v1/auth/login",
            sameCredentials,
            200,
        );
        await otherSession.write(
            "POST",
            "/api/v1/auth/login",
            otherCredentials,
            200,
        );
        await subject.prepare("recipe");
        await same.start();
        await same.prepare("recipe");
        await other.start();
        await other.prepare("recipe");
        const beforeOwn = await ownSession.read("/api/v1/household/sync"),
            beforeOther = await otherSession.read("/api/v1/household/sync");
        const recipe = (
            await ownSession.read("/api/v1/recipes/" + state.recipeId)
        ).json;
        const response = subject.proxy
            .acknowledged("/api/v1/recipes/" + state.recipeId + "/favourite")
            .then(
                (value) => ({ value }),
                (error) => ({ error }),
            );
        await subject.agent.command("toggleFavourite");
        const accepted = await response;
        if (accepted.error) throw accepted.error;
        const expected = recipe.favourite
            ? "Add to favourites"
            : "Remove from favourites";
        const { expect } = createRequire(
            new URL("../e2e/package.json", import.meta.url),
        )("@playwright/test");
        await expect(
            same.page.getByRole("button", { name: expected, exact: true }),
        ).toBeVisible({ timeout: 30000 });
        const changed = await ownSession.request(
            "GET",
            "/api/v1/household/sync",
            undefined,
            undefined,
            beforeOwn.etag,
        );
        assert.equal(changed.status, 200);
        assert.notEqual(changed.etag, beforeOwn.etag);
        assert.equal(
            changed.json.contentRevision,
            beforeOwn.json.contentRevision,
        );
        assert.equal(
            (
                await ownSession.request(
                    "GET",
                    "/api/v1/household/sync",
                    undefined,
                    undefined,
                    changed.etag,
                )
            ).status,
            304,
        );
        assert.equal(
            (
                await otherSession.request(
                    "GET",
                    "/api/v1/household/sync",
                    undefined,
                    undefined,
                    beforeOther.etag,
                )
            ).status,
            304,
        );
        checks.nativePersonalCacheIsolation = true;
        checks.nativeFavouriteCrossClient = true;
        const draft = "MP native unsaved " + crypto.randomUUID(),
            remote = "MP native accepted " + crypto.randomUUID();
        await subject.agent.command("beginDraft", { marker: draft });
        await other.write("recipe", remote);
        await subject.agent.command("assertDraft", { marker: draft });
        const rejected = subject.proxy
            .outcome("/api/v1/recipes/" + state.recipeId, 412)
            .then(
                (value) => ({ value }),
                (error) => ({ error }),
            );
        await subject.agent.command("saveStale", { marker: draft });
        const stale = await rejected;
        if (stale.error) throw stale.error;
        assert.equal(stale.value.status, 412);
        assert.equal(
            (await ownSession.read("/api/v1/recipes/" + state.recipeId)).json
                .title,
            remote,
        );
        checks.nativeDirtyFormPreserved = true;
        checks.nativeStaleWriteRejected = true;
        await subject.agent.command("cancelDraft");
        await subject.observe("recipe", remote);
        checks.nativeAcceptedStateConvergence = true;
        return checks;
    } finally {
        await same.close();
        await other.close();
    }
}
