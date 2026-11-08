#!/usr/bin/env bash
set -euo pipefail
umask 077
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
secrets="$root/deploy/secrets"
mkdir -p "$secrets/keys"
chmod 700 "$secrets" "$secrets/keys"
if [[ ! -s "$secrets/postgres-password" ]]; then openssl rand -base64 48 > "$secrets/postgres-password"; fi
if [[ ! -s "$secrets/maintenance-secret" ]]; then openssl rand -base64 48 > "$secrets/maintenance-secret"; fi
if [[ ! -s "$secrets/setup-token" ]]; then openssl rand -base64 48 > "$secrets/setup-token"; fi
if [[ ! -s "$secrets/data-protection.pfx" ]]; then
    openssl rand -base64 48 > "$secrets/data-protection-password"
    scratch="$(mktemp -d "${TMPDIR:-/tmp}/recipe-manager-key.XXXXXXXX")"
    trap 'rm -rf "$scratch"' EXIT
    openssl req -x509 -newkey rsa:3072 -nodes -days 3650 -subj '/CN=RecipeManager development key protection' \
        -keyout "$scratch/private.pem" -out "$scratch/certificate.pem" >/dev/null 2>&1
    openssl pkcs12 -export -inkey "$scratch/private.pem" -in "$scratch/certificate.pem" \
        -out "$secrets/data-protection.pfx" -passout "file:$secrets/data-protection-password"
fi
printf '%s\n' 'Development secret files are ready. Existing database passwords and key certificates were preserved.'
