// Read-only qualification of the owned local reference VM. Never changes Docker context/resources.
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import crypto from "node:crypto";
import { execFileSync, spawn } from "node:child_process";
import { performance } from "node:perf_hooks";
import { percentile95 } from "./measurement.js";

export function referenceHost(fixture, dockerHost) {
    assert.ok(
        dockerHost?.startsWith(
            "unix:///private/tmp/recipe-manager-reference-lima/",
        ),
        "Select the owned isolated reference VM Docker socket explicitly.",
    );
    assert.ok(fs.statSync(dockerHost.slice("unix://".length)).isSocket());
    const docker = (args) =>
        execFileSync("docker", ["--host", dockerHost, ...args], {
            encoding: "utf8",
            maxBuffer: 4 * 1024 * 1024,
        });
    const info = JSON.parse(docker(["info", "--format", "{{json .}}"]));
    assert.equal(info.NCPU, 2);
    assert.ok(
        info.MemTotal >= 3.5 * 1024 ** 3 && info.MemTotal <= 4 * 1024 ** 3,
    );
    const config = fs.readFileSync(
        path.join(fixture.directory, ".env"),
        "utf8",
    );
    const project = config.match(
        /^COMPOSE_PROJECT_NAME='(rm-validation-[a-f0-9]{8})'$/m,
    )[1];
    const container = (service) => {
        const ids = docker([
            "ps",
            "--filter",
            "label=com.docker.compose.project=" + project,
            "--filter",
            "label=com.docker.compose.service=" + service,
            "--format",
            "{{.ID}}",
        ])
            .trim()
            .split("\n")
            .filter(Boolean);
        assert.equal(
            ids.length,
            1,
            "Expected one running owned " + service + " container",
        );
        return ids[0];
    };
    const proxy = container("proxy"),
        application = container("app"),
        database = container("db");
    const actualCa = docker([
        "exec",
        proxy,
        "cat",
        "/data/caddy/pki/authorities/local/root.crt",
    ]);
    assert.equal(
        crypto.createHash("sha256").update(actualCa).digest("hex"),
        crypto.createHash("sha256").update(fixture.ca).digest("hex"),
        "The measured TLS origin must belong to this reference installation",
    );
    const image = docker([
        "inspect",
        "--format",
        "{{.Image}}",
        application,
    ]).trim();
    const migrations = docker([
        "exec",
        database,
        "psql",
        "-U",
        "recipe_manager",
        "-d",
        "recipe_manager",
        "-Atc",
        'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";',
    ])
        .trim()
        .split("\n")
        .filter(Boolean);
    assert.ok(migrations.length > 0);
    assert.equal(process.platform, "darwin");
    const storage = execFileSync("diskutil", ["info", "-plist", "/"], {
        encoding: "utf8",
    });
    const ssd = /<key>SolidState<\/key>\s*<true\s*\/>/.test(storage);
    assert.ok(
        ssd,
        "Record actual SSD-backed host storage before accepting the local VZ reference host",
    );
    return {
        qualified: true,
        cpuCount: info.NCPU,
        memoryBytes: info.MemTotal,
        operatingSystem: info.OperatingSystem,
        kernel: info.KernelVersion,
        dockerVersion: info.ServerVersion,
        storageDriver: info.Driver,
        hostSsd: ssd,
        backendImage: image,
        migrations,
        transport:
            "Owned local VZ VM; explicit socket; measured origin CA matches the VM proxy",
    };
}
export async function networkEvidence(
    actor,
    { samples = 100, warmup = 9 } = {},
) {
    const durationsMs = [];
    for (let index = -warmup; index < samples; index++) {
        const start = performance.now();
        await actor.networkProbe();
        if (index >= 0) durationsMs.push(performance.now() - start);
    }
    const maximumMs = Math.max(...durationsMs),
        p95Ms = percentile95(durationsMs);
    return {
        samples,
        warmup,
        durationsMs,
        maximumMs,
        p95Ms,
        upperBound:
            "Controller roundtrip includes instrumentation and HTTPS health processing; never subtracts their overhead",
        pass: samples >= 100 && maximumMs < 50,
    };
}
export async function clientProbe(script, fixture, directory) {
    const log = fs.openSync(directory + ".log", "a", 0o600);
    try {
        const code = await new Promise((resolve, reject) => {
            const child = spawn(
                process.execPath,
                [script, "--fixture", fixture.directory, "--output", directory],
                { stdio: ["ignore", log, log] },
            );
            child.once("error", reject);
            child.once("exit", resolve);
        });
        assert.equal(code, 0, "Read the private ancillary client probe log");
        return JSON.parse(
            fs.readFileSync(path.join(directory, "report.json"), "utf8"),
        );
    } finally {
        fs.closeSync(log);
    }
}
export async function nativeSearchCalendar(
    subject,
    state,
    { samples = 100, warmup = 9 } = {},
) {
    const durations = { search: [], calendar: [] };
    for (let index = -warmup; index < samples; index++) {
        const title =
            "MP synthetic recipe " + ((index + warmup) % 2 ? "0498" : "0499");
        await subject.agent.command("prepareSearch", { title });
        let start = performance.now();
        await subject.agent.command("submitSearch", { title });
        if (index >= 0) durations.search.push(performance.now() - start);
        start = performance.now();
        await subject.agent.command("calendarProbe", { mealId: state.mealId });
        if (index >= 0) durations.calendar.push(performance.now() - start);
        if (index >= 0 && (index + 1) % 25 === 0)
            console.log(
                `Native search/calendar: ${index + 1} warmed visible pairs`,
            );
    }
    // Preparatory probes navigate independently; the next timed sample must prepare its view again.
    subject.entity = undefined;
    const workflows = Object.entries(durations).map(
        ([workflow, durationsMs]) => ({
            workflow,
            count: durationsMs.length,
            durationsMs,
            p95Ms: percentile95(durationsMs),
            pass:
                durationsMs.length >= 100 && percentile95(durationsMs) <= 2000,
        }),
    );
    return {
        client: "SwiftUI",
        warmup,
        samples,
        clock: "Controller monotonic; conservative command dispatch through visible UI acknowledgement",
        workflows,
        pass: workflows.every((workflow) => workflow.pass),
    };
}
