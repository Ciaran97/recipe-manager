import { test } from "node:test";
import assert from "node:assert/strict";
import { Measurement, percentile95, pairings, modes } from "./measurement.js";

test("nearest-rank p95 does not average away slow samples", () => {
    assert.equal(percentile95([]), null);
    assert.equal(
        percentile95([...Array(94).fill(1), ...Array(6).fill(2500)]),
        2500,
    );
});
test("one controller clock correlates exact revision and rejects duplicate events", () => {
    let now = 10;
    const run = new Measurement(() => now);
    run.begin({
        id: "a",
        pairing: pairings[0],
        mode: "connected",
        entity: "recipe",
        revision: "r2",
    });
    assert.throws(() => run.event("a", "rendered", "r1"));
    run.event("a", "acknowledged", "r2");
    now = 120;
    run.event("a", "rendered", "r2");
    assert.throws(() => run.event("a", "rendered", "r2"));
    assert.deepEqual(run.report().cells[0].durationsMs, [110]);
    assert.equal(run.report().timingPass, false);
});
test("recovery starts after acknowledged offline mutation and explicit reactivation", () => {
    let now = 0;
    const run = new Measurement(() => now);
    run.begin({
        id: "a",
        pairing: pairings[0],
        mode: "foreground",
        entity: "meal",
        revision: "r3",
    });
    assert.throws(() => run.event("a", "active", "r3"));
    run.event("a", "acknowledged", "r3");
    assert.throws(() => run.event("a", "rendered", "r3"));
    now = 10000;
    run.event("a", "active", "r3");
    now = 10500;
    run.event("a", "rendered", "r3");
    assert.equal(run.report().cells[2].p95Ms, 500);
});
test("every pairing and mode requires 100 complete samples and all entity types", () => {
    let now = 0;
    const run = new Measurement(() => now);
    for (const pairing of pairings)
        for (const mode of modes)
            for (let i = 0; i < 100; i++) {
                const id = `${pairing}-${mode}-${i}`,
                    revision = `revision-${i}`;
                run.begin({
                    id,
                    pairing,
                    mode,
                    entity: ["shopping", "recipe", "meal"][i % 3],
                    revision,
                });
                run.event(id, "acknowledged", revision);
                if (mode !== "connected") run.event(id, "active", revision);
                now +=
                    pairing === "ios-ios" && mode === "foreground" ? 2100 : 100;
                run.event(id, "rendered", revision);
            }
    const report = run.report();
    assert.equal(report.cells.filter((cell) => cell.pass).length, 11);
    assert.equal(report.timingPass, false);
});
test("100 samples of only one entity and incomplete work cannot pass a cell", () => {
    let now = 0;
    const run = new Measurement(() => now++);
    for (let i = 0; i < 100; i++) {
        const id = `a-${i}`;
        run.begin({
            id,
            pairing: pairings[0],
            mode: "connected",
            entity: "recipe",
            revision: "r1",
        });
        run.event(id, "acknowledged", "r1");
        if (i !== 99) run.event(id, "rendered", "r1");
    }
    const cell = run.report().cells[0];
    assert.equal(cell.count, 99);
    assert.equal(cell.incomplete, 1);
    assert.equal(cell.entitiesCovered, false);
    assert.equal(cell.pass, false);
});
test("already rendered accepted state has zero post-ack delay", () => {
    let now = 0;
    const run = new Measurement(() => now);
    const sample = {
        id: "a",
        pairing: pairings[0],
        mode: "connected",
        entity: "shopping",
        revision: "r1",
    };
    run.begin(sample);
    assert.throws(() => run.begin(sample));
    run.event("a", "rendered", "r1");
    now = 10;
    run.event("a", "acknowledged", "r1");
    assert.deepEqual(run.report().cells[0].durationsMs, [0]);
});

test("controller checkpoints retain response timing before the revision body is decoded", () => {
    let now = 10;
    const run = new Measurement(() => now);
    const acknowledged = run.checkpoint();
    now = 110;
    const rendered = run.checkpoint();
    now = 500;
    run.begin({
        id: "deferred",
        pairing: pairings[0],
        mode: "connected",
        entity: "recipe",
        revision: "r4",
    });
    run.event("deferred", "acknowledged", "r4", acknowledged);
    run.event("deferred", "rendered", "r4", rendered);
    assert.deepEqual(run.report().cells[0].durationsMs, [100]);
    assert.equal(run.report().cells[0].samples[0].revision, "r4");
});
test("device timestamps, foreign checkpoints and checkpoint reuse are rejected", () => {
    const run = new Measurement(),
        other = new Measurement();
    run.begin({
        id: "a",
        pairing: pairings[0],
        mode: "connected",
        entity: "shopping",
        revision: "r1",
    });
    assert.throws(() => run.event("a", "acknowledged", "r1", 100));
    assert.throws(() =>
        run.event("a", "acknowledged", "r1", other.checkpoint()),
    );
    const token = run.checkpoint();
    run.event("a", "acknowledged", "r1", token);
    assert.throws(() => run.event("a", "rendered", "r1", token));
});

test("recovery checkpoints cannot backdate reactivation or rendering", () => {
    let now = 0;
    const run = new Measurement(() => now);
    const early = run.checkpoint();
    now = 10;
    run.begin({
        id: "recovery",
        pairing: pairings[0],
        mode: "reconnect",
        entity: "meal",
        revision: "r1",
    });
    run.event("recovery", "acknowledged", "r1");
    assert.throws(() => run.event("recovery", "active", "r1", early));
    now = 20;
    run.event("recovery", "active", "r1");
    assert.throws(() => run.event("recovery", "rendered", "r1", early));
});
