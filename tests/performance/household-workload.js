#!/usr/bin/env node
// Real clients and the real backend only. Never resets an installation or publishes credentials.
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import crypto from "node:crypto";
import { parseArgs } from "node:util";
import { Measurement, pairings, modes } from "./measurement.js";
import { loadFixture, seedFixture } from "./fixture.js";
import { AngularActor, launchBrowser } from "./clients/angular.js";
import { NativeHarness, NativeActor } from "./clients/native.js";
import {
    referenceHost,
    networkEvidence,
    clientProbe,
    nativeSearchCalendar,
} from "./evidence.js";
import { verifyNativeConsistency } from "./consistency-native.js";

const { values } = parseArgs({
    options: {
        fixture: { type: "string" },
        output: { type: "string" },
        pairing: { type: "string", multiple: true },
        mode: { type: "string", multiple: true },
        samples: { type: "string", default: "100" },
        warmup: { type: "string", default: "9" },
        smoke: { type: "boolean", default: false },
        headed: { type: "boolean", default: false },
        native: { type: "boolean", default: false },
        "native-derived-data": { type: "string" },
        qualify: { type: "boolean", default: false },
        "reference-docker-host": { type: "string" },
    },
});
assert.ok(
    values.fixture && values.output,
    "Supply --fixture and a new private --output directory.",
);
const selected = values.pairing ?? pairings,
    selectedModes = values.mode ?? modes;
assert.ok(
    selected.every((pairing) => pairings.includes(pairing)) &&
        selectedModes.every((mode) => modes.includes(mode)),
);
const samples = Number(values.samples),
    warmup = Number(values.warmup);
assert.ok(
    Number.isInteger(samples) &&
        samples > 0 &&
        Number.isInteger(warmup) &&
        warmup >= 3,
);
if (!values.smoke)
    assert.ok(
        samples >= 100,
        "Acceptance requires at least 100 samples per cell.",
    );
assert.ok(
    values.native || selected.every((pairing) => pairing === "angular-angular"),
    "Supply --native explicitly to build disposable native performance agents.",
);
fs.mkdirSync(values.output, { mode: 0o700 });
const run = new Measurement();
const failures = [],
    completedCells = [];
const runId = crypto.randomBytes(8).toString("hex");
const qualification = { network: [] };
let browser,
    nativeHarness,
    actors = [],
    current = {};
try {
    const fixture = await loadFixture(values.fixture),
        state = await seedFixture(fixture);
    if (values.qualify) {
        qualification.hardware = referenceHost(
            fixture,
            values["reference-docker-host"],
        );
        qualification.browserConsistency = await clientProbe(
            "tests/performance/consistency.js",
            fixture,
            path.join(values.output, "browser-consistency"),
        );
        qualification.browserSearchCalendar = await clientProbe(
            "tests/performance/search-calendar.js",
            fixture,
            path.join(values.output, "browser-search-calendar"),
        );
    }
    if (
        values.qualify ||
        selected.some((pairing) => pairing.includes("angular"))
    )
        browser = await launchBrowser(fixture, { headed: values.headed });
    if (selected.some((pairing) => pairing.includes("ios"))) {
        nativeHarness = new NativeHarness(
            fixture,
            run,
            path.join(values.output, "native-private"),
            values["native-derived-data"],
        );
        await nativeHarness.initialize();
    }
    const actor = (kind, credentials) =>
        kind === "angular"
            ? new AngularActor(browser, fixture, state, credentials, run)
            : new NativeActor(nativeHarness, fixture, state, credentials, run);
    for (const pairing of selected) {
        const [writerKind, observerKind] = pairing.split("-");
        const writer = actor(writerKind, fixture.admin);
        const observer = actor(observerKind, state.member);
        actors = [writer, observer];
        await writer.start();
        await observer.start();
        if (values.qualify) {
            const native = [writer, observer].find(
                (actor) => actor instanceof NativeActor,
            );
            if (native && !qualification.nativeConsistency) {
                qualification.nativeConsistency = await verifyNativeConsistency(
                    native,
                    fixture,
                    state,
                    browser,
                    run,
                );
                qualification.nativeSearchCalendar = await nativeSearchCalendar(
                    native,
                    state,
                    { samples, warmup },
                );
            }
            qualification.network.push({
                pairing,
                actor: "writer",
                ...(await networkEvidence(writer)),
            });
            qualification.network.push({
                pairing,
                actor: "observer",
                ...(await networkEvidence(observer)),
            });
        }
        for (const mode of selectedModes) {
            for (let index = -warmup; index < samples; index++) {
                const entity = ["shopping", "recipe", "meal"][
                    (index + warmup) % 3
                ];
                const id = `${runId}-${pairing}-${mode}-${index}`;
                const marker = "MP " + id;
                current = { pairing, mode, entity, index };
                await writer.prepare(entity);
                await observer.prepare(entity);
                let rendered, active;
                if (mode === "connected")
                    rendered = observer.observe(entity, marker).then(
                        (token) => ({ token }),
                        (error) => ({ error }),
                    );
                else await observer.unavailable(mode);
                const accepted = await writer.write(entity, marker);
                if (index >= 0) {
                    run.begin({
                        id,
                        pairing,
                        mode,
                        entity,
                        revision: accepted.revision,
                    });
                    run.event(
                        id,
                        "acknowledged",
                        accepted.revision,
                        accepted.acknowledged,
                    );
                }
                if (mode !== "connected") {
                    await observer.assertUnavailable(entity, marker, mode);
                    active = await observer.activate(mode);
                    if (index >= 0)
                        run.event(id, "active", accepted.revision, active);
                    rendered = observer.observe(entity, marker).then(
                        (token) => ({ token }),
                        (error) => ({ error }),
                    );
                }
                const observed = await rendered;
                if (observed.error) throw observed.error;
                if (index >= 0)
                    run.event(
                        id,
                        "rendered",
                        accepted.revision,
                        observed.token,
                    );
                if (index >= 0 && (index + 1) % 25 === 0)
                    console.log(
                        `${pairing}/${mode}: ${index + 1} accepted rendered samples`,
                    );
            }
            completedCells.push({ pairing, mode });
        }
        await writer.close();
        await observer.close();
        actors = [];
    }
} catch (error) {
    failures.push({ ...current, type: error.name });
    fs.writeFileSync(
        path.join(values.output, "private-failure.txt"),
        error.stack ?? String(error),
        { mode: 0o600 },
    );
    console.error(
        "Workload failed; inspect the private failure artifact. No sample or cell was waived.",
    );
} finally {
    const cleanupFailure = (error) =>
        failures.push({ phase: "cleanup", type: error.name });
    for (const actor of actors) await actor.close().catch(cleanupFailure);
    if (browser) await browser.close().catch(cleanupFailure);
    if (nativeHarness) await nativeHarness.close().catch(cleanupFailure);
    const report = {
        procedure: "MP-SYNC-01",
        clock: "single controller monotonic clock; no device timestamps",
        datasetRecipes: 500,
        users: 2,
        runId,
        smoke: values.smoke,
        headed: values.headed,
        nativeInstrumentation: nativeHarness
            ? {
                  writeAcknowledgement:
                      "Controller TLS proxy observes successful opaque response ETag before forwarding; conservative relative to client receipt",
                  foreground:
                      "Controller activation-command dispatch starts conservative interval including native foregrounding",
                  reconnect:
                      "Only the owned per-agent API/SignalR proxy is disconnected; real client reconnection/backoff remains enabled",
                  rendering:
                      "Real SwiftUI visible unique marker acknowledged through private controller; no device timestamps",
                  configuration:
                      "Release with testability enabled; no shipping app instrumentation",
              }
            : undefined,
        samplesPerCell: samples,
        warmupPerCell: warmup,
        completedCells,
        qualification,
        failures,
        ...run.report(),
    };
    // Timing alone cannot certify the complete MP-SYNC-01 procedure. These gates
    // remain explicit until their live-client probes and host evidence are recorded.
    report.additionalGates = {
        hardwareAndNetwork:
            qualification.hardware?.qualified === true &&
            qualification.network.length === selected.length * 2 &&
            qualification.network.every((result) => result.pass),
        warmedSearchAndCalendar:
            qualification.browserSearchCalendar?.clientTimingPass === true &&
            qualification.nativeSearchCalendar?.pass === true,
        personalCacheIsolation:
            qualification.browserConsistency?.pass === true &&
            qualification.nativeConsistency?.nativePersonalCacheIsolation ===
                true &&
            qualification.nativeConsistency?.nativeFavouriteCrossClient ===
                true,
        dirtyFormsAndConflicts:
            qualification.browserConsistency?.pass === true &&
            qualification.nativeConsistency?.nativeDirtyFormPreserved ===
                true &&
            qualification.nativeConsistency?.nativeStaleWriteRejected ===
                true &&
            qualification.nativeConsistency?.nativeAcceptedStateConvergence ===
                true,
    };
    report.acceptancePass =
        !values.smoke &&
        failures.length === 0 &&
        report.timingPass &&
        completedCells.length === 12 &&
        Object.values(report.additionalGates).every(Boolean);
    fs.writeFileSync(
        path.join(values.output, "report.json"),
        JSON.stringify(report, null, 2),
        { mode: 0o600 },
    );
    if (failures.length || (!values.smoke && !report.acceptancePass))
        process.exitCode = 1;
    console.log(
        values.smoke
            ? "Smoke run recorded; this never closes performance acceptance."
            : "Full performance acceptance: " + report.acceptancePass,
    );
}
