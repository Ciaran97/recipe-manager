# MP-SYNC-01 performance validation

Status: **partial Angular measurements recorded; T133 remains open**. Functional cross-client tests are not latency evidence. All three Angular→Angular modes are measured. Native pairings are not yet measured successfully.

## Measurement tooling prepared

`tests/performance/measurement.js` provides the shared controller-side measurement core. Run its
independent tests with:

```sh
node --test tests/performance/measurement.test.js
```

Fifteen controller/measurement tests pass (nine measurement and six private TLS/controller tests): nearest-rank p95, exact revision matching/duplicate rejection, recovery ordering,
all twelve pairing/mode cells, incomplete/entity coverage already-rendered state, deferred controller checkpoints, foreign/reused checkpoint rejection and recovery backdating rejection. These tests use synthetic clock values to verify the measuring
code; they are not measurements of Recipe Manager.

The core uses only its controller's monotonic clock. Each sample identifies a pairing, mode,
shopping/recipe/meal entity and exact acknowledged revision. Connected timing starts at successful
write acknowledgement; recovery requires an acknowledged mutation followed by an explicit observer
reactivation. A rendered acknowledgement must identify that revision. Each cell needs at least 100
complete samples, representation of all three entity types and p95 at most 2,000 ms. Missing cells,
incomplete samples and one failing cell keep the aggregate timing result false. A timing result alone
cannot establish hardware, network, personal-cache correctness or dirty-form acceptance.

The Angular workload driver now exercises actual production UI writes and visible updates for recipes, shopping items and meals. A connected smoke run with three excluded warm-ups and three measured samples completed successfully on the reference installation; this is calibration evidence only. Native workload drivers and their render instrumentation are still required. In particular, receiving a
SignalR notification or completing an HTTP fetch is not sufficient evidence that a client rendered
the accepted revision. Do not use independently timestamped device logs or replace rendering with
an API polling loop. The controller must observe successful writes and rendered revisions using the
single-clock procedure, with instrumentation overhead documented. If rendering precedes the writer's
acknowledgement, connected post-ack latency is zero rather than a negative duration.

## Reference-host operator preparation

Use an explicitly designated disposable test installation with no real household data. Do not run
seeding against an existing household, alter a shared Docker host's global resource allocation or
provision a paid host without authorization. The required host has 2 vCPUs, 4 GiB RAM and SSD-backed
storage; record the actual allocation and virtualization constraints, not just the physical host's
advertised capacity. A normal larger local Docker desktop run does not satisfy this gate.

Before running the eventual workload, retain:

- Host OS/kernel, CPU allocation, memory limit, storage type, Docker/Compose versions, exact image
  digests and applied database migration revision.
- A trusted HTTPS origin reachable by both clients, valid test-account authentication and the
  client/runtime versions (including native device or simulator details).
- RTT samples demonstrating less than 50 ms from the participating clients; state how RTT was
  measured and any relay/controller overhead.
- Exactly 500 synthetic recipes, two active authenticated household members and a warm-up phase
  excluded from reported samples. Keep seed data separate from production backups.
- A separate output directory with restricted permissions. Never record session cookies, invitation
  secrets, passwords, provider keys or private household bodies in published reports.

## Required execution and report

Run all four directional pairings: Angular→Angular, Angular→iOS, iOS→Angular and iOS→iOS. For each,
collect 100 warmed samples in each mode: connected, network reconnect and foreground return.
Exercise shopping, recipe and meal changes. Recovery mutations occur while the observer is
unavailable; start its timing when it becomes active with a reachable authenticated server. Document
that transition and the reconnection behavior rather than excluding backoff to improve the result.

Report raw durations and p95 separately for each of the twelve cells, plus incomplete/failed samples,
rendered revisions and instrumentation details. Do not combine cells into one favorable average.
Also verify same-user favourites/preferences across devices, unchanged other-user personal cache
validation, dirty-form preservation, explicit conflicts and eventual accepted-state convergence.
Measure warmed recipe search and weekly-calendar workflows separately against their specification
criteria. Record failures before making measured optimizations; retain before/after evidence.

An isolated local Linux reference VM is now configured: Ubuntu 26.04.1 ARM64, two virtual CPUs, 4 GiB allocated RAM (4,086,222,848 guest-visible bytes), SSD-backed private disk, Docker 29.9.3 and Compose 5.7.0. It has no host mounts and uses an explicitly selected Docker socket without changing the global Docker context. Operational validation is recorded separately in US7.md. The performance fixture contains exactly 500 synthetic recipes and two authenticated members.

`tests/performance/household-workload.js` accepts a private fixture directory and creates a new private report directory. The Angular driver is validated. A test-only native SwiftUI UI agent and loopback TLS controller/proxy are now implemented behind explicit `--native`; native calibration is in progress and no native timing pass is claimed. Browser TLS is first verified against the fixture CA, then its exact leaf public key is pinned only in the disposable browser profile. A successful PUT response's opaque ETag identifies the accepted revision; controller checkpoints record response acknowledgement and visible unique-marker rendering. Genuine browser online/visibility events identify recovery. No device clocks or API polling substitute for rendered UI.

A report includes all twelve required cells even for a partial run. Missing native cells, insufficient counts, ancillary correctness checks and unrecorded host/network evidence keep aggregate acceptance false. The full workload and evidence review remain necessary before checking T133. The native parity suite may run concurrently during calibration; final performance measurements must record contention and should run without competing builds.

## Partial reference measurements — 2026-11-04

The real Angular UI workload completed 100 samples per measured workflow after nine excluded warm-ups. Every synchronization cell includes shopping, recipe and meal updates. Results use nearest-rank p95:

| Workflow | Samples | p95 |
|---|---:|---:|
| Angular→Angular connected | 100 | 97.043 ms |
| Angular→Angular network reconnect | 100 | 78.229 ms |
| Angular→Angular foreground return | 100 | 76.646 ms |
| Angular recipe search | 100 | 44.706 ms |
| Angular weekly calendar open | 100 | 109.851 ms |

Raw distributions and acknowledged/rendered controller checkpoints are in [sync results](performance-results/angular-sync-reference-2026-11-04.json) and [search/calendar results](performance-results/angular-search-calendar-reference-2026-11-04.json), plus [foreground results](performance-results/angular-foreground-reference-2026-11-04.json). Backend operations ran on the specified isolated two-vCPU/four-GiB VM. The physical Mac also ran native simulator/build validation, so these are diagnostic local distributions rather than final uncontended release results. Native client and network qualification remain incomplete.

Initial foreground calibration failed honestly: neither a new browser tab nor CDP window minimization produced the required genuine hidden/visible document transition in this automation environment. No visibility event was synthesized and no failed transition was counted. Inspection of the pinned Playwright source identified its forced-focus override; attaching to owned Chromium default contexts with `noDefaults: true` removes that override. A subsequent three-sample foreground smoke and full 100-sample run passed using actual window minimization/restoration and genuine document visibility events. The browser driver now starts owned Chromium processes and attaches to default contexts with `noDefaults: true`; sending a second-session focus override alone was insufficient. Native workload drivers, all twelve successful cells, network evidence and personal-cache/dirty-form probes remain necessary. The report's aggregate acceptance is false, including explicit incomplete ancillary gates.

### Browser correctness probes

The real three-session Angular probe passes same-user favourite synchronization, personal ETag 200/304 transitions, unchanged other-member cache validation, unsaved recipe-form preservation, stale save rejection (412) and convergence after explicitly confirming discard. The accepted remote recipe remains intact. See [correctness results](performance-results/angular-consistency-reference-2026-11-04.json). These checks do not claim native-client coverage or satisfy all ancillary gates by themselves.

The fifteen controller/proxy/measurement regression tests are included in the backend/web validation script and CI. The 2026-11-04 source recheck passes them without skips.

The native transport tests also exercise real TLS WebSocket upgrade forwarding, socket closure during a simulated disconnection and rejection of absolute-form proxy URLs. These validate the measuring fixture, not application latency.
