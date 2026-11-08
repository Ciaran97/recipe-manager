#!/usr/bin/env bash
# Local/CI checks, never a publication or signing-distribution command.
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
mode="${1:---backend-web}"
verify_native_tools() {
    test "$(xcodebuild -version | head -1)" = 'Xcode 27.2'
    test "$(dotnet --version)" = "$(python3 -c 'import json; print(json.load(open("global.json"))["sdk"]["version"])')"
    test "$(node --version)" = "v$(tr -d '\n' < .nvmrc)"
}
run_native_parity() {
    # Each story owns its simulator, migrated PostgreSQL and HTTPS application.
    for journey in CatalogueUITests RecipeEditingUITests CoreWorkflowParityTests RecipeImageUITests RecipeLifecycleParityTests PlannerUITests ShoppingUITests NativeSyncUITests RecipeSearchUITests ImportReviewUITests ShareHandoffUITests ShareImportUITests ImportRetryUITests DynamicTypeUITests CrossClientUITests CriticalJourneyUITests; do
        printf 'Native parity journey: %s\n' "$journey"
        scripts/test-ios-households.sh "$journey"
    done
}
case "$mode" in
  --backend-web)
    node --test tests/performance/measurement.test.js tests/performance/native-control.test.js
    dotnet restore RecipeManager.slnx --locked-mode
    scripts/check-contracts.sh --typescript
    dotnet build RecipeManager.slnx --no-restore
    dotnet format RecipeManager.slnx --no-restore --verify-no-changes
    dotnet test RecipeManager.slnx --no-build --no-restore
    npm --prefix src/RecipeManager.Web run lint
    npm --prefix src/RecipeManager.Web run format:check
    npm --prefix src/RecipeManager.Web test -- --watch=false
    npm --prefix src/RecipeManager.Web run build
    npm --prefix src/RecipeManager.Web audit --audit-level=high
    npm --prefix tests/e2e audit --audit-level=high
    npm --prefix scripts/contracts audit --audit-level=high
    npm --prefix tests/e2e test
    ;;
  --native)
    verify_native_tools
    : "${RECIPE_MANAGER_SIMULATOR_ID:?Select an installed iPhone simulator explicitly.}"
    scripts/check-contracts.sh --check
    xcodebuild -resolvePackageDependencies -project ios/RecipeManager.xcodeproj -scheme RecipeManager -onlyUsePackageVersionsFromResolvedFile
    xcodebuild -project ios/RecipeManager.xcodeproj -scheme RecipeManager \
      -destination "platform=iOS Simulator,id=$RECIPE_MANAGER_SIMULATOR_ID" \
      CODE_SIGNING_ALLOWED=YES CODE_SIGN_IDENTITY=- test \
      -only-testing:RecipeManagerTests -only-testing:ShareHandoffTests
    ;;
  --native-parity)
    verify_native_tools
    docker info >/dev/null
    run_native_parity
    ;;
  --native-acceptance)
    verify_native_tools
    docker info >/dev/null
    run_native_parity
    RECIPE_MANAGER_E2E_TEXT_SIZE=accessibility-extra-extra-extra-large scripts/test-ios-households.sh DynamicTypeUITests
    RECIPE_MANAGER_E2E_TEXT_SIZE=accessibility-extra-extra-extra-large scripts/test-ios-households.sh CriticalJourneyUITests
    scripts/test-ios-households.sh NativeAccessibilityUITests
    ;;
  *) echo 'Usage: validate-release.sh [--backend-web|--native|--native-parity|--native-acceptance]' >&2; exit 2 ;;
esac
printf 'Selected checks passed. Review docs/release-checklist.md for separate release gates.\n'
