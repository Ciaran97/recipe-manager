import { performance } from "node:perf_hooks";

export const pairings = [
    "angular-angular",
    "angular-ios",
    "ios-angular",
    "ios-ios",
];
export const modes = ["connected", "reconnect", "foreground"];
const entities = ["shopping", "recipe", "meal"];
export function percentile95(values) {
    if (!values.length) return null;
    return [...values].sort((a, b) => a - b)[
        Math.ceil(values.length * 0.95) - 1
    ];
}

// Only the controller supplies the clock. Device timestamps are never accepted.
export class Measurement {
    #clock;
    #samples = new Map();
    #checkpoints = new WeakMap();
    #usedCheckpoints = new WeakSet();
    constructor(clock = () => performance.now()) {
        this.#clock = clock;
    }
    checkpoint() {
        const token = Object.freeze({});
        this.#checkpoints.set(token, this.#clock());
        return token;
    }
    begin({ id, pairing, mode, entity, revision }) {
        if (
            !id ||
            this.#samples.has(id) ||
            !pairings.includes(pairing) ||
            !modes.includes(mode) ||
            !entities.includes(entity) ||
            typeof revision !== "string" ||
            !revision.length
        )
            throw new Error("Invalid or duplicate sample");
        this.#samples.set(id, { id, pairing, mode, entity, revision });
    }
    event(id, kind, revision, checkpoint) {
        const sample = this.#samples.get(id);
        if (!sample || revision !== sample.revision)
            throw new Error("Unknown sample or stale revision");
        if (
            !["acknowledged", "active", "rendered"].includes(kind) ||
            sample[kind] !== undefined
        )
            throw new Error("Invalid or duplicate event");
        if (
            kind === "active" &&
            (sample.mode === "connected" || sample.acknowledged === undefined)
        )
            throw new Error(
                "Recovery requires a successful write before reactivation",
            );
        if (
            kind === "rendered" &&
            sample.mode !== "connected" &&
            sample.active === undefined
        )
            throw new Error("Recovery render must follow reactivation");
        if (checkpoint !== undefined) {
            if (
                !this.#checkpoints.has(checkpoint) ||
                this.#usedCheckpoints.has(checkpoint)
            )
                throw new Error("Invalid or reused controller checkpoint");
        }
        const time =
            checkpoint === undefined
                ? this.#clock()
                : this.#checkpoints.get(checkpoint);
        if (
            (kind === "active" && time < sample.acknowledged) ||
            (kind === "rendered" &&
                sample.mode !== "connected" &&
                time < sample.active)
        )
            throw new Error(
                "Recovery checkpoint precedes its required transition",
            );
        sample[kind] = time;
        if (checkpoint !== undefined) this.#usedCheckpoints.add(checkpoint);
    }
    report() {
        const cells = [];
        for (const pairing of pairings)
            for (const mode of modes) {
                const selected = [...this.#samples.values()].filter(
                    (s) => s.pairing === pairing && s.mode === mode,
                );
                const complete = selected.filter(
                    (s) =>
                        s.acknowledged !== undefined &&
                        s.rendered !== undefined &&
                        (mode === "connected" || s.active !== undefined),
                );
                const durations = complete.map((s) =>
                    Math.max(
                        0,
                        s.rendered -
                            (mode === "connected" ? s.acknowledged : s.active),
                    ),
                );
                const p95Ms = percentile95(durations);
                const coverage = entities.every((entity) =>
                    complete.some((s) => s.entity === entity),
                );
                cells.push({
                    pairing,
                    mode,
                    count: complete.length,
                    incomplete: selected.length - complete.length,
                    entitiesCovered: coverage,
                    p95Ms,
                    durationsMs: durations,
                    samples: complete.map(
                        ({
                            id,
                            entity,
                            revision,
                            acknowledged,
                            active,
                            rendered,
                        }) => ({
                            id,
                            entity,
                            revision,
                            acknowledged,
                            active,
                            rendered,
                        }),
                    ),
                    pass:
                        complete.length >= 100 &&
                        complete.length === selected.length &&
                        coverage &&
                        p95Ms <= 2000,
                });
            }
        return {
            procedure: "MP-SYNC-01",
            clock: "controller-monotonic",
            cells,
            timingPass: cells.every((cell) => cell.pass),
        };
    }
}
