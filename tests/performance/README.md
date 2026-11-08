# Live performance probes

These probes use production Angular UI and the real API on an **owned disposable reference fixture**, with exactly 500 synthetic recipes and two members. They never reset an installation. They require the pinned `tests/e2e` dependencies and its Playwright Chromium installation.

The private fixture directory must contain the guarded deployment `.env`, `root.crt` and `verification.json` created by operational validation. These contain credentials: keep the directory private and outside source control. Preparing a workload is resumable; do not point it at an ordinary household.

```sh
node --test tests/performance/measurement.test.js
node tests/performance/household-workload.js --fixture /private/path --output /private/new-sync-report --pairing angular-angular --mode connected --mode reconnect
node tests/performance/search-calendar.js --fixture /private/path --output /private/new-search-report
node tests/performance/consistency.js --fixture /private/path --output /private/new-consistency-report
```

Set `PLAYWRIGHT_BROWSERS_PATH` if Chromium is installed outside its usual cache. Output directories must be new. Reports and failure details have private permissions. Published reports must contain only synthetic identifiers and no authentication material.

Each timing probe excludes nine warm-ups and collects 100 samples by default. `--samples 3 --warmup 3 --smoke` calibrates the driver and **cannot pass acceptance**. Synchronization uses one controller clock, opaque accepted ETags and visible unique markers, never independently timestamped device logs. The response's successful headers acknowledge the committed write; observer rendering may precede that acknowledgement, producing zero post-ack delay.

Foreground mode requires `--headed`. The driver launches owned Chromium profiles and attaches with `noDefaults: true`, avoiding Playwright's forced focus emulation, then uses actual window minimization/restoration, checking real document visibility events. It fails if the environment cannot produce those events; it never dispatches fabricated visibility events. Network recovery uses actual offline/online browser-context transitions.

Angular-only runs leave native cells missing and return nonzero for aggregate acceptance even when their measured cells pass. Native workload drivers, complete hardware/network qualification and cross-client correctness evidence remain required by MP-SYNC-01. Search/calendar probes report Angular timing independently and do not close native acceptance. Do not run the final benchmark alongside competing builds; record controller/polling overhead and all failed samples.

Native calibration is available on macOS with the pinned Xcode and installed iOS 27.0 runtime:

```sh
node tests/performance/household-workload.js --fixture /private/path --output /private/new-native-report --native --pairing ios-angular --mode connected --samples 3 --warmup 3 --smoke
```

The native driver builds a Release simulator app with testability enabled and launches a test-only XCTest UI agent. It creates its own simulators and a two-day private fixture CA trusted only in those simulators. It never installs a profile on a physical phone or changes macOS trust. Credential-bearing xctestrun files, keys, logs and results remain in the private output directory. No shipping app hooks, test API endpoints or duplicate business rules are added.

A loopback TLS proxy validates the real backend's TLS, transparently forwards HTTP/SignalR and records the exact successful write ETag before forwarding its response. This starts timing slightly earlier than native receipt, conservatively including forwarding overhead. Reconnect faults close only that actor's proxy connections, retaining actual client retry/backoff; they do not fabricate an NWPath change. Native foreground timing conservatively includes controller command delivery and application activation. Rendering requires the real SwiftUI marker to exist and be hittable, acknowledged to the same controller clock.

`--native-derived-data /absolute/private/path` can reuse optimized build products between **sequential** runs. Never share it between concurrent builds. By default each output directory owns separate products. Every native agent must terminate successfully with one passed test and zero skips/failures; its owned simulator is then removed. Controller/proxy regression tests run with `node --test tests/performance/measurement.test.js tests/performance/native-control.test.js` and require normal loopback listener access. Native calibration is still being validated; incomplete or failed runs do not close T133.

Full qualification additionally requires `--qualify --reference-docker-host unix:///private/tmp/recipe-manager-reference-lima/rm-reference/sock/docker.sock`. This read-only check validates the owned local VM's actual CPU/memory, proxy CA, image/migrations and SSD backing. The run collects conservative controller-clock network roundtrips from both actors, browser/native cache/conflict probes and warmed search/calendar samples. Native calibration remains open; qualification flags do not waive missing or failed cells. Keep the Mac awake during the run (for example, `caffeinate -i node …`) and avoid competing builds.
