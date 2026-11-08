#!/usr/bin/env bash
# Restore only owned disposable validation projects; preserve every source/target volume.
set -euo pipefail
umask 077
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
source_directory="${1:?Provide the owned validation directory}"
source_project=$(python3 - "$source_directory/.env" <<'PY'
import pathlib,re,sys
text=pathlib.Path(sys.argv[1]).read_text()
match=re.search(r"^COMPOSE_PROJECT_NAME='(rm-validation-[a-f0-9]{8})'$",text,re.M)
if not match: raise SystemExit('Refuse a non-validation installation')
print(match[1])
PY
)
[[ -f "$source_directory/verification.json" && -f "$source_directory/root.crt" && ! -e "$source_directory/backup" ]]
restore_directory=$(mktemp -d "${TMPDIR:-/tmp}/recipe-manager-restored.XXXXXXXX")
restore_project="rm-validation-$(openssl rand -hex 4)"
export RECIPE_MANAGER_IMAGE="${RECIPE_MANAGER_IMAGE:-recipe-manager:implementation}"
export RECIPE_MANAGER_HTTP_PORT=18080 RECIPE_MANAGER_TLS_HTTP_PORT=18081 RECIPE_MANAGER_TLS_HTTPS_PORT=18443 RECIPE_MANAGER_TLS_BIND_ADDRESS=127.0.0.1
source_compose() { docker compose --env-file "$source_directory/.env" -f "$root/deploy/compose.yaml" -f "$root/deploy/compose.tls.yaml" "$@"; }
restore_compose() { docker compose --env-file "$restore_directory/.env" -f "$root/deploy/compose.yaml" -f "$root/deploy/compose.tls.yaml" "$@"; }
finish() {
    status=$?
    trap - EXIT
    source_compose stop >/dev/null 2>&1 || true
    if [[ -f "$restore_directory/.env" ]]; then restore_compose stop >/dev/null 2>&1 || true; fi
    printf 'Private source and restore artifacts: %s %s\n' "$source_directory" "$restore_directory"
    printf 'Preserved source/restore volumes for %s and %s. No existing volumes were deleted.\n' "$source_project" "$restore_project"
    exit "$status"
}
trap finish EXIT
source_compose stop proxy app
mkdir -m 700 "$source_directory/backup"
source_compose exec -T db pg_dump -U recipe_manager -d recipe_manager -Fc > "$source_directory/backup/database.dump"
source_app=$(source_compose ps -a -q app)
source_proxy=$(source_compose ps -a -q proxy)
docker cp "$source_app:/var/lib/recipe-manager/images" "$source_directory/backup/images"
docker cp "$source_app:/var/lib/recipe-manager/keys" "$source_directory/backup/keys"
docker cp "$source_proxy:/data" "$source_directory/backup/tls-data"
docker cp "$source_proxy:/config" "$source_directory/backup/tls-config"
cp -R "$source_directory/secrets/production" "$source_directory/backup/secrets"
python3 "$root/scripts/configure-self-hosting.py" https://localhost:18443 --directory "$restore_directory" --project "$restore_project"
# Replace only this newly generated, unused fixture's secrets with the protected source backup.
cp -f "$source_directory/backup/secrets/"* "$restore_directory/secrets/production/"
chmod 444 "$restore_directory/secrets/production/"*
cp "$source_directory/verification.json" "$restore_directory/verification.json"
cp "$source_directory/root.crt" "$restore_directory/root.crt"
chmod 600 "$restore_directory/verification.json" "$restore_directory/root.crt"
restore_compose up -d --no-build --wait db
restore_compose exec -T db pg_restore -U recipe_manager -d recipe_manager --exit-on-error < "$source_directory/backup/database.dump"
restore_compose create app proxy
restore_app=$(restore_compose ps -a -q app)
restore_proxy=$(restore_compose ps -a -q proxy)
volume_at() { docker inspect --format "{{range .Mounts}}{{if eq .Destination \"$2\"}}{{.Name}}{{end}}{{end}}" "$1"; }
images_volume=$(volume_at "$restore_app" /var/lib/recipe-manager/images)
keys_volume=$(volume_at "$restore_app" /var/lib/recipe-manager/keys)
tls_data_volume=$(volume_at "$restore_proxy" /data)
tls_config_volume=$(volume_at "$restore_proxy" /config)
for volume in "$images_volume" "$keys_volume" "$tls_data_volume" "$tls_config_volume"; do [[ "$volume" == "$restore_project"_* ]]; done
# A short-lived helper restores only these named target volumes, with networking disabled.
docker run --rm --network none --user 0 --entrypoint /bin/sh \
    --mount "type=bind,source=$source_directory/backup,target=/backup,readonly" \
    --mount "type=volume,source=$images_volume,target=/images" \
    --mount "type=volume,source=$keys_volume,target=/keys" \
    --mount "type=volume,source=$tls_data_volume,target=/tls-data" \
    --mount "type=volume,source=$tls_config_volume,target=/tls-config" \
    "$RECIPE_MANAGER_IMAGE" -c 'cp -a /backup/images/. /images/ && cp -a /backup/keys/. /keys/ && cp -a /backup/tls-data/. /tls-data/ && cp -a /backup/tls-config/. /tls-config/ && chown -R 1654:1654 /images /keys && chmod 0700 /images /keys && chown -R 0:0 /tls-data /tls-config'
restore_compose up -d --no-build --wait --wait-timeout 120
NODE_EXTRA_CA_CERTS="$restore_directory/root.crt" node "$root/scripts/test-deployment.mjs" "$restore_directory" --verify --restore-session --signalr
printf '%s\n' "$restore_directory" > "$source_directory/restored-directory"
