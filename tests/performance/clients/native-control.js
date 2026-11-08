// Private loopback-only test controller and transparent TLS fault proxy; never part of the app.
import assert from "node:assert/strict";
import https from "node:https";
import crypto from "node:crypto";

export class NativeControl {
    constructor(tls, measurement) {
        Object.assign(this, { tls, measurement });
        this.agents = new Map();
        this.token = crypto.randomBytes(32).toString("hex");
    }
    async start() {
        this.server = https.createServer(
            this.tls,
            async (request, response) => {
                const supplied = Buffer.from(
                        String(request.headers["x-performance-control"] ?? ""),
                    ),
                    expected = Buffer.from(this.token);
                if (
                    supplied.length !== expected.length ||
                    !crypto.timingSafeEqual(supplied, expected)
                ) {
                    response.writeHead(403).end();
                    return;
                }
                const match =
                    /^\/agents\/([a-f0-9]+)\/(next|ready|result|status)$/.exec(
                        request.url ?? "",
                    );
                const agent = match && this.agents.get(match[1]);
                if (!agent) {
                    response.writeHead(404).end();
                    return;
                }
                const reply = (body) => {
                    if (!response.destroyed)
                        response
                            .writeHead(200, {
                                "Content-Type": "application/json",
                            })
                            .end(JSON.stringify(body));
                };
                try {
                    if (request.method === "GET" && match[2] === "next") {
                        assert.ok(
                            !agent.poll,
                            "Only one outstanding native poll",
                        );
                        agent.poll = reply;
                        const timeout = setTimeout(() => {
                            if (agent.poll === reply) {
                                agent.poll = undefined;
                                reply({});
                            }
                        }, 20000);
                        response.on("close", () => {
                            clearTimeout(timeout);
                            if (agent.poll === reply) agent.poll = undefined;
                        });
                        agent.dispatch();
                        return;
                    }
                    assert.equal(request.method, "POST");
                    const chunks = [];
                    let size = 0;
                    for await (const chunk of request) {
                        size += chunk.length;
                        assert.ok(size <= 16384);
                        chunks.push(chunk);
                    }
                    const body = JSON.parse(Buffer.concat(chunks).toString());
                    if (match[2] === "status") {
                        assert.ok(
                            ["app-launched", "server-selected"].includes(
                                body.stage,
                            ),
                        );
                        agent.stage = body.stage;
                        console.log("Native setup: " + body.stage);
                        reply({});
                        return;
                    }
                    if (match[2] === "ready") {
                        agent.ready.resolve();
                        reply({});
                        return;
                    }
                    assert.equal(
                        body.id,
                        agent.pending?.id,
                        "Unknown or duplicate command result",
                    );
                    const pending = agent.pending;
                    agent.pending = undefined;
                    if (body.ok === true)
                        pending.resolve(this.measurement.checkpoint());
                    else pending.reject(new Error("Native UI command failed"));
                    reply({});
                } catch {
                    if (!response.headersSent) response.writeHead(400).end();
                }
            },
        );
        await new Promise((resolve, reject) => {
            this.server.once("error", reject);
            this.server.listen(0, "127.0.0.1", resolve);
        });
        this.origin = "https://localhost:" + this.server.address().port;
    }
    register() {
        const id = crypto.randomBytes(12).toString("hex");
        const agent = { id, queue: [] };
        agent.ready = Promise.withResolvers();
        agent.dispatch = () => {
            if (agent.poll && agent.queue.length) {
                const send = agent.poll;
                agent.poll = undefined;
                send(agent.queue.shift());
            }
        };
        agent.command = (action, body = {}) => {
            assert.ok(!agent.pending, "Native commands are sequential");
            const deferred = Promise.withResolvers(),
                commandId = crypto.randomUUID();
            const timeout = setTimeout(() => {
                if (agent.pending?.id === commandId) {
                    agent.pending = undefined;
                    deferred.reject(
                        new Error("Native UI command timed out: " + action),
                    );
                }
            }, 90000);
            agent.pending = {
                id: commandId,
                resolve: (token) => {
                    clearTimeout(timeout);
                    deferred.resolve(token);
                },
                reject: (error) => {
                    clearTimeout(timeout);
                    deferred.reject(error);
                },
            };
            agent.queue.push({ ...body, id: commandId, action });
            agent.dispatch();
            return deferred.promise;
        };
        this.agents.set(id, agent);
        return agent;
    }
    async close() {
        for (const agent of this.agents.values()) {
            agent.poll?.({});
            agent.pending?.reject(new Error("Controller closed"));
        }
        if (this.server) {
            this.server.closeAllConnections();
            await new Promise((resolve) => this.server.close(resolve));
        }
    }
}

export class NativeProxy {
    constructor(tls, fixture, measurement) {
        Object.assign(this, { tls, fixture, measurement });
        this.sockets = new Set();
        this.offline = false;
    }
    async start() {
        const target = new URL(this.fixture.origin);
        const options = (request) => ({
            method: request.method,
            headers: { ...request.headers, host: target.host },
            ca: this.fixture.ca,
            family: 4,
        });
        this.server = https.createServer(this.tls, (request, response) => {
            if (!request.url?.startsWith("/") || request.url.startsWith("//")) {
                response.writeHead(400).end();
                return;
            }
            if (this.offline) {
                request.resume();
                response.writeHead(503).end();
                return;
            }
            const upstream = https.request(
                new URL(request.url, target),
                options(request),
                (incoming) => {
                    if (
                        this.pending &&
                        request.method === "PUT" &&
                        new URL(request.url, target).pathname ===
                            this.pending.resource
                    ) {
                        const pending = this.pending;
                        this.pending = undefined;
                        if (
                            incoming.statusCode === pending.expectedStatus &&
                            (pending.expectedStatus !== 200 ||
                                typeof incoming.headers.etag === "string")
                        )
                            pending.resolve({
                                status: incoming.statusCode,
                                revision: incoming.headers.etag,
                                acknowledged: this.measurement.checkpoint(),
                            });
                        else
                            pending.reject(
                                new Error(
                                    "Native timed write was not accepted",
                                ),
                            );
                    }
                    response.writeHead(incoming.statusCode, incoming.headers);
                    incoming.pipe(response);
                },
            );
            upstream.on("error", () => {
                if (!response.headersSent) response.writeHead(502);
                response.end();
            });
            response.on("close", () => {
                if (!response.writableFinished) upstream.destroy();
            });
            request.pipe(upstream);
        });
        this.server.on("connection", (socket) => {
            this.sockets.add(socket);
            socket.on("close", () => this.sockets.delete(socket));
        });
        this.server.on("upgrade", (request, downstream, head) => {
            if (!request.url?.startsWith("/") || request.url.startsWith("//")) {
                downstream.destroy();
                return;
            }
            if (this.offline) {
                downstream.destroy();
                return;
            }
            const upstream = https.request(
                new URL(request.url, target),
                options(request),
            );
            upstream.on("upgrade", (incoming, socket, upstreamHead) => {
                downstream.write(
                    `HTTP/1.1 ${incoming.statusCode} ${incoming.statusMessage}\r\n` +
                        incoming.rawHeaders.reduce(
                            (text, value, index, all) =>
                                index % 2
                                    ? text
                                    : text +
                                      value +
                                      ": " +
                                      all[index + 1] +
                                      "\r\n",
                            "",
                        ) +
                        "\r\n",
                );
                if (upstreamHead.length) downstream.write(upstreamHead);
                if (head.length) socket.write(head);
                socket.pipe(downstream);
                downstream.pipe(socket);
                downstream.on("close", () => socket.destroy());
                socket.on("close", () => downstream.destroy());
                socket.on("error", () => downstream.destroy());
                downstream.on("error", () => socket.destroy());
            });
            upstream.on("response", (incoming) => {
                incoming.resume();
                downstream.destroy();
            });
            upstream.on("error", () => downstream.destroy());
            upstream.end();
        });
        await new Promise((resolve, reject) => {
            this.server.once("error", reject);
            this.server.listen(0, "127.0.0.1", resolve);
        });
        this.origin = "https://localhost:" + this.server.address().port;
    }
    acknowledged(resource) {
        return this.outcome(resource, 200);
    }
    outcome(resource, expectedStatus) {
        assert.ok([200, 412].includes(expectedStatus));
        assert.ok(!this.pending);
        const deferred = Promise.withResolvers();
        const timeout = setTimeout(() => {
            this.pending = undefined;
            deferred.reject(new Error("Native accepted response not observed"));
        }, 90000);
        this.pending = {
            resource,
            expectedStatus,
            resolve: (value) => {
                clearTimeout(timeout);
                deferred.resolve(value);
            },
            reject: (error) => {
                clearTimeout(timeout);
                deferred.reject(error);
            },
        };
        return deferred.promise;
    }
    unavailable() {
        this.offline = true;
        for (const socket of this.sockets) socket.destroy();
    }
    activate() {
        this.offline = false;
        return this.measurement.checkpoint();
    }
    async close() {
        this.pending?.reject(new Error("Proxy closed"));
        this.pending = undefined;
        for (const socket of this.sockets) socket.destroy();
        if (this.server)
            await new Promise((resolve) => this.server.close(resolve));
    }
}
