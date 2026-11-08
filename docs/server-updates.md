# Pre-release images and server updates

Publishing a GitHub **pre-release** runs the backend/web and iOS checks at its tagged commit,
then builds the application image and publishes it to `ghcr.io/ciaran97/recipe-manager`.
Normal pushes, pull requests, saved drafts and stable releases do not publish images.
Use a Docker-compatible release tag such as `v0.1.0-beta.1` (letters, numbers, dots,
underscores and hyphens, at most 128 characters). Publish a new version instead of moving
an existing release tag. There is no mutable `latest` deployment channel.

The workflow uses `release: published` with a pre-release condition. This includes a
pre-release published from a draft; GitHub's `prereleased` activity alone misses that case.
See [GitHub release events](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#release).

## Publish an image

1. Merge the implementation into `main` after review.
2. Create a GitHub release targeting the intended commit. Select **Set as a pre-release**
   and publish it. Release notes should call out database migrations and recovery considerations.
3. Wait for **Publish pre-release container** to pass. Its summary records the image tag and digest.
4. On first publication, open the repository's package and set its visibility to **Public**
   if anonymous pulls are wanted. A public repository does not automatically make a new package public.
   The workflow links the package to its source repository and uses its scoped `GITHUB_TOKEN`;
   no publishing PAT is needed. Private packages require a server login with a classic PAT with
   `read:packages` (use `docker login ghcr.io --username YOUR_LOGIN --password-stdin`).

Images currently target **Linux amd64**, matching the Proxmox Ubuntu Docker VM.
A failed backend/web or native check prevents publication. This automation does not replace
manual gates in the release checklist or claim that a pre-release is production-qualified.
See [GitHub's publishing documentation](https://docs.github.com/en/actions/tutorials/publish-packages/publish-docker-images).

## Update the external-proxy Docker server

This helper supports the base Compose deployment with separately managed Caddy/cloudflared,
plus an optional `deploy/compose.home.yaml`. It does not manage the bundled TLS overlay,
custom additional manifests, PostgreSQL major upgrades, Caddy, or cloudflared. Use the
[manual operations guide](self-hosting.md#configuration-operations-and-upgrades) for those cases.

Bootstrap the helper once, after it has been merged:

```bash
cd /opt/recipe-manager/recipe-manager
git pull --ff-only
```

Keep the local `deploy/compose.home.yaml` containing the LAN binding; it is ignored by Git.
Then choose an image from a successful pre-release workflow:

```bash
./scripts/update-server.sh v0.1.0-beta.1
```

The helper requires Python 3, Docker Compose and write access to the installation configuration.
It downloads the image before downtime, resolves it to an immutable digest, and:

1. Stops the app so household writes are paused, leaving PostgreSQL running.
2. Creates a private backup of the database, images, encryption keys, all Compose file-backed
   secrets, existing configuration, LAN override and exact previous application image.
3. Checks that PostgreSQL can read the dump's archive directory. This is an integrity check,
   **not a completed restore drill**; regularly follow the isolated [restore procedure](backup-restore.md).
4. Runs the one-shot migrator with the downloaded image.
5. Saves `RECIPE_MANAGER_IMAGE` with its digest in `deploy/.env`, then starts the same image
   without building and waits for the app's readiness health check.

Backups default to `~/.local/share/recipe-manager/backups/`. Choose separate backup storage with:

```bash
./scripts/update-server.sh v0.1.0-beta.1 --backup-directory /mnt/private-backups/recipe-manager
```

Ensure that destination is private and has enough space for the database, images and an exported
application image. Keep protected off-server copies; backups on the same VM alone do not protect
against host loss. The helper does not delete old backups, images, secrets or volumes.

After success, check `https://recipe.swanepoel.dev/health/ready`, login, recipe edits, images and
shopping updates between two devices. Further updates use the same command with the next tag;
no local build, reconfiguration of the tunnel, or rerun of the initializer is needed.
Pull repository changes separately when updated Compose manifests or the helper itself are needed;
the application version comes from the explicit image tag, not the server checkout's HEAD.

## Failure and recovery

A pull failure leaves the running app untouched. A backup, migration or readiness failure leaves
the app stopped and retains diagnostics and any backup files. Only a backup with `backup-complete`
has finished all backup commands. Do not automatically start the old app after a migration failure:
schema changes may have occurred. Consult the migration logs and the restore guide.

Each backup includes `install.env`, the resolved old `compose.json`, optional `compose.home.yaml`,
`application-image-id`, `application-image.tar`, database dump, images, keys and `secrets/`.
Secret backup filenames use Compose secret names; `compose.json` records their original source paths.
Use those mappings when restoring. Load the previous image with `docker image load` from the tar
and restore the matching pre-upgrade database/files/configuration into an isolated target first.
The backup may contain custom paths and all secrets: keep it private.

Updates are serialized with `deploy/.update-lock`. If the process is forcibly terminated, first
confirm no updater or migration is still running before removing this empty lock directory.
Never use `docker compose down -v` or rerun the setup initializer to recover an existing installation.

## Validate updater changes

Run the failure-boundary tests without Docker:

```bash
python3 scripts/test-update-server.py
```

For an opt-in integration check with an already-built host-compatible application image:

```bash
python3 scripts/test-update-server-docker.py recipe-manager:implementation
```

This creates a unique `rm-validation-*` installation and loopback-only local registry,
exercises a real image update, checks retained database/image data, and restores the dump
into a separate test database. It never reads the working installation's `deploy/.env`.
The registry is removed afterward; stopped test volumes and private backup artifacts are retained.
This tests the deployment mechanics, not a live GHCR publication or the external tunnel.
