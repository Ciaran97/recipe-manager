#!/usr/bin/env bash
set -euo pipefail
umask 077
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
"$root/scripts/prepare-development.sh"
if [[ ! -s "$root/deploy/secrets/localhost.pem" || ! -s "$root/deploy/secrets/localhost.key" ]]; then
    dotnet dev-certs https --export-path "$root/deploy/secrets/localhost.pem" --format Pem --no-password
    chmod 600 "$root/deploy/secrets/localhost.pem" "$root/deploy/secrets/localhost.key"
fi
export NODE_EXTRA_CA_CERTS="$root/deploy/secrets/localhost.pem"
npm start --prefix "$root/src/RecipeManager.Web"
