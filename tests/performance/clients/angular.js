import assert from "node:assert/strict";
import { realBrowserFactory } from "./real-browser.js";
import { createRequire } from "node:module";
import { X509Certificate, createHash } from "node:crypto";
import tls from "node:tls";
const { chromium, expect } = createRequire(
    new URL("../../e2e/package.json", import.meta.url),
)("@playwright/test");

export async function launchBrowser(fixture, { headed = false } = {}) {
    // Verify ordinary TLS first, then pin this one test leaf in the private Chromium profile.
    const pin = await new Promise((resolve, reject) => {
        const socket = tls.connect(
            {
                host: "localhost",
                port: Number(new URL(fixture.origin).port || 443),
                servername: "localhost",
                ca: fixture.ca,
                family: 4,
            },
            () => {
                const certificate = new X509Certificate(
                    socket.getPeerCertificate().raw,
                );
                resolve(
                    createHash("sha256")
                        .update(
                            certificate.publicKey.export({
                                type: "spki",
                                format: "der",
                            }),
                        )
                        .digest("base64"),
                );
                socket.end();
            },
        );
        socket.setTimeout(15000, () =>
            socket.destroy(new Error("Reference TLS handshake timeout")),
        );
        socket.on("error", reject);
    });
    if (headed) return realBrowserFactory(chromium, pin);
    return chromium.launch({
        headless: !headed,
        ignoreDefaultArgs: ["--disable-backgrounding-occluded-windows"],
        args: ["--ignore-certificate-errors-spki-list=" + pin],
    });
}
export class AngularActor {
    constructor(browser, fixture, state, credentials, measurement) {
        Object.assign(this, {
            browser,
            fixture,
            state,
            credentials,
            measurement,
        });
    }
    async start() {
        this.context = await this.browser.newContext({
            baseURL: this.fixture.origin,
            viewport: { width: 1280, height: 900 },
        });
        this.page = await this.context.newPage();
        this.page.setDefaultTimeout(15000);
        this.page.on("websocket", (socket) => {
            if (!socket.url().includes("/hubs/shopping")) return;
            this.ready = false;
            this.socket = socket;
            socket.on("framereceived", () => {
                if (this.socket === socket) this.ready = true;
            });
            socket.on("close", () => {
                if (this.socket === socket) this.ready = false;
            });
        });
        await this.page.exposeBinding(
            "__rmPerformanceState",
            (_source, event) => {
                if (this.pendingActive && event === this.pendingActive.event) {
                    const pending = this.pendingActive;
                    this.pendingActive = undefined;
                    pending.resolve(this.measurement.checkpoint());
                }
            },
        );
        await this.page.addInitScript(() => {
            document.addEventListener("visibilitychange", () => {
                if (!document.hidden) window.__rmPerformanceState("foreground");
            });
            window.addEventListener("online", () =>
                window.__rmPerformanceState("reconnect"),
            );
        });
        await this.page.goto(this.fixture.origin + "/recipes");
        await this.page
            .getByLabel("Email", { exact: true })
            .waitFor({ state: "visible" });
        {
            await this.page
                .getByLabel("Email", { exact: true })
                .fill(this.credentials.email);
            await this.page
                .getByLabel("Password", { exact: true })
                .fill(this.credentials.password);
            await this.page
                .getByRole("button", { name: "Sign in", exact: true })
                .click();
        }
        await expect(
            this.page
                .getByRole("link", { name: "Shopping Lists", exact: true })
                .first(),
        ).toBeVisible();
    }
    async prepare(entity) {
        if (this.entity === entity) return;
        this.ready = false;
        if (entity === "recipe") {
            await this.page.goto(
                this.fixture.origin + "/recipes/" + this.state.recipeId,
            );
            await expect(
                this.page.getByRole("link", {
                    name: "Edit recipe",
                    exact: true,
                }),
            ).toBeVisible();
        } else if (entity === "shopping") {
            await this.page.goto(this.fixture.origin + "/shopping-lists");
            await this.page
                .getByRole("button", { name: /^MP shared shopping / })
                .click();
            await expect(
                this.page.locator(
                    `[data-shopping-item="${this.state.itemId}"]`,
                ),
            ).toBeVisible();
        } else {
            await this.page.goto(this.fixture.origin + "/meal-planner");
            await this.page
                .getByLabel("Week containing", { exact: true })
                .fill(this.state.monday);
            await this.page
                .getByLabel("Week containing", { exact: true })
                .press("Tab");
            await expect(
                this.page.locator(`[data-meal-id="${this.state.mealId}"]`),
            ).toBeVisible();
        }
        const deadline = performance.now() + 15000;
        while (!this.ready && performance.now() < deadline)
            await new Promise((resolve) => setTimeout(resolve, 10));
        assert.ok(
            this.ready,
            "SignalR must be established before a warmed sample",
        );
        this.entity = entity;
    }
    async write(entity, marker) {
        let resource, save;
        if (entity === "recipe") {
            await this.page
                .getByRole("link", { name: "Edit recipe", exact: true })
                .click();
            await this.page.getByLabel("Title", { exact: true }).fill(marker);
            resource = "/api/v1/recipes/" + this.state.recipeId;
            save = this.page.getByRole("button", {
                name: "Save recipe",
                exact: true,
            });
        } else if (entity === "shopping") {
            await this.page
                .locator(`[data-shopping-item="${this.state.itemId}"]`)
                .getByRole("button", { name: /^Edit / })
                .click();
            await this.page
                .getByLabel("Item name", { exact: true })
                .fill(marker);
            resource = `/api/v1/shopping-lists/${this.state.listId}/items/${this.state.itemId}`;
            save = this.page.getByRole("button", {
                name: "Save item changes",
                exact: true,
            });
        } else {
            await this.page
                .locator(`[data-meal-id="${this.state.mealId}"]`)
                .getByRole("button", { name: /^Edit / })
                .click();
            await this.page
                .getByLabel("Meal description", { exact: true })
                .fill(marker);
            resource = "/api/v1/meals/" + this.state.mealId + "/other";
            save = this.page.getByRole("button", {
                name: "Save meal",
                exact: true,
            });
        }
        const accepted = this.page
            .waitForResponse(
                (response) =>
                    response.request().method() === "PUT" &&
                    new URL(response.url()).pathname === resource,
            )
            .then(async (response) => {
                assert.equal(
                    response.status(),
                    200,
                    "The timed UI write must succeed",
                );
                // The successful response headers acknowledge the committed write. Waiting for
                // response.finished() can hang when Angular navigates away after saving.
                const revision = response.headers()["etag"];
                assert.equal(typeof revision, "string");
                return {
                    revision,
                    acknowledged: this.measurement.checkpoint(),
                };
            })
            .catch((error) => ({ error }));
        await save.click();
        const result = await accepted;
        if (result.error) throw result.error;
        if (entity === "recipe")
            await expect(
                this.page.getByRole("link", {
                    name: "Edit recipe",
                    exact: true,
                }),
            ).toBeVisible();
        else if (entity === "shopping")
            await expect(
                this.page.getByRole("heading", {
                    name: "Edit shopping item",
                    exact: true,
                }),
            ).toBeHidden();
        else
            await expect(
                this.page.getByLabel("Meal description", { exact: true }),
            ).toBeHidden();
        return result;
    }
    async observe(entity, marker) {
        if (entity === "recipe")
            await expect(
                this.page.getByRole("heading", { name: marker, exact: true }),
            ).toBeVisible({ timeout: 30000 });
        else if (entity === "shopping")
            await expect(
                this.page.locator(
                    `[data-shopping-item="${this.state.itemId}"]`,
                ),
            ).toContainText(marker, { timeout: 30000 });
        else
            await expect(
                this.page.locator(`[data-meal-id="${this.state.mealId}"]`),
            ).toContainText(marker, { timeout: 30000 });
        return this.measurement.checkpoint();
    }
    async unavailable(mode) {
        if (mode === "reconnect") {
            await this.context.setOffline(true);
            await this.page.waitForFunction(() => !navigator.onLine);
        } else {
            this.windowSession = await this.context.newCDPSession(this.page);
            // Playwright normally emulates a permanently focused page. Disable
            // that override so native window visibility drives real DOM events.
            await this.windowSession.send(
                "Emulation.setFocusEmulationEnabled",
                { enabled: false },
            );
            const { targetInfo } = await this.windowSession.send(
                "Target.getTargetInfo",
            );
            const { windowId } = await this.windowSession.send(
                "Browser.getWindowForTarget",
                { targetId: targetInfo.targetId },
            );
            this.windowId = windowId;
            await this.windowSession.send("Browser.setWindowBounds", {
                windowId,
                bounds: { windowState: "minimized" },
            });
            await this.page.waitForFunction(() => document.hidden);
        }
    }
    async assertUnavailable(entity, marker, mode) {
        if (mode !== "reconnect") {
            assert.equal(await this.page.evaluate(() => document.hidden), true);
            return;
        }
        const content =
            entity === "recipe"
                ? this.page.locator("h1").last()
                : entity === "shopping"
                  ? this.page.locator(
                        `[data-shopping-item="${this.state.itemId}"]`,
                    )
                  : this.page.locator(`[data-meal-id="${this.state.mealId}"]`);
        assert.ok(
            !(await content.textContent()).includes(marker),
            "An offline observer must not receive the accepted mutation",
        );
    }
    async activate(mode) {
        const active = new Promise((resolve, reject) => {
            const timeout = setTimeout(() => {
                this.pendingActive = undefined;
                reject(new Error("Missing genuine browser reactivation event"));
            }, 15000);
            this.pendingActive = {
                event: mode,
                resolve: (token) => {
                    clearTimeout(timeout);
                    resolve(token);
                },
            };
        });
        if (mode === "reconnect") await this.context.setOffline(false);
        else {
            await this.windowSession.send("Browser.setWindowBounds", {
                windowId: this.windowId,
                bounds: { windowState: "normal" },
            });
            await this.page.bringToFront();
            await this.windowSession.detach();
            this.windowSession = undefined;
        }
        return active;
    }
    async networkProbe() {
        const status = await this.page.evaluate(async () => {
            const response = await fetch("/health/ready", {
                cache: "no-store",
            });
            await response.arrayBuffer();
            return response.status;
        });
        assert.equal(status, 200);
    }
    async close() {
        await this.context.close();
    }
}
