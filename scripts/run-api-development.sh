#!/usr/bin/env bash
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
scripts/prepare-development.sh
export DOTNET_ENVIRONMENT=Development
export ASPNETCORE_ENVIRONMENT=Development
export ConnectionStrings__Database="Host=localhost;Port=${RECIPE_MANAGER_DB_PORT:-15432};Database=recipe_manager;Username=recipe_manager"
export Images__Directory="$root/deploy/data/images"
export Setup__TokenFile="$root/deploy/secrets/setup-token"
export Database__PasswordFile="$root/deploy/secrets/postgres-password"
export DataProtection__KeysDirectory="$root/deploy/secrets/keys"
export DataProtection__CertificateFile="$root/deploy/secrets/data-protection.pfx"
export DataProtection__CertificatePasswordFile="$root/deploy/secrets/data-protection-password"
if [[ -n "${RECIPE_MANAGER_RUNTIME_DIR:-}" ]]; then
    export Maintenance__SocketPath="$RECIPE_MANAGER_RUNTIME_DIR/maintenance.sock"
    export Maintenance__SecretFile="$root/deploy/secrets/maintenance-secret"
    export Server__PublicAddress="${Server__PublicAddress:-https://localhost:7243}"
fi
case "${1:-run}" in
    run) dotnet run --project src/RecipeManager.Api --no-launch-profile --urls https://localhost:7243 ;;
    maintenance) shift; dotnet run --project src/RecipeManager.Api --no-launch-profile -- maintenance "$@" ;;
    migrate) dotnet ef database update --project src/RecipeManager.Infrastructure --startup-project src/RecipeManager.Api ;;
    *) echo 'Usage: run-api-development.sh [run|migrate|maintenance command email]' >&2; exit 2 ;;
esac
