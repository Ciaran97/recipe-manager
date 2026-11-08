# Release evidence and outstanding gates

This is a release checklist, not a statement that Recipe Manager is ready for publication.
The implementation task state is in specs/001-household-recipe-manager/tasks.md. Keep incomplete
items open until the named evidence exists; do not substitute unit tests for device or workload tests.

## Local evidence (2026-11-03–05)

- [x] .NET SDK 10.0.403 / ASP.NET runtime 10.0.14; 348 backend tests passed across unit,
  architecture and PostgreSQL integration suites, including session revocation and isolation.
- [x] Angular 22.2.4 / CLI 22.2.5 / TypeScript 6.0.2; 63 tests, production build, lint and formatting passed.
- [x] Xcode 27.2 / Apple Swift 6.4.2 (language mode 6); 95 app/handoff tests passed. Signed simulator authentication,
  organisers, review/share import and lost-commit recovery acceptance passed.
- [x] OpenAPI, generated TypeScript and Swift contracts rechecked against the shared backend after the release changes.
- [x] Production linux/arm64 and linux/amd64 images built; non-root runtime, TLS proxy,
  migrations, image sanitisation, protected configuration and SignalR exercised.
- [x] Paused-write backup restored into separate PostgreSQL/file volumes; sessions, images,
  encrypted settings, shopping manual/purchased state and notifications retained.
- [x] Automated web WCAG A/AA checks and no horizontal overflow on nine screens at 320px
  and 1280px, with keyboard skip-link and recipe-entry navigation.

## Executable validation

Install pinned dependencies first with `dotnet restore RecipeManager.slnx --locked-mode` and
`npm ci --prefix` for `src/RecipeManager.Web`, `tests/e2e` and `scripts/contracts`. Install Chromium
with `npx --prefix tests/e2e playwright install chromium`. Docker must be running for integration
and browser acceptance; Xcode and its installed simulator runtime are required for native checks.

```sh
scripts/validate-release.sh --backend-web
RECIPE_MANAGER_SIMULATOR_ID=<installed-iphone-uuid> scripts/validate-release.sh --native
scripts/validate-release.sh --native-parity
scripts/validate-release.sh --native-acceptance
```

The original thirteen-suite `--native-parity` baseline passed without skips on iOS 27.0; the expanded sixteen-suite command is still being validated.
Additional operation-level assertions are being validated before T092 closes; see
`specs/001-household-recipe-manager/validation/native-parity.md`. The complete local `--backend-web` release mode now passes, including all browser acceptance. Native acceptance and the hosted release workflow have not yet passed end to end. The
release workflow requires a trusted macOS runner with Docker and the `recipe-manager-release`
label. Do not execute untrusted pull-request code on that runner. Standard macOS CI runs native
unit/handoff tests explicitly; only the isolated acceptance fixture proves native UI journeys.

## Remaining release gates

- [x] Continuous Angular import→organise→cook/leftovers→shop→snapshot refresh→reconcile journey passes with exact totals and preserved edits.
- [x] Complete staged critical user journey across Angular and iOS, including explicit snapshot
  refresh and shopping reconciliation without losing manual edits or purchased state.
- [x] Largest system text-size fixture passes setup/invitation/member profile, title growth and 44-point calendar navigation checks.
- [ ] Complete native accessibility audits (11 simulator findings; 12 findings on the latest physical audit), physical VoiceOver, keyboard/focus and
  minimum/current supported iOS verification; retain zero blocking issues.
- [x] Real signed-device app and Share Extension installation; physical planner, shopping and recipe editing pass.
- [x] Owner verified Safari URL capture, editable review and saving. ChatGPT currently shares a conversation URL.
- [ ] Physical plain-text handoff and VoiceOver review; do not treat a conversation URL as recipe text.
- [ ] Four client-pairing performance distributions, 100 samples, reconnection and foreground
  recovery against the specified 500-recipe workload and 2-vCPU/4-GiB reference deployment. The isolated Linux reference deployment is prepared and its operational checks pass; full performance acceptance remains open.
- [ ] Run required Linux/macOS/release workflows; retain real app/extension signing evidence.
- [x] Verify browser/.NET notices in the new arm64/AMD64 images and Swift notices in the signed app resource. Full SkiaSharp native LICENSE/codec notice bundles are also copied and checksum-verified in both image architectures; see dependency-review.md. AMD64 container verification is emulated locally.
- [ ] Review all native codec/transitive notices and keep both platform distributions current.
- [ ] Owner selects project license and contribution terms, private vulnerability-report route,
  release identity and signing/distribution identifiers. See docs/publication-readiness.md.
- [ ] Recheck current dependency advisories and source/image/asset provenance before publication.

## Security and recovery boundaries

Logout is all-session logout. Password/security-stamp changes, resets, member removal and account
deactivation revoke sessions and active household broadcast eligibility. Revocation and notification
dispatch are serialized at the documented authorization boundary; hub connection and invocation
checks never replace backend permissions. Reconnect with expired/revoked credentials fails.

Personal cache validation combines household content revision with the requesting user's preference
revision. Foreground recovery revalidates stale data; version 1 does not promise offline editing or
background delivery. Both clients use authoritative backend calculations and concurrency checks.

The Share Extension captures URL/text into a credential-free App Group inbox. The user explicitly
opens the app and confirms server/account/household before authenticated import. Review precedes
saving. A durable owner-scoped handoff receipt survives draft cleanup and lost success responses;
repeated commits return the original result for the full seven-day lifetime. Ordinary retry-key
expiry must never shorten that guarantee. Expired uncommitted handoffs fail safely; committed
receipts retain original-result/tombstone identity as specified in the data model.

See the contracts and validation/security.md for exact revocation, synchronization, revision and
handoff semantics. See docs/backup-restore.md for restoring database, images, secrets and protected
keys together. Never publish private fixture artifacts, cookies, credentials or certificate keys.

The iOS 18.0 ARM-only and universal runtime download attempts both exit 70 (unavailable in the
current Xcode catalogue). Minimum OS validation needs a compatible runner/device; iOS 27 results
do not substitute for it. No deployment target was silently raised.
