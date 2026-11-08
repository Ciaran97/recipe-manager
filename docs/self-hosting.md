# Self-host Recipe Manager

For a first installation, start with the [home-server walkthrough](../README.md#start-here). Remote access through a named tunnel is covered by the [Cloudflare Tunnel guide](cloudflare-tunnel.md).

The production stack runs PostgreSQL, a one-shot migrator and one non-root ASP.NET Core application
serving Angular, REST and SignalR. The optional Caddy overlay supplies household HTTPS. SwiftUI uses
that same HTTPS origin; build/sign it separately as described in [iOS installation](ios-installation.md).

The default runtime does not need an AI key or paid service. Images, database rows and Data Protection
keys persist in separate named volumes. The database has no published host port. The app's HTTP port
is bound to host loopback for a local TLS proxy; do not publish it directly to household clients.

## First installation

Install Docker with Compose v2, Python 3 and OpenSSL. Arrange a stable household DNS name pointing to
the host. From the repository root:

```sh
python3 scripts/configure-self-hosting.py https://recipes.home.arpa
docker compose --env-file deploy/.env -f deploy/compose.yaml -f deploy/compose.tls.yaml config --quiet
docker compose --env-file deploy/.env -f deploy/compose.yaml -f deploy/compose.tls.yaml up -d --build --wait
docker compose --env-file deploy/.env -f deploy/compose.yaml -f deploy/compose.tls.yaml ps -a
```

The initializer creates an unused project-owned network, a private `deploy/.env` and fresh secrets in
`deploy/secrets/production`. It refuses existing configuration or an active project network and never
rotates a working installation's secrets. The secret directory is 0700; individual secret files are
0444 so the non-root UID can read their read-only file mounts. The private host parent prevents other
host users from traversing into them. Keep these paths out of source control. Run backups before any
manual changes to secrets; replacing the database password or protection certificate independently
will break the existing installation.

Expect a healthy database, a migrator exited with code zero, then a healthy app and running proxy.
`dotnet RecipeManager.Api.dll migrate` is a separate executable mode; the web host does not migrate
at startup. A failed migrator blocks app startup. `GET /health/live` measures process availability;
`GET /health/ready` additionally checks database access and applied migrations, with no AI/provider
request and no private failure details. Docker's image health probe uses readiness.

The image is built from pinned .NET 10.0.403 SDK, ASP.NET 10.0.14, Node 26.10.0 and PostgreSQL 18
multi-architecture digests. The API runs as UID/GID 1654 with a read-only root filesystem, dropped
capabilities and writable private image/key volumes. Its maintenance socket is in private tmpfs and
is recreated on container restart. The Caddy image has its own pinned multi-architecture digest.

## Trust household HTTPS

The included Caddy configuration uses its private local certificate authority. Export only its
public root, then install/trust that root on household browsers/devices:

```sh
docker compose --env-file deploy/.env -f deploy/compose.yaml -f deploy/compose.tls.yaml \
  exec -T proxy cat /data/caddy/pki/authorities/local/root.crt > recipe-manager-local-root.crt
```

Verify the root through a trusted operator channel before installing it. Keep the private CA key
inside its protected TLS volume/backups. The Data Protection PFX is a separate server-side encryption
key; never install or distribute it as a device trust certificate. Access the configured DNS origin
in a browser and on iOS with ordinary TLS validation. Do not bypass certificate or hostname checks.
The app's own Local Network permission explanation covers connecting to the chosen household server;
physical permission/VoiceOver testing remains a release gate.

Open Settings/setup, use the operator token from the protected `setup-token` file, create the first
administrator and household, then invite the second member. Each person has separate credentials.
Public registration is disabled after setup. The setup token is not a password and need not be sent
to other household members. Do not paste it into logs, issues or screenshots.

## Use an existing TLS proxy instead

Preserve API ETags unchanged in both directions. The included Caddy configuration compresses web
assets and excludes `/api` and `/api/*` from encoding, because encoding suffixes on opaque API
validators cause correct updates to fail with HTTP 412. Apply the same exclusion when using a
custom proxy; do not strip suffixes in clients or relax backend concurrency checks.

Omit `compose.tls.yaml`. Proxy the configured HTTPS origin to host loopback port 8080 and preserve
Host plus `X-Forwarded-For`, `X-Forwarded-Proto` and `X-Forwarded-Host`. Permit WebSocket upgrades.
The initializer sets `RECIPE_MANAGER_TRUSTED_PROXY_IP` to the owned Docker network's host gateway.
If the proxy connects through a different address, explicitly set its actual IP in that variable.
The TLS overlay instead trusts only its reserved proxy container IP. The server accepts one hop and
only the configured public host. Public and loopback hostnames are allowed for service health/recovery.
Never enable unrestricted `ASPNETCORE_FORWARDEDHEADERS_ENABLED` or trust arbitrary proxy networks.
See Microsoft's [proxy configuration guidance](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0).

The overlay binds ports 80/443 by default; an existing proxy can occupy them while the base stack
uses loopback only. Optional `RECIPE_MANAGER_HTTP_PORT`, `RECIPE_MANAGER_TLS_HTTP_PORT`,
`RECIPE_MANAGER_TLS_HTTPS_PORT` and `RECIPE_MANAGER_TLS_BIND_ADDRESS` override host bindings.
The public origin must match the client's actual HTTPS host/port. Do not change the internal port
8080 independently of the packaged health probe and maintenance public listener.

## Configuration, operations and upgrades

Keep `COMPOSE_PROJECT_NAME`, network identity and the existing volume names stable. Use `.env` for
non-secret origins, paths and proxy addresses; database/setup/maintenance/PFX passwords are mounted
files. Household AI configuration is administered through the authenticated UI, encrypted with the
persisted key ring/certificate, optional and disabled by default. Import images are not fetched
implicitly. The optional compatible AI provider runs on the server; clients receive no API key.

Back up and restore using [the paused-write procedure](backup-restore.md). For an upgrade, retain the
old image and verified backup, review migration notes, stop proxy/app writes, build/pull the new
image, run only the migrator and start the app/proxy after successful completion. Never run two web
versions against a schema during migration. A schema rollback requires the matching pre-upgrade
backup/application, not a blind down migration. Check health, login, representative recipes, leftovers,
shopping edits, image retrieval and real-time changes after restart.

Structured application logs omit request bodies, EF parameters and private provider errors. Inspect
`logs migrator` or health/service state first; do not publish entire dumps, key volumes or diagnostic
screenshots. Secret/configuration failures return a safe error and require operator correction.
The host-local recovery CLI, including all-session reset/revocation, is documented in
[recovery](recovery.md). With this stack, run it through `docker compose ... exec app` using the same
manifest/environment; no recovery endpoint is exposed through TCP, even with a maintenance secret.

## Development database and validation

The former database-only configuration is retained as `deploy/compose.dev.yaml`, with its original
`recipe-manager-dev` project and volume names. It still binds PostgreSQL only to 127.0.0.1:15432.
Development uses `scripts/prepare-development.sh` and `docker compose -p recipe-manager-dev -f deploy/compose.dev.yaml`, not production secrets/volumes. The explicit project name also prevents a production `.env` from overriding the development project identity.
Do not delete existing development or production volumes to run a test.

Implementation validation used fresh `rm-validation-*` projects, loopback-only proxy ports, generated
secrets and normally verified TLS. The API/image/SignalR workflow, persistence and restore evidence is
recorded under `specs/001-household-recipe-manager/validation/US7.md`. A clean reference Linux host,
physical iOS installation/accessibility, and final cross-client workload gates are reported separately;
local container success alone does not imply those checks passed.
