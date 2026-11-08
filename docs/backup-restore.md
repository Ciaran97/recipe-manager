# Paused-write backup and restore

Back up the database, private images, Data Protection key ring, its PFX and password, mounted secrets
and non-secret installation configuration together. With the bundled TLS proxy, also back up its
`/data` and `/config` volumes so household devices keep trusting the same local CA. Database data
alone cannot recover encrypted provider settings, protected sessions or image files.

Keep backups in access-controlled encrypted storage with a retention policy appropriate for the
household. A backup includes password hashes, private recipes, API credentials encrypted by server
keys, and the keys needed to recover them. Do not attach it to an issue or publish it with source.
The Data Protection PFX and its password belong to the server backup, not device certificate setup.

## Capture a consistent backup

The following commands use the production TLS overlay. Omit proxy-specific commands/volumes when
using a separately managed TLS proxy. Run from repository root; choose an absolute private backup
path on storage with enough free space. Do not overwrite a prior backup.

```sh
umask 077
backup_root=/absolute/private/path/recipe-manager-backup
mkdir -m 700 "$backup_root"
compose() { docker compose --env-file deploy/.env -f deploy/compose.yaml -f deploy/compose.tls.yaml "$@"; }
compose stop proxy app
compose exec -T db pg_dump -U recipe_manager -d recipe_manager -Fc > "$backup_root/database.dump"
app_container=$(compose ps -a -q app)
proxy_container=$(compose ps -a -q proxy)
docker cp "$app_container:/var/lib/recipe-manager/images" "$backup_root/images"
docker cp "$app_container:/var/lib/recipe-manager/keys" "$backup_root/keys"
docker cp "$proxy_container:/data" "$backup_root/tls-data"
docker cp "$proxy_container:/config" "$backup_root/tls-config"
cp -R deploy/secrets/production "$backup_root/secrets"
cp deploy/.env "$backup_root/install.env"
docker image inspect recipe-manager:local --format '{{.Id}}' > "$backup_root/application-image-id"
compose up -d --no-build --wait
```

Stopping the app/proxy pauses all writers before the database dump and file copies. PostgreSQL stays
running for `pg_dump`; do not stop it midway through a dump. A failed command means the backup is
incomplete: retain diagnostics privately and keep the application paused until you know which steps
completed. Never continue an upgrade assuming a partial backup is usable. The commands do not delete
or recreate any source volume.

Retain the exact application image/source and pinned PostgreSQL version alongside the backup. If
moving to a host without the image, export/import it with Docker image save/load or use the same
trusted versioned registry image. Record migration history, date and image identifier. Keep the
setup/maintenance secrets, database password, PFX and PFX password together under protected access;
do not independently regenerate them on an existing database.

## Restore in isolation first

Create a separate Compose project/network/configuration directory and clean target volumes. Keep
the original stack/volumes and backup unchanged. Use the same database username/name and PostgreSQL
version, matching application image and original protected secrets. The initializer can create an
unused target project; replace only that newly generated target's secrets with the backup before
starting it. Update the target `.env` with its own network/proxy addresses and correct secret path,
rather than blindly reusing source network identities. Keep the target inaccessible to normal users
while validating it.

Start only the fresh target database, then restore:

```sh
# Use a function pointing at the target's .env and project, never the source installation.
target_compose up -d --no-build --wait db
target_compose exec -T db pg_restore -U recipe_manager -d recipe_manager --exit-on-error < "$backup_root/database.dump"
target_compose create app proxy
```

Copy backed-up images and keys into the target named volumes before starting the app. Set their
owner to application UID/GID 1654 and root directories to 0700; retain protected file modes. Docker
copy into a container normally creates root-owned files, so a direct copy without correcting
ownership can make keys unreadable. Restore the TLS `/data` and `/config` contents as the proxy's
root-owned files. Use a short-lived, network-disabled helper mounting only the backup read-only and
the explicitly identified target volumes. Do not mount or change source volumes, host-wide paths
or an unverified volume name.

The complete executable example is [test-deployment-restore.sh](../scripts/test-deployment-restore.sh).
It deliberately refuses ordinary installations and operates only on owned `rm-validation-*` fixture
projects. Its commands have been exercised against fresh target volumes. Adapt its explicitly named
volume restore steps for a real recovery after verifying project/volume identities; do not weaken
the validation-only guard to point the test script at production.

After files/ownership are correct, run the target migrator and start the app/proxy:

```sh
target_compose up -d --no-build --wait
```

Verify normally trusted HTTPS, readiness, existing account login, pre-restart session decoding,
representative recipe quantities, original cooking snapshots, actual/leftover balances, manual and
purchased shopping state, image byte retrieval and encrypted provider settings. Test a shared update
through the TLS proxy. Check household membership/settings match the recovery point. The automated
fixture also retains committed handoff receipts and expired identities in its PostgreSQL-level
restore coverage. Do not accept counts alone as proof of usable restored keys/files.

## Upgrade and rollback

Take and validate a paused-write backup before changing the application/schema. Stop old app/proxy
writes, build/pull the new image, run the one-shot migrator, then start the new app and validate health
and representative workflows. A failed migration must leave the app stopped. Repeating a successful
migrator is safe; running it implicitly in every web process is not the deployment model.

For rollback, stop writes and restore the pre-upgrade database/files/keys into a separate target with
the previous application image. Validate it before switching the household origin. Do not blindly
apply down migrations or run an older binary against a newer schema. Organiser down migration loses
organiser associations; snapshot/import receipt recovery is documented in the migration notes.
Keep the failed target and original backup until recovery is verified. No command here requires
`docker compose down -v` or deletion of original data.
