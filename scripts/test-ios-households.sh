#!/usr/bin/env bash
set -euo pipefail
umask 077
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
export DEVELOPER_DIR="${DEVELOPER_DIR:-/Applications/Xcode.app/Contents/Developer}"
scratch="$(mktemp -d "${TMPDIR:-/tmp}/recipe-manager-native-e2e.XXXXXXXX")"
derived="${RECIPE_MANAGER_E2E_DERIVED_DATA:-$scratch/DerivedData}"
fixture_pid=''
web_pid=''
simulator=''
cross_pid=''
cross=0
cross_spec='cross-client.spec.ts'
share_mode=0
drop_commit=0
photo_mode=0
tests=(-only-testing:RecipeManagerUITests/AuthenticationUITests)
text_size="${RECIPE_MANAGER_E2E_TEXT_SIZE:-large}"
case "$text_size" in
    extra-small|small|medium|large|extra-large|extra-extra-large|extra-extra-extra-large|accessibility-medium|accessibility-large|accessibility-extra-large|accessibility-extra-extra-large|accessibility-extra-extra-extra-large) ;;
    *) echo 'Select a supported simulator content size.' >&2; exit 2 ;;
esac
for selector in "$@"; do
    if [[ ! "$selector" =~ ^[A-Za-z][A-Za-z0-9_]*(/test[A-Za-z0-9_]*)?$ ]]; then
        echo 'Select an existing native UI test class or Class/testMethod.' >&2; exit 2
    fi
    feature="${selector%%/*}"
    if [[ "$feature" == NativePerformanceUITests ]]; then echo 'Use tests/performance/household-workload.js --native for the reference-host controller.' >&2; exit 2; fi
    if [[ ! -f "$root/ios/RecipeManagerUITests/$feature.swift" ]]; then
        echo 'Select an existing native UI test class.' >&2; exit 2
    fi
    if [[ "$feature" == CriticalJourneyUITests && "$selector" != "$feature" ]]; then
        echo 'The continuous controller requires both critical journey methods; select the whole class.' >&2; exit 2
    fi
    tests+=("-only-testing:RecipeManagerUITests/$selector")
    if [[ "$feature" == ImportRetryUITests ]]; then drop_commit=1; fi
    if [[ "$feature" == RecipeImageUITests ]]; then photo_mode=1; fi
    if [[ "$feature" == CrossClientUITests ]]; then
        if [[ "$cross" == 1 && "$cross_spec" != cross-client.spec.ts ]]; then echo 'Run the two cross-client fixture classes separately.' >&2; exit 2; fi
        cross=1
    fi
    if [[ "$feature" == CriticalJourneyUITests ]]; then
        if [[ "$cross" == 1 && "$cross_spec" != cross-client-critical.spec.ts ]]; then echo 'Run the two cross-client fixture classes separately.' >&2; exit 2; fi
        cross=1; cross_spec='cross-client-critical.spec.ts'
    fi
    if [[ "$feature" == ShareHandoffUITests || "$feature" == ShareImportUITests || "$feature" == ImportRetryUITests || "$feature" == CriticalJourneyUITests ]]; then share_mode=1; fi
done
cleanup() {
    status=$?
    trap - EXIT
    if [[ -n "$cross_pid" ]]; then kill "$cross_pid" 2>/dev/null || true; wait "$cross_pid" 2>/dev/null || true; fi
    if [[ -n "$web_pid" ]]; then kill "$web_pid" 2>/dev/null || true; fi
    if [[ -n "$fixture_pid" ]]; then kill "$fixture_pid" 2>/dev/null || true; fi
    if [[ -n "$simulator" ]]; then
        xcrun simctl shutdown "$simulator" >/dev/null 2>&1 || true
        xcrun simctl delete "$simulator" >/dev/null 2>&1 || true
    fi
    if [[ "$status" != 0 || "${RECIPE_MANAGER_KEEP_E2E_ARTIFACTS:-}" == 1 ]]; then
        printf 'Private native E2E artifacts: %s\n' "$scratch"
    else rm -rf "$scratch"; fi
    exit "$status"
}
trap cleanup EXIT
# Refuse existing listeners: this fixture never reuses a developer or production server.
node - "$cross" <<'NODE'
const net = require('node:net');
(async () => { for (const port of (process.argv[2] === '1' ? [4201, 7285, 7291] : [4201, 7285])) await new Promise((resolve, reject) => {
  const server = net.createServer(); server.once('error', reject); server.listen(port, '127.0.0.1', () => server.close(resolve));
}); })().catch(() => { process.stderr.write('E2E ports are in use; stop the prior fixture first.\n'); process.exitCode = 1; });
NODE
# Fail before creating credentials or waiting for HTTPS if PostgreSQL cannot be started.
docker info >/dev/null
dotnet run --project tests/RecipeManager.E2EHost --no-launch-profile -- tests/e2e/.runtime 7285 > "$scratch/api.log" 2>&1 &
fixture_pid=$!
# Read CA bytes afresh on each probe: a prior fixture may have left a retired certificate.
node - "$root/tests/e2e/.runtime/localhost.pem" <<'NODE'
const https = require('node:https'), fs = require('node:fs');
const file = process.argv[2], deadline = Date.now() + 120000;
function probe() {
  if (!fs.existsSync(file)) return retry();
  const request = https.get('https://localhost:7285/api/v1/setup', { ca: fs.readFileSync(file) }, response => {
    response.resume(); if (response.statusCode !== 200) retry();
  }); request.on('error', retry);
}
function retry() { if (Date.now() > deadline) { process.stderr.write('Isolated API HTTPS fixture did not become ready.\n'); process.exitCode = 1; } else setTimeout(probe, 500); }
probe();
NODE
export NODE_EXTRA_CA_CERTS="$root/tests/e2e/.runtime/localhost.pem"
export RECIPE_MANAGER_API_ORIGIN=https://localhost:7285
RECIPE_MANAGER_E2E_RUN=1 RECIPE_MANAGER_E2E_DROP_HANDOFF_COMMIT="$drop_commit" npm start --prefix src/RecipeManager.Web -- --port 4201 --ssl-cert ../../tests/e2e/.runtime/localhost.pem --ssl-key ../../tests/e2e/.runtime/localhost.key > "$scratch/web.log" 2>&1 &
web_pid=$!
node - <<'NODE'
const https = require('node:https');
const deadline = Date.now() + 120000;
function probe() {
  const request = https.get('https://localhost:4201/api/v1/setup', response => {
    response.resume(); if (response.statusCode === 200) return;
    retry();
  }); request.on('error', retry);
}
function retry() { if (Date.now() > deadline) { process.stderr.write('HTTPS E2E fixture did not become ready.\n'); process.exitCode = 1; } else setTimeout(probe, 500); }
probe();
NODE
runtime=$(xcrun simctl list runtimes --json | python3 -c 'import json,sys; r=next(r for r in json.load(sys.stdin)["runtimes"] if r.get("isAvailable") and r["name"].startswith("iOS "));print(r["identifier"])')
simulator=$(xcrun simctl create "Recipe Manager E2E" com.apple.CoreSimulator.SimDeviceType.iPhone-18-Pro "$runtime")
xcrun simctl boot "$simulator"
xcrun simctl bootstatus "$simulator" -b > "$scratch/simulator.log" 2>&1
# Bring only this owned simulator forward so keyboard events target its UI.
if [[ -d "$DEVELOPER_DIR/Applications/Simulator.app" ]]; then
    open -a "$DEVELOPER_DIR/Applications/Simulator.app" --args -CurrentDeviceUDID "$simulator"
fi
xcrun simctl ui "$simulator" content_size "$text_size"
if [[ "$photo_mode" == 1 ]]; then
    python3 scripts/create-native-photo-fixtures.py "$scratch/photos"
    xcrun simctl addmedia "$simulator" "$scratch/photos/recipe-red.png" "$scratch/photos/recipe-blue.png"
fi
export RECIPE_MANAGER_E2E_TEXT_SIZE="$text_size"
xcrun simctl keychain "$simulator" add-root-cert "$root/tests/e2e/.runtime/localhost.pem"
xcodebuild -project ios/RecipeManager.xcodeproj -scheme RecipeManager -destination "platform=iOS Simulator,id=$simulator" -derivedDataPath "$derived" CODE_SIGNING_ALLOWED=YES CODE_SIGN_IDENTITY=- build-for-testing > "$scratch/build.log" 2>&1
if [[ "$share_mode" == 1 ]]; then
    xcodebuild -project ios/RecipeManager.xcodeproj -scheme RecipeManagerShareHost -destination "platform=iOS Simulator,id=$simulator" -derivedDataPath "$derived" CODE_SIGNING_ALLOWED=YES CODE_SIGN_IDENTITY=- build > "$scratch/share-host-build.log" 2>&1
    xcrun simctl install "$simulator" "$derived/Build/Products/Debug-iphonesimulator/RecipeManagerShareHost.app"
fi
export RECIPE_MANAGER_CROSS_MODE="$cross"
if [[ "$cross" == 1 ]]; then
    export RECIPE_MANAGER_CROSS_TOKEN="$(node -e "process.stdout.write(require('node:crypto').randomBytes(32).toString('hex'))")"
    export RECIPE_MANAGER_CROSS_RUNTIME="$root/tests/e2e/.runtime"
    export RECIPE_MANAGER_CROSS_READY="$scratch/cross-ready"
    RECIPE_MANAGER_EXTERNAL_E2E=1 node tests/e2e/node_modules/@playwright/test/cli.js test "$cross_spec" --config tests/e2e/playwright.config.ts > "$scratch/cross-client.log" 2>&1 &
    cross_pid=$!
    node - "$RECIPE_MANAGER_CROSS_READY" "$cross_pid" <<'NODE'
const fs = require('node:fs');
const deadline = Date.now() + 30000;
function ready() {
  if (fs.existsSync(process.argv[2])) return;
  try { process.kill(Number(process.argv[3]), 0); } catch { process.stderr.write('Cross-client observer exited before readiness.\n'); process.exitCode = 1; return; }
  if (Date.now() > deadline) { process.stderr.write('Cross-client observer did not become ready.\n'); process.exitCode = 1; }
  else setTimeout(ready, 100);
}
ready();
NODE
fi
python3 - "$derived" "$root/tests/e2e/.runtime/setup-token" <<'PY'
import glob,os,plistlib,sys
run=glob.glob(sys.argv[1]+'/Build/Products/RecipeManager_*.xctestrun')[0]
data=plistlib.load(open(run,'rb'))
if 'TestConfigurations' in data: targets=[t for c in data['TestConfigurations'] for t in c['TestTargets']]
else: targets=[v for k,v in data.items() if isinstance(v,dict) and k=='RecipeManagerUITests']
for target in targets:
    if target.get('BlueprintName')!='RecipeManagerUITests': continue
    target.setdefault('EnvironmentVariables',{}).update({
      'RECIPE_MANAGER_RUN_E2E':'1','RECIPE_MANAGER_E2E_ORIGIN':'https://localhost:4201',
      'RECIPE_MANAGER_E2E_TEXT_SIZE':os.environ['RECIPE_MANAGER_E2E_TEXT_SIZE'],
      'RECIPE_MANAGER_E2E_SETUP_TOKEN':open(sys.argv[2]).read().strip(),
      'RECIPE_MANAGER_E2E_AI_ORIGIN':open(os.path.join(os.path.dirname(sys.argv[2]),'ai-origin')).read().strip(),
      'RECIPE_MANAGER_E2E_AI_TEXT':open(os.path.join(os.path.dirname(sys.argv[2]),'ai-recipe-text')).read()})
    appinfo=plistlib.load(open(sys.argv[1]+'/Build/Products/Debug-iphonesimulator/RecipeManager.app/Info.plist','rb'))
    target['EnvironmentVariables']['RECIPE_MANAGER_E2E_APP_BUNDLE_ID']=appinfo['CFBundleIdentifier']
    if os.environ.get('RECIPE_MANAGER_CROSS_MODE') == '1':
        target['EnvironmentVariables']['RECIPE_MANAGER_CROSS_TOKEN'] = os.environ['RECIPE_MANAGER_CROSS_TOKEN']
output=sys.argv[1]+'/Build/Products/HouseholdE2E.xctestrun'
with open(output,'wb') as f: plistlib.dump(data,f)
os.chmod(output,0o600)
PY
xcodebuild test-without-building -xctestrun "$derived/Build/Products/HouseholdE2E.xctestrun" -destination "platform=iOS Simulator,id=$simulator" "${tests[@]}" -parallel-testing-enabled NO -resultBundlePath "$scratch/Households.xcresult" CODE_SIGNING_ALLOWED=YES CODE_SIGN_IDENTITY=- > "$scratch/test.log" 2>&1
if [[ "$photo_mode" == 1 ]]; then python3 scripts/check-native-image-requests.py "$scratch/api.log"; fi
if [[ "$drop_commit" == 1 ]]; then rg --quiet 'fixture_commit_response_dropped' "$scratch/web.log" || { echo 'Expected a real committed response to be dropped.' >&2; exit 1; }; fi
if [[ "$cross" == 1 ]]; then wait "$cross_pid"; cross_pid=''; fi
xcrun xcresulttool get test-results summary --path "$scratch/Households.xcresult" > "$scratch/summary.json"
python3 - "$scratch/summary.json" "$#" <<'PY'
import json,sys
result=json.load(open(sys.argv[1]))
if result.get('passedTests',0)<1+int(sys.argv[2]) or result.get('failedTests',0) or result.get('skippedTests',0):
    raise SystemExit('Native household journey must pass without skips')
print('Native household journey passed without skips.')
PY
RECIPE_MANAGER_EXTERNAL_E2E=1 npm --prefix tests/e2e test -- cross-client-household.spec.ts
