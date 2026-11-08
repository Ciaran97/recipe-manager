#!/usr/bin/env bash
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
mode="${1:---check}"
case "$mode" in --check|--write|--typescript) ;; *) echo 'Usage: check-contracts.sh [--check|--write|--typescript]' >&2; exit 2 ;; esac
scratch="$(mktemp -d "${TMPDIR:-/tmp}/recipe-manager-contracts.XXXXXXXX")"
trap 'rm -rf "$scratch"' EXIT
export DOTNET_CLI_TELEMETRY_OPTOUT=1
mkdir -p "$scratch/openapi" "$scratch/typescript" "$scratch/swift"
dotnet build src/RecipeManager.Api --no-restore -t:Rebuild --disable-build-servers -p:OpenApiGenerateDocuments=true -p:OpenApiDocumentsDirectory="$scratch/openapi"
node scripts/contracts/node_modules/openapi-typescript/bin/cli.js "$scratch/openapi/openapi.json" -o "$scratch/typescript/schema.ts"
node src/RecipeManager.Web/node_modules/prettier/bin/prettier.cjs --write "$scratch/typescript/schema.ts" --config src/RecipeManager.Web/.prettierrc
if [[ "$mode" != --typescript ]]; then
    swift run --package-path ios/Packages/RecipeManagerAPI swift-openapi-generator generate "$scratch/openapi/openapi.json" --mode types --mode client --access-modifier public --output-directory "$scratch/swift"
fi
if [[ "$mode" == --write ]]; then
    cp "$scratch/openapi/openapi.json" specs/001-household-recipe-manager/contracts/openapi.json
    cp "$scratch/typescript/schema.ts" src/RecipeManager.Web/src/app/core/api/generated/schema.ts
    cp "$scratch/swift/"*.swift ios/Packages/RecipeManagerAPI/Sources/Generated/
else
    diff -u specs/001-household-recipe-manager/contracts/openapi.json "$scratch/openapi/openapi.json"
    diff -u src/RecipeManager.Web/src/app/core/api/generated/schema.ts "$scratch/typescript/schema.ts"
    if [[ "$mode" != --typescript ]]; then
        diff -ru ios/Packages/RecipeManagerAPI/Sources/Generated "$scratch/swift"
    fi
fi
