import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import https from "node:https";
import crypto from "node:crypto";

export class Session {
    constructor(origin, ca, cookies = []) {
        this.origin = origin;
        this.ca = ca;
        this.cookies = new Map(cookies);
    }
    async request(method, resource, body, etag, cacheTag) {
        assert.ok(resource.startsWith("/api/v1/"));
        const headers = { "Accept-Encoding": "identity" };
        if (!["GET", "HEAD"].includes(method))
            headers["X-XSRF-TOKEN"] = (
                await this.request("GET", "/api/v1/auth/csrf")
            ).json.token;
        headers.Cookie = [...this.cookies.values()].join("; ");
        if (etag) headers["If-Match"] = etag;
        if (cacheTag) headers["If-None-Match"] = cacheTag;
        if (method === "POST") headers["Idempotency-Key"] = crypto.randomUUID();
        const bytes =
            body === undefined ? undefined : Buffer.from(JSON.stringify(body));
        if (bytes) {
            headers["Content-Type"] = "application/json";
            headers["Content-Length"] = bytes.length;
        }
        return new Promise((resolve, reject) => {
            const outgoing = https.request(
                new URL(resource, this.origin),
                { method, headers, ca: this.ca, family: 4, timeout: 30000 },
                (response) => {
                    for (const value of response.headers["set-cookie"] ?? []) {
                        const pair = value.split(";")[0];
                        this.cookies.set(
                            pair.slice(0, pair.indexOf("=")),
                            pair,
                        );
                    }
                    const chunks = [];
                    response.on("data", (chunk) => chunks.push(chunk));
                    response.on("end", () => {
                        const data = Buffer.concat(chunks);
                        let json;
                        try {
                            json = data.length
                                ? JSON.parse(data.toString())
                                : undefined;
                        } catch {
                            reject(new Error("Expected an API JSON response"));
                            return;
                        }
                        resolve({
                            status: response.statusCode,
                            json,
                            etag: response.headers.etag,
                            headers: response.headers,
                        });
                    });
                },
            );
            outgoing.on("timeout", () =>
                outgoing.destroy(new Error("Reference API timeout")),
            );
            outgoing.on("error", reject);
            if (bytes) outgoing.write(bytes);
            outgoing.end();
        });
    }
    async read(resource) {
        const result = await this.request("GET", resource);
        assert.equal(result.status, 200, resource);
        return result;
    }
    async write(method, resource, body, expected, etag) {
        const result = await this.request(method, resource, body, etag);
        assert.equal(
            result.status,
            expected,
            `${method} ${resource}: ${result.json?.code ?? ""}`,
        );
        return result;
    }
}
export async function loadFixture(directory) {
    const config = fs.readFileSync(path.join(directory, ".env"), "utf8");
    assert.match(
        config,
        /^COMPOSE_PROJECT_NAME='rm-validation-[a-f0-9]{8}'$/m,
        "Use only an owned reference validation project.",
    );
    const origin = config.match(
        /^RECIPE_MANAGER_PUBLIC_ORIGIN='([^']+)'$/m,
    )?.[1];
    assert.ok(
        origin &&
            new URL(origin).protocol === "https:" &&
            new URL(origin).hostname === "localhost",
    );
    const ca = fs.readFileSync(path.join(directory, "root.crt"));
    const record = JSON.parse(
        fs.readFileSync(path.join(directory, "verification.json"), "utf8"),
    );
    const session = new Session(origin, ca, record.cookies);
    await session.read("/api/v1/auth/me"); // Normal certificate, authentication and membership validation.
    return {
        directory,
        origin,
        ca,
        session,
        admin: { email: record.email, password: record.password },
    };
}
async function allRecipes(session) {
    const items = [];
    let cursor;
    do {
        const response = await session.read(
            "/api/v1/recipes?limit=100&archived=include" +
                (cursor ? "&cursor=" + encodeURIComponent(cursor) : ""),
        );
        items.push(...response.json.items);
        cursor = response.json.nextCursor;
    } while (cursor);
    return items;
}
export async function seedFixture(fixture) {
    const { directory, session } = fixture;
    const file = path.join(directory, "workload-state.json");
    if (fs.existsSync(file)) {
        const state = JSON.parse(fs.readFileSync(file));
        assert.equal((await allRecipes(session)).length, 500);
        assert.equal(
            (await session.read("/api/v1/household/members")).json.items.length,
            2,
            "The reference workload requires exactly two members",
        );
        return state;
    }
    const initial = await allRecipes(session);
    assert.ok(initial.length <= 500);
    const catalogue = (
        await session.read(
            "/api/v1/ingredients?q=MP%20fixture%20ingredient&limit=100",
        )
    ).json.items;
    const ingredient =
        catalogue.find(
            (item) => item.canonicalName === "MP fixture ingredient",
        ) ??
        (
            await session.write(
                "POST",
                "/api/v1/ingredients",
                { canonicalName: "MP fixture ingredient" },
                201,
            )
        ).json;
    const collections = [],
        tags = [];
    const oldCollections = (await session.read("/api/v1/collections?limit=100"))
        .json.items;
    for (let i = 0; i < 10; i++)
        collections.push(
            (
                oldCollections.find(
                    (item) => item.name === "MP collection " + i,
                ) ??
                (
                    await session.write(
                        "POST",
                        "/api/v1/collections",
                        { name: "MP collection " + i },
                        201,
                    )
                ).json
            ).id,
        );
    const oldTags = (await session.read("/api/v1/tags?limit=100")).json.items;
    for (let i = 0; i < 20; i++)
        tags.push(
            (
                oldTags.find((item) => item.name === "MP tag " + i) ??
                (
                    await session.write(
                        "POST",
                        "/api/v1/tags",
                        { name: "MP tag " + i },
                        201,
                    )
                ).json
            ).id,
        );
    let recipeId =
        initial.find((item) => item.title.startsWith("MP synthetic recipe"))
            ?.id ?? initial[0]?.id;
    for (let i = initial.length; i < 500; i++) {
        const response = await session.write(
            "POST",
            "/api/v1/recipes",
            {
                title: "MP synthetic recipe " + String(i).padStart(4, "0"),
                defaultServings: "2",
                measurementStandard: "AU",
                groups: [],
                ingredients: [
                    {
                        position: 0,
                        ingredientId: ingredient.id,
                        quantityText: "100",
                        kind: "numeric",
                        unit: "g",
                        uncertain: false,
                    },
                ],
                instructions: [
                    { position: 0, text: "Prepare this synthetic test meal." },
                ],
                collectionIds: [collections[i % collections.length]],
                tagIds: [tags[i % tags.length]],
            },
            201,
        );
        if (i === initial.length) recipeId = response.json.id;
    }
    const oldLists = (await session.read("/api/v1/shopping-lists")).json.items;
    const list =
        oldLists.find((item) => item.name === "MP shared shopping") ??
        (
            await session.write(
                "POST",
                "/api/v1/shopping-lists",
                { name: "MP shared shopping" },
                201,
            )
        ).json;
    const existingList = await session.read(
        `/api/v1/shopping-lists/${list.id}`,
    );
    const added = existingList.json.items.some(
        (item) => item.label === "MP initial item",
    )
        ? existingList.json
        : (
              await session.write(
                  "POST",
                  `/api/v1/shopping-lists/${list.id}/items`,
                  {
                      label: "MP initial item",
                      quantityText: "1",
                      kind: "numeric",
                      unit: "item",
                  },
                  201,
                  existingList.etag,
              )
          ).json;
    const itemId = added.items.find(
        (item) => item.label === "MP initial item",
    ).id;
    const settings = (await session.read("/api/v1/household/settings")).json;
    const today = new Intl.DateTimeFormat("en-CA", {
        timeZone: settings.timezone,
        year: "numeric",
        month: "2-digit",
        day: "2-digit",
    }).format(new Date());
    const date = new Date(today + "T12:00:00Z");
    date.setUTCDate(date.getUTCDate() - ((date.getUTCDay() + 6) % 7));
    const monday = date.toISOString().slice(0, 10);
    const slots = (await session.read("/api/v1/meal-slots")).json.items;
    const slotId = slots.find((slot) => slot.name === "Dinner").id;
    const last = new Date(date);
    last.setUTCDate(last.getUTCDate() + 6);
    const calendar = (
        await session.read(
            `/api/v1/meals?from=${monday}&to=${last.toISOString().slice(0, 10)}`,
        )
    ).json.items;
    const meal =
        calendar.find((item) => item.label === "MP initial meal") ??
        (
            await session.write(
                "POST",
                "/api/v1/meals/other",
                { date: monday, slotId, label: "MP initial meal" },
                201,
            )
        ).json;
    const pending = path.join(directory, "workload-member.json");
    const member = fs.existsSync(pending)
        ? JSON.parse(fs.readFileSync(pending))
        : {
              email: "mp-member@example.test",
              password: crypto.randomBytes(32).toString("base64url"),
          };
    if (!fs.existsSync(pending))
        fs.writeFileSync(pending, JSON.stringify(member), {
            mode: 0o600,
            flag: "wx",
        });
    const joining = new Session(fixture.origin, fixture.ca);
    // A lost successful invitation response must not strand a partially prepared fixture.
    const members = (await session.read("/api/v1/household/members")).json
        .items;
    if (members.some((item) => item.email === member.email)) {
        await joining.write("POST", "/api/v1/auth/login", member, 200);
    } else {
        assert.equal(
            members.length,
            1,
            "Never add a workload user to an unrelated household",
        );
        const invitation = (
            await session.write(
                "POST",
                "/api/v1/household/invitations",
                {},
                201,
            )
        ).json;
        await joining.write(
            "POST",
            "/api/v1/invitations/accept",
            { token: invitation.token, ...member, name: "MP observer" },
            201,
        );
    }
    assert.equal(
        (await session.read("/api/v1/household/members")).json.items.length,
        2,
    );
    const state = {
        recipeId,
        listId: list.id,
        itemId,
        mealId: meal.id,
        monday,
        slotId,
        member,
    };
    fs.writeFileSync(file, JSON.stringify(state), { mode: 0o600, flag: "wx" });
    assert.equal((await allRecipes(session)).length, 500);
    return state;
}
