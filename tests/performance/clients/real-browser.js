// Owned Chromium processes attached without Playwright's permanent-focus override.
// Each actor gets its own private default context, profile and DevTools endpoint.
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawn } from "node:child_process";
import { once } from "node:events";

export function realBrowserFactory(chromium, pin) {
    const owned = new Set();
    return {
        async newContext(options) {
            const profile = fs.mkdtempSync(
                path.join(os.tmpdir(), "recipe-manager-perf-chromium-"),
            );
            fs.chmodSync(profile, 0o700);
            const child = spawn(
                chromium.executablePath(),
                [
                    "--user-data-dir=" + profile,
                    "--remote-debugging-port=0",
                    "--remote-debugging-address=127.0.0.1",
                    "--no-first-run",
                    "--no-default-browser-check",
                    "--disable-sync",
                    "--ignore-certificate-errors-spki-list=" + pin,
                    "about:blank",
                ],
                { stdio: "ignore" },
            );
            const exited = once(child, "exit");
            let closed = false,
                browser;
            const close = async () => {
                if (closed) return;
                closed = true;
                if (browser) await browser.close().catch(() => {});
                if (child.exitCode === null && child.signalCode === null)
                    child.kill("SIGTERM");
                await Promise.race([
                    exited,
                    new Promise((resolve) => setTimeout(resolve, 5000)),
                ]);
                if (child.exitCode === null && child.signalCode === null) {
                    child.kill("SIGKILL");
                    await exited;
                }
                fs.rmSync(profile, { recursive: true, force: true });
                owned.delete(close);
            };
            owned.add(close);
            try {
                const activePort = path.join(profile, "DevToolsActivePort"),
                    deadline = performance.now() + 15000;
                while (!fs.existsSync(activePort)) {
                    if (
                        child.exitCode !== null ||
                        child.signalCode !== null ||
                        performance.now() > deadline
                    )
                        throw new Error("Owned Chromium endpoint unavailable");
                    await new Promise((resolve) => setTimeout(resolve, 25));
                }
                const port = Number(
                    fs.readFileSync(activePort, "utf8").split("\n")[0],
                );
                if (!Number.isInteger(port) || port < 1 || port > 65535)
                    throw new Error("Invalid owned Chromium endpoint");
                browser = await chromium.connectOverCDP(
                    "http://127.0.0.1:" + port,
                    { noDefaults: true },
                );
                const context = browser.contexts()[0];
                // Default contexts attached with noDefaults retain genuine window/tab visibility.
                // Relative navigation is resolved explicitly by AngularActor, not hidden context settings.
                return {
                    newPage: async () => {
                        const page = await context.newPage();
                        await page.setViewportSize(options.viewport);
                        return page;
                    },
                    newCDPSession: (page) => context.newCDPSession(page),
                    setOffline: (value) => context.setOffline(value),
                    close,
                };
            } catch (error) {
                await close();
                throw error;
            }
        },
        async close() {
            for (const close of [...owned]) await close();
        },
    };
}
