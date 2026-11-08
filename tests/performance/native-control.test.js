import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import https from "node:https";
import { createFixtureCertificates } from "./clients/native.js";
import { NativeControl, NativeProxy } from "./clients/native-control.js";
import { Measurement } from "./measurement.js";
let directory, tls, ca;
before(() => {
    directory = fs.mkdtempSync(
        path.join(os.tmpdir(), "recipe-manager-performance-control-test-"),
    );
    fs.chmodSync(directory, 0o700);
    tls = createFixtureCertificates(directory);
    ca = fs.readFileSync(path.join(directory, "root.crt"));
});
after(() => fs.rmSync(directory, { recursive: true, force: true }));
function request(origin, resource, { method = "GET", token, body } = {}) {
    return new Promise((resolve, reject) => {
        const outgoing = https.request(
            new URL(resource, origin),
            {
                method,
                ca,
                family: 4,
                agent: false,
                headers: {
                    ...(token ? { "X-Performance-Control": token } : {}),
                    ...(body ? { "Content-Type": "application/json" } : {}),
                },
            },
            (response) => {
                const chunks = [];
                response.on("data", (chunk) => chunks.push(chunk));
                response.on("end", () => {
                    const text = Buffer.concat(chunks).toString();
                    resolve({
                        status: response.statusCode,
                        headers: response.headers,
                        text,
                        json: text ? JSON.parse(text) : undefined,
                    });
                });
            },
        );
        outgoing.on("error", reject);
        if (body) outgoing.write(JSON.stringify(body));
        outgoing.end();
    });
}
test("native controller requires its private token and rejects unknown agents", async () => {
    const controller = new NativeControl(tls, new Measurement());
    await controller.start();
    try {
        assert.equal(
            (await request(controller.origin, "/agents/a/next")).status,
            403,
        );
        assert.equal(
            (
                await request(controller.origin, "/agents/a/next", {
                    token: controller.token,
                })
            ).status,
            404,
        );
    } finally {
        await controller.close();
    }
});
test("native controller correlates one UI result and rejects a stale or duplicate result", async () => {
    const measurement = new Measurement(),
        controller = new NativeControl(tls, measurement);
    await controller.start();
    try {
        const agent = controller.register(),
            base = "/agents/" + agent.id;
        const result = agent.command("observe", {
            entity: "recipe",
            marker: "unique rendered title",
        });
        assert.throws(() => agent.command("observe"), /sequential/);
        const command = await request(controller.origin, base + "/next", {
            token: controller.token,
        });
        assert.equal(command.json.action, "observe");
        assert.equal(
            (
                await request(controller.origin, base + "/result", {
                    method: "POST",
                    token: controller.token,
                    body: { id: "stale", ok: true },
                })
            ).status,
            400,
        );
        assert.equal(
            (
                await request(controller.origin, base + "/result", {
                    method: "POST",
                    token: controller.token,
                    body: { id: command.json.id, ok: true },
                })
            ).status,
            200,
        );
        const checkpoint = await result;
        measurement.begin({
            id: "proof",
            pairing: "angular-ios",
            mode: "connected",
            entity: "recipe",
            revision: "opaque",
        });
        measurement.event("proof", "acknowledged", "opaque");
        measurement.event("proof", "rendered", "opaque", checkpoint);
        assert.equal(
            (
                await request(controller.origin, base + "/result", {
                    method: "POST",
                    token: controller.token,
                    body: { id: command.json.id, ok: true },
                })
            ).status,
            400,
        );
    } finally {
        await controller.close();
    }
});
test("native TLS proxy observes exact accepted ETag and preserves real offline/recovery behavior", async () => {
    const source = https.createServer(tls, (incoming, response) => {
        incoming.resume();
        response
            .writeHead(200, {
                ETag: '"resource:7"',
                "Content-Type": "application/json",
            })
            .end('{"accepted":true}');
    });
    await new Promise((resolve) => source.listen(0, "127.0.0.1", resolve));
    const proxy = new NativeProxy(
        tls,
        { origin: "https://localhost:" + source.address().port, ca },
        new Measurement(),
    );
    await proxy.start();
    try {
        const acknowledged = proxy.acknowledged("/api/v1/recipes/fixture");
        assert.equal(
            (
                await request(proxy.origin, "/api/v1/recipes/fixture", {
                    method: "PUT",
                    body: { title: "synthetic" },
                })
            ).status,
            200,
        );
        assert.equal((await acknowledged).revision, '"resource:7"');
        proxy.unavailable();
        assert.equal(
            (await request(proxy.origin, "/api/v1/recipes/fixture")).status,
            503,
        );
        assert.ok(proxy.activate());
        assert.equal(
            (await request(proxy.origin, "/api/v1/recipes/fixture")).status,
            200,
        );
    } finally {
        await proxy.close();
        source.closeAllConnections();
        await new Promise((resolve) => source.close(resolve));
    }
});
test("native TLS proxy never acknowledges a rejected write", async () => {
    const source = https.createServer(tls, (incoming, response) => {
        incoming.resume();
        response
            .writeHead(412, { "Content-Type": "application/json" })
            .end("{}");
    });
    await new Promise((resolve) => source.listen(0, "127.0.0.1", resolve));
    const proxy = new NativeProxy(
        tls,
        { origin: "https://localhost:" + source.address().port, ca },
        new Measurement(),
    );
    await proxy.start();
    try {
        const rejected = assert.rejects(
            proxy.acknowledged("/api/v1/recipes/fixture"),
            /not accepted/,
        );
        assert.equal(
            (
                await request(proxy.origin, "/api/v1/recipes/fixture", {
                    method: "PUT",
                    body: {},
                })
            ).status,
            412,
        );
        await rejected;
    } finally {
        await proxy.close();
        source.closeAllConnections();
        await new Promise((resolve) => source.close(resolve));
    }
});

test("native TLS proxy forwards WebSocket upgrade bytes and disconnects the real socket for a fault", async () => {
    let sourceSocket;
    const frame = Buffer.from([0x81, 4, 112, 105, 110, 103]);
    const source = https.createServer(tls);
    source.on("upgrade", (_request, socket) => {
        sourceSocket = socket;
        socket.on("error", () => {});
        socket.write(
            "HTTP/1.1 101 Switching Protocols\r\nConnection: Upgrade\r\nUpgrade: websocket\r\n\r\n",
        );
        socket.write(frame);
    });
    await new Promise((resolve) => source.listen(0, "127.0.0.1", resolve));
    const proxy = new NativeProxy(
        tls,
        { origin: "https://localhost:" + source.address().port, ca },
        new Measurement(),
    );
    await proxy.start();
    let socket;
    try {
        const upgraded = await new Promise((resolve, reject) => {
            const outgoing = https.request(
                new URL("/hubs/shopping?id=synthetic", proxy.origin),
                {
                    ca,
                    family: 4,
                    agent: false,
                    headers: { Connection: "Upgrade", Upgrade: "websocket" },
                },
            );
            outgoing.on("upgrade", (_response, value, head) =>
                resolve({ socket: value, head }),
            );
            outgoing.on("error", reject);
            outgoing.end();
        });
        socket = upgraded.socket;
        const bytes = upgraded.head.length
            ? upgraded.head
            : await new Promise((resolve) => socket.once("data", resolve));
        assert.deepEqual(bytes, frame);
        let timeout;
        const closed = new Promise((resolve, reject) => {
            timeout = setTimeout(
                () => reject(new Error("Fault did not close native WebSocket")),
                2000,
            );
            socket.once("close", resolve);
        });
        proxy.unavailable();
        try {
            await closed;
        } finally {
            clearTimeout(timeout);
        }
    } finally {
        socket?.destroy();
        sourceSocket?.destroy();
        await proxy.close();
        source.closeAllConnections();
        await new Promise((resolve) => source.close(resolve));
    }
});

test("native TLS proxy rejects absolute-form URLs rather than forwarding to another origin", async () => {
    let forwarded = 0;
    const source = https.createServer(tls, (_incoming, response) => {
        forwarded++;
        response.writeHead(200).end();
    });
    await new Promise((resolve) => source.listen(0, "127.0.0.1", resolve));
    const proxy = new NativeProxy(
        tls,
        { origin: "https://localhost:" + source.address().port, ca },
        new Measurement(),
    );
    await proxy.start();
    try {
        const status = await new Promise((resolve, reject) => {
            const outgoing = https.request(
                proxy.origin,
                {
                    ca,
                    family: 4,
                    agent: false,
                    path: "https://example.invalid/private",
                    method: "GET",
                },
                (response) => {
                    response.resume();
                    response.on("end", () => resolve(response.statusCode));
                },
            );
            outgoing.on("error", reject);
            outgoing.end();
        });
        assert.equal(status, 400);
        assert.equal(forwarded, 0);
    } finally {
        await proxy.close();
        source.closeAllConnections();
        await new Promise((resolve) => source.close(resolve));
    }
});
