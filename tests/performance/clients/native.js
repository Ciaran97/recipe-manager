// Disposable simulator driver. All recipe/planner/shopping mutations use the real SwiftUI UI.
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { spawn, execFileSync } from "node:child_process";
import { NativeControl, NativeProxy } from "./native-control.js";

function tool(executable, args, log) {
    const descriptor = fs.openSync(log, "a", 0o600);
    const process = spawn(executable, args, {
        stdio: ["ignore", descriptor, descriptor],
        env: {
            ...globalThis.process.env,
            DEVELOPER_DIR:
                globalThis.process.env.DEVELOPER_DIR ??
                "/Applications/Xcode.app/Contents/Developer",
        },
    });
    fs.closeSync(descriptor);
    const completed = new Promise((resolve) => {
        process.once("error", () => resolve({ code: -1 }));
        process.once("exit", (code, signal) => resolve({ code, signal }));
    });
    return { process, completed };
}
async function bounded(promise, milliseconds, label) {
    let timer;
    try {
        return await Promise.race([
            promise,
            new Promise((_, reject) => {
                timer = setTimeout(
                    () => reject(new Error(label)),
                    milliseconds,
                );
            }),
        ]);
    } finally {
        clearTimeout(timer);
    }
}
async function run(executable, args, log) {
    const result = await tool(executable, args, log).completed;
    assert.equal(
        result.code,
        0,
        path.basename(executable) + " failed; inspect private tool log",
    );
}
export function createFixtureCertificates(directory) {
    const config = `[req]\nprompt=no\ndistinguished_name=dn\nx509_extensions=ca\n[dn]\nCN=Recipe Manager Private Performance Fixture\n[ca]\nbasicConstraints=critical,CA:TRUE\nkeyUsage=critical,keyCertSign,cRLSign\n[server]\nbasicConstraints=critical,CA:FALSE\nkeyUsage=critical,digitalSignature,keyEncipherment\nextendedKeyUsage=serverAuth\nsubjectAltName=DNS:localhost,IP:127.0.0.1\n`;
    fs.writeFileSync(path.join(directory, "openssl.cnf"), config, {
        mode: 0o600,
    });
    const openssl = (args) =>
        execFileSync("openssl", args, { cwd: directory, stdio: "ignore" });
    openssl([
        "req",
        "-x509",
        "-newkey",
        "rsa:2048",
        "-nodes",
        "-days",
        "2",
        "-config",
        "openssl.cnf",
        "-keyout",
        "root.key",
        "-out",
        "root.crt",
    ]);
    openssl([
        "req",
        "-new",
        "-newkey",
        "rsa:2048",
        "-nodes",
        "-subj",
        "/CN=localhost",
        "-keyout",
        "server.key",
        "-out",
        "server.csr",
    ]);
    openssl([
        "x509",
        "-req",
        "-in",
        "server.csr",
        "-CA",
        "root.crt",
        "-CAkey",
        "root.key",
        "-CAcreateserial",
        "-days",
        "2",
        "-extfile",
        "openssl.cnf",
        "-extensions",
        "server",
        "-out",
        "server.crt",
    ]);
    for (const name of fs.readdirSync(directory))
        fs.chmodSync(path.join(directory, name), 0o600);
    return {
        key: fs.readFileSync(path.join(directory, "server.key")),
        cert: fs.readFileSync(path.join(directory, "server.crt")),
    };
}
export class NativeHarness {
    constructor(fixture, measurement, directory, derivedDirectory) {
        Object.assign(this, {
            fixture,
            measurement,
            directory,
            derivedDirectory,
        });
        this.simulators = new Set();
        this.actors = new Set();
    }
    async initialize() {
        assert.equal(
            process.platform,
            "darwin",
            "Native measurements require macOS/Xcode.",
        );
        fs.mkdirSync(this.directory, { mode: 0o700 });
        const tlsDirectory = path.join(this.directory, "tls");
        fs.mkdirSync(tlsDirectory, { mode: 0o700 });
        this.tls = createFixtureCertificates(tlsDirectory);
        this.rootCertificate = path.join(tlsDirectory, "root.crt");
        this.control = new NativeControl(this.tls, this.measurement);
        await this.control.start();
        this.builder = await this.createSimulator();
        this.derived =
            this.derivedDirectory ?? path.join(this.directory, "DerivedData");
        assert.ok(
            path.isAbsolute(this.derived),
            "Native DerivedData must be an absolute path",
        );
        if (!fs.existsSync(this.derived))
            fs.mkdirSync(this.derived, { recursive: true, mode: 0o700 });
        await run(
            "xcodebuild",
            [
                "-project",
                "ios/RecipeManager.xcodeproj",
                "-scheme",
                "RecipeManager",
                "-configuration",
                "Release",
                "-destination",
                "platform=iOS Simulator,id=" + this.builder,
                "-derivedDataPath",
                this.derived,
                "-onlyUsePackageVersionsFromResolvedFile",
                "ENABLE_TESTABILITY=YES",
                "ONLY_ACTIVE_ARCH=YES",
                "CODE_SIGNING_ALLOWED=YES",
                "CODE_SIGN_IDENTITY=-",
                "build-for-testing",
            ],
            path.join(this.directory, "build.log"),
        );
        const products = path.join(this.derived, "Build/Products");
        const files = fs
            .readdirSync(products)
            .filter((file) => /^RecipeManager_.*\.xctestrun$/.test(file));
        assert.ok(
            files.length > 0,
            "The native test build must generate an xctestrun file",
        );
        // Reused products may retain the earlier universal simulator run file.
        // Use the fresh run file generated by this successful active-architecture build.
        files.sort(
            (left, right) =>
                fs.statSync(path.join(products, right)).mtimeMs -
                fs.statSync(path.join(products, left)).mtimeMs,
        );
        this.runFile = path.join(products, files[0]);
    }
    async createSimulator() {
        const environment = {
            ...process.env,
            DEVELOPER_DIR:
                process.env.DEVELOPER_DIR ??
                "/Applications/Xcode.app/Contents/Developer",
        };
        const devices = JSON.parse(
            execFileSync("xcrun", ["simctl", "list", "devicetypes", "--json"], {
                env: environment,
            }),
        );
        const device =
            devices.devicetypes.find((item) => item.name === "iPhone 17 Pro") ??
            devices.devicetypes.find((item) => item.name.startsWith("iPhone"));
        const runtimes = JSON.parse(
            execFileSync("xcrun", ["simctl", "list", "runtimes", "--json"], {
                env: environment,
            }),
        );
        const runtime = runtimes.runtimes.find(
            (item) => item.isAvailable && item.name === "iOS 27.0",
        );
        assert.ok(
            device && runtime,
            "Use the installed, recorded iPhone/iOS 27.0 simulator runtime.",
        );
        const id = execFileSync(
            "xcrun",
            [
                "simctl",
                "create",
                "Recipe Manager Private Performance",
                device.identifier,
                runtime.identifier,
            ],
            { env: environment, encoding: "utf8" },
        ).trim();
        assert.match(id, /^[A-F0-9-]{36}$/i);
        this.simulators.add(id);
        const log = path.join(this.directory, "simulator-" + id + ".log");
        await run("xcrun", ["simctl", "boot", id], log);
        await run("xcrun", ["simctl", "bootstatus", id, "-b"], log);
        await run(
            "xcrun",
            ["simctl", "keychain", id, "add-root-cert", this.rootCertificate],
            log,
        );
        if (
            fs.existsSync(
                path.join(
                    process.env.DEVELOPER_DIR ??
                        "/Applications/Xcode.app/Contents/Developer",
                    "Applications/Simulator.app",
                ),
            )
        )
            await run(
                "open",
                [
                    "-a",
                    path.join(
                        process.env.DEVELOPER_DIR ??
                            "/Applications/Xcode.app/Contents/Developer",
                        "Applications/Simulator.app",
                    ),
                    "--args",
                    "-CurrentDeviceUDID",
                    id,
                ],
                log,
            );
        return id;
    }
    async startActor(actor) {
        const simulator = this.builder ?? (await this.createSimulator());
        this.builder = undefined;
        actor.simulator = simulator;
        const file = path.join(this.directory, actor.agent.id + ".xctestrun");
        const values = {
            RECIPE_MANAGER_RUN_PERFORMANCE: "1",
            RECIPE_MANAGER_PERF_ORIGIN: actor.proxy.origin,
            RECIPE_MANAGER_PERF_CONTROL: this.control.origin,
            RECIPE_MANAGER_PERF_TOKEN: this.control.token,
            RECIPE_MANAGER_PERF_AGENT: actor.agent.id,
            RECIPE_MANAGER_PERF_EMAIL: actor.credentials.email,
            RECIPE_MANAGER_PERF_PASSWORD: actor.credentials.password,
        };
        const config = path.join(this.directory, actor.agent.id + ".json");
        fs.writeFileSync(config, JSON.stringify(values), { mode: 0o600 });
        execFileSync(
            "python3",
            [
                "-c",
                `import json,plistlib,sys,os
source,output,configuration=sys.argv[1:]
data=plistlib.load(open(source,'rb'))
def absolute(value):
 if isinstance(value,str): return value.replace('__TESTROOT__',os.path.dirname(source))
 if isinstance(value,list): return [absolute(item) for item in value]
 if isinstance(value,dict): return {key:absolute(item) for key,item in value.items()}
 return value
data=absolute(data)
if 'TestConfigurations' in data: targets=[target for item in data['TestConfigurations'] for target in item['TestTargets']]
else: targets=[value for key,value in data.items() if key=='RecipeManagerUITests']
for target in targets:
 if target.get('BlueprintName','RecipeManagerUITests')=='RecipeManagerUITests': target.setdefault('EnvironmentVariables',{}).update(json.load(open(configuration)))
with open(output,'wb') as stream: plistlib.dump(data,stream)
os.chmod(output,0o600)`,
                this.runFile,
                file,
                config,
            ],
            { stdio: "ignore" },
        );
        const resultPath = path.join(
            this.directory,
            actor.agent.id + ".xcresult",
        );
        actor.resultPath = resultPath;
        const execution = tool(
            "xcodebuild",
            [
                "test-without-building",
                "-xctestrun",
                file,
                "-destination",
                "platform=iOS Simulator,id=" + simulator,
                "-only-testing:RecipeManagerUITests/NativePerformanceUITests",
                "-parallel-testing-enabled",
                "NO",
                "-test-timeouts-enabled",
                "YES",
                "-maximum-test-execution-time-allowance",
                "28800",
                "-default-test-execution-time-allowance",
                "28800",
                "-resultBundlePath",
                resultPath,
                "CODE_SIGNING_ALLOWED=YES",
                "CODE_SIGN_IDENTITY=-",
            ],
            path.join(this.directory, actor.agent.id + "-test.log"),
        );
        actor.execution = execution;
        let timeout;
        try {
            await Promise.race([
                actor.agent.ready.promise,
                execution.completed.then(() => {
                    throw new Error("Native agent exited before ready");
                }),
                new Promise((_, reject) => {
                    timeout = setTimeout(
                        () =>
                            reject(new Error("Native agent readiness timeout")),
                        900000,
                    );
                }),
            ]);
        } finally {
            clearTimeout(timeout);
        }
    }
    async close() {
        for (const actor of [...this.actors])
            await actor.close().catch(() => {});
        if (this.control) await this.control.close();
        for (const id of this.simulators) {
            const log = path.join(this.directory, "simulator-" + id + ".log");
            await run("xcrun", ["simctl", "shutdown", id], log).catch(() => {});
            await run("xcrun", ["simctl", "delete", id], log).catch(() => {});
        }
    }
}
export class NativeActor {
    constructor(harness, fixture, state, credentials, measurement) {
        Object.assign(this, {
            harness,
            fixture,
            state,
            credentials,
            measurement,
        });
    }
    async start() {
        this.agent = this.harness.control.register();
        this.proxy = new NativeProxy(
            this.harness.tls,
            this.fixture,
            this.measurement,
        );
        await this.proxy.start();
        this.harness.actors.add(this);
        await this.harness.startActor(this);
    }
    async prepare(entity) {
        if (this.entity === entity) return;
        await this.focusSimulator();
        const title =
            entity === "recipe"
                ? (
                      await this.fixture.session.read(
                          "/api/v1/recipes/" + this.state.recipeId,
                      )
                  ).json.title
                : undefined;
        await this.agent.command("prepare", {
            entity,
            title,
            ...this.state,
            member: undefined,
        });
        this.entity = entity;
    }
    async write(entity, marker) {
        await this.focusSimulator();
        const resource =
            entity === "recipe"
                ? "/api/v1/recipes/" + this.state.recipeId
                : entity === "shopping"
                  ? `/api/v1/shopping-lists/${this.state.listId}/items/${this.state.itemId}`
                  : "/api/v1/meals/" + this.state.mealId + "/other";
        const accepted = this.proxy.acknowledged(resource).then(
            (value) => ({ value }),
            (error) => ({ error }),
        );
        await this.agent.command("write", {
            entity,
            marker,
            itemId: this.state.itemId,
            mealId: this.state.mealId,
        });
        const result = await accepted;
        if (result.error) throw result.error;
        return result.value;
    }
    observe(entity, marker) {
        return this.agent.command("observe", { entity, marker });
    }
    async unavailable(mode) {
        if (mode === "reconnect") this.proxy.unavailable();
        else await this.agent.command("background");
    }
    async assertUnavailable(entity, marker, mode) {
        await this.agent.command(
            mode === "reconnect" ? "assertAbsent" : "assertBackground",
            { entity, marker },
        );
    }
    async activate(mode) {
        if (mode === "reconnect") return this.proxy.activate();
        // Conservative interval includes activation command delivery and native foregrounding.
        const token = this.measurement.checkpoint();
        await this.agent.command("foreground");
        return token;
    }
    async focusSimulator() {
        if (
            this.simulator &&
            fs.existsSync(
                path.join(
                    process.env.DEVELOPER_DIR ??
                        "/Applications/Xcode.app/Contents/Developer",
                    "Applications/Simulator.app",
                ),
            )
        )
            await run(
                "open",
                [
                    "-a",
                    path.join(
                        process.env.DEVELOPER_DIR ??
                            "/Applications/Xcode.app/Contents/Developer",
                        "Applications/Simulator.app",
                    ),
                    "--args",
                    "-CurrentDeviceUDID",
                    this.simulator,
                ],
                path.join(
                    this.harness.directory,
                    "simulator-" + this.simulator + ".log",
                ),
            );
    }
    networkProbe() {
        return this.agent.command("network");
    }
    async close() {
        if (this.closed) return;
        this.closed = true;
        let clean = false;
        try {
            if (
                this.execution &&
                this.execution.process.exitCode === null &&
                !this.agent.pending
            ) {
                await this.agent.command("stop");
                const result = await bounded(
                    this.execution.completed,
                    60000,
                    "Native agent did not terminate after stop",
                );
                assert.equal(result.code, 0);
                clean = true;
            }
        } finally {
            if (this.execution && this.execution.process.exitCode === null) {
                this.execution.process.kill("SIGTERM");
                await bounded(
                    this.execution.completed,
                    10000,
                    "Native agent termination timeout",
                ).catch(async () => {
                    this.execution.process.kill("SIGKILL");
                    await this.execution.completed;
                });
            }
            if (this.proxy) await this.proxy.close();
            this.harness.actors.delete(this);
            if (this.simulator) {
                const log = path.join(
                    this.harness.directory,
                    "simulator-" + this.simulator + ".log",
                );
                await run(
                    "xcrun",
                    ["simctl", "shutdown", this.simulator],
                    log,
                ).catch(() => {});
                await run("xcrun", ["simctl", "delete", this.simulator], log);
                this.harness.simulators.delete(this.simulator);
            }
        }
        if (this.execution)
            assert.ok(clean, "Native UI agent did not finish cleanly");
        if (clean) {
            const summary = JSON.parse(
                execFileSync(
                    "xcrun",
                    [
                        "xcresulttool",
                        "get",
                        "test-results",
                        "summary",
                        "--path",
                        this.resultPath,
                    ],
                    {
                        env: {
                            ...process.env,
                            DEVELOPER_DIR:
                                process.env.DEVELOPER_DIR ??
                                "/Applications/Xcode.app/Contents/Developer",
                        },
                    },
                ),
            );
            assert.equal(summary.failedTests, 0);
            assert.equal(summary.skippedTests, 0);
            assert.equal(summary.passedTests, 1);
        }
    }
}
