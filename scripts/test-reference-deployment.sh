#!/usr/bin/env bash
# Run only on an isolated Linux reference host. Every installation/restore is a new owned project.
set -euo pipefail
umask 077
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
directory="${1:?Provide a new private validation directory}"
initial_image="${2:?Provide a locally available previous image}"
final_image="${3:?Provide a locally available current image}"
[[ "$(uname -s)" == Linux && ! -e /.dockerenv ]]
python3 - <<'PY'
import os
from pathlib import Path
memory = dict(line.split(':', 1) for line in Path('/proc/meminfo').read_text().splitlines())
assert len(os.sched_getaffinity(0)) == 2, 'Use a two-vCPU reference host.'
assert 3500 * 1024 <= int(memory['MemTotal'].split()[0]) <= 4096 * 1024, 'Use a four-GiB reference host.'
print('Verified Linux reference allocation: two CPUs and four GiB memory.')
PY
[[ ! -e "$directory/.env" && ! -e "$directory/secrets" ]]
project="rm-validation-$(openssl rand -hex 4)"
export RECIPE_MANAGER_HTTP_PORT=18080 RECIPE_MANAGER_TLS_HTTP_PORT=18081 RECIPE_MANAGER_TLS_HTTPS_PORT=18443 RECIPE_MANAGER_TLS_BIND_ADDRESS=127.0.0.1
export RECIPE_MANAGER_IMAGE="$initial_image"
python3 "$root/scripts/configure-self-hosting.py" https://localhost:18443 --directory "$directory" --project "$project"
compose() { docker compose --env-file "$directory/.env" -f "$root/deploy/compose.yaml" -f "$root/deploy/compose.tls.yaml" "$@"; }
compose up -d --no-build --wait --wait-timeout 180
compose cp proxy:/data/caddy/pki/authorities/local/root.crt "$directory/root.crt"
node "$root/scripts/test-deployment.mjs" "$directory"
NODE_EXTRA_CA_CERTS="$directory/root.crt" node "$root/scripts/test-deployment.mjs" "$directory" --verify --exercise-routing --upload-image --signalr
compose restart app proxy
compose up -d --no-build --wait --wait-timeout 120
NODE_EXTRA_CA_CERTS="$directory/root.crt" node "$root/scripts/test-deployment.mjs" "$directory" --verify --restore-session --signalr
# Upgrade the application while retaining its existing database, files, keys and sessions.
compose stop app proxy
export RECIPE_MANAGER_IMAGE="$final_image"
compose up -d --no-build --wait --wait-timeout 180
NODE_EXTRA_CA_CERTS="$directory/root.crt" node "$root/scripts/test-deployment.mjs" "$directory" --verify --restore-session --exercise-routing --signalr
# Restore into another new project. The guarded helper preserves and stops both sets of volumes.
bash "$root/scripts/test-deployment-restore.sh" "$directory"
printf 'Reference-host deployment, restart, image upgrade and paused-write restore passed. Private evidence: %s\n' "$directory"
