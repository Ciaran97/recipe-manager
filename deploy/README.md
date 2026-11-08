# Recipe Manager deployment

`Dockerfile` builds Angular and ASP.NET Core into one non-root application image. Production
`compose.yaml` runs private PostgreSQL, an explicit one-shot migrator and the API/static host.
`compose.tls.yaml` optionally adds household HTTPS using a pinned Caddy image and private local CA.

Start with the [home-server README](../README.md#start-here), [Cloudflare Tunnel](../docs/cloudflare-tunnel.md), [self-hosting](../docs/self-hosting.md), [backup/restore](../docs/backup-restore.md) and
[native iOS installation](../docs/ios-installation.md). The initializer creates protected secret
files and a project-owned network, refuses overwrites and prints no credentials.

The database-only developer service moved to `compose.dev.yaml`; its original project name,
15432 loopback binding and named volume are preserved. From repository root:

```sh
scripts/prepare-development.sh
docker compose -p recipe-manager-dev -f deploy/compose.dev.yaml config --quiet
docker compose -p recipe-manager-dev -f deploy/compose.dev.yaml up -d --wait db
```

Stopping Compose preserves its volumes. Do not use volume deletion as a troubleshooting or test
reset procedure for an existing installation. Acceptance uses separate `rm-validation-*` projects.
