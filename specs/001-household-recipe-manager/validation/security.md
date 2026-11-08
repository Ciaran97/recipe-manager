# Security regression evidence

The full backend run passes 336 tests without skips: 129 unit, five architecture and 202 integration.
The new FullIsolationTests exercise two authenticated households across recipes, ingredients,
collections, tags, categories, meal slots/plans, cooking balances, shopping lists, images, private
import drafts and durable handoffs. Foreign conditional validators never yield another household's
304. Foreign references fail, source data stays intact, personal import state remains private even
from another same-household member, CSRF is required and Members retain shared editing but cannot
invite users. A real authenticated SignalR connection is revoked while removal races a shared
update; post-removal sentinel broadcasts, reconnection and conditional sync access are denied.

The existing clock/gate-controlled suites cover every revocation cause, a send already in progress,
queued sends, stalled/noncooperative transports, expiry, password reset/change and stamp rotation.
Native HTTP contracts verify authorization before conditional cache evaluation. SSRF/redirect/DNS
pinning, extraction bounds, provider-error sanitization, private image re-encoding/access and import
receipt ownership/concurrency tests are part of the same complete suite.

Review found and reproduced a native startup race: an outstanding credential read could publish the
old origin/user after a newer server change, sign-out or sign-in. Three tests first failed with ten
expectation failures. ServerSelection now tags asynchronous transitions, invalidates old views,
removes the superseded saved credential and validates the transition after each await. Obsolete
restoration cannot publish a client/user or replace newer errors/loading state. Root navigation waits
for a usable client before displaying authentication. All 91 native/handoff tests pass, including
these three cases. The subsequent signed authentication/import-retry fixture also passes.

Source and restored production fixture logs were checked against their mounted secrets, synthetic
administrator password and saved session cookies. None appeared. The checker prints no secret
values and refuses ordinary installations. Provider-specific tests additionally assert sanitized
errors; Caddy access logging is not enabled, avoiding query-token logging. Private fixture artifacts
still contain synthetic credentials and must not be published.

This is implementation evidence, not a claim that physical-device accessibility, the reference-host
workload, future dependency advisories or publication review have passed. Those remain separate
release gates.
