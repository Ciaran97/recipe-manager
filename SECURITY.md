# Security policy — publication preparation

The application is unreleased. Supported-version and response-time commitments will be set when the
owner publishes a release policy. The private vulnerability reporting address/channel is pending
owner approval; no address or GitHub repository URL is invented here. Obtain that private route
before sending exploit details, household exports or credentials. A public issue should contain only
non-sensitive coordination information.

Relevant security boundaries include authenticated household isolation, Administrator/Member roles,
invitation ownership/expiry, CSRF, current session/security-stamp validation, SignalR revocation,
private images/import drafts, durable handoff ownership and bounded external recipe fetching.
Server/account selection and Keychain restoration must not mix clients or identities. Provider keys
stay on the backend, encrypted with the persistent key ring and operator certificate. AI import is
optional and requires explicit user action after processing disclosure.

Use synthetic accounts/data to reproduce a report. Describe affected versions, preconditions,
expected/actual behavior and minimal steps. Redact passwords, cookie/CSRF/invitation/setup tokens,
provider keys, private certificate keys, recipe text and personal identifiers. Do not publish private
fixture logs, screenshots, backups or Xcode result bundles; their synthetic secrets still grant
access to the corresponding fixture while it exists.

For suspected compromise, preserve relevant protected evidence, isolate access through the operator's
trusted host and use the common [recovery/revocation CLI](docs/recovery.md). Password reset, account
deactivation, membership removal and stamp rotation revoke active sessions/connections. Follow the
[backup/restore guide](docs/backup-restore.md); never delete original volumes as an investigation step.
Dependency advisories and upgrade notes must be reviewed against pinned lockfiles before distribution.

Implementation evidence is recorded in
[security validation](specs/001-household-recipe-manager/validation/security.md). Passing that suite
is not a vulnerability-free guarantee or a substitute for the pending release/publication review.
