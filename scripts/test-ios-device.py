#!/usr/bin/env python3
"""Run selected native journeys on an explicitly designated phone and owned LAN test stack."""
import argparse
import json
import os
from pathlib import Path
import plistlib
import re
import secrets
import signal
import ssl
import subprocess
import tempfile
import urllib.request

ROOT = Path(__file__).resolve().parents[1]



def run_owned(command, *, env, stdout, stderr, check=False):
    """Cancel only this invocation's process group, including xcodebuild children."""
    process = subprocess.Popen(command, env=env, stdout=stdout, stderr=stderr,
                               start_new_session=True)
    previous = signal.getsignal(signal.SIGTERM)

    def interrupted(_signum, _frame):
        raise KeyboardInterrupt

    signal.signal(signal.SIGTERM, interrupted)
    try:
        status = process.wait()
    except BaseException:
        if process.poll() is None:
            try:
                os.killpg(process.pid, signal.SIGINT)
            except ProcessLookupError:
                pass
            try:
                process.wait(timeout=15)
            except subprocess.TimeoutExpired:
                try:
                    os.killpg(process.pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
                process.wait()
        raise
    finally:
        signal.signal(signal.SIGTERM, previous)
    if check and status:
        raise subprocess.CalledProcessError(status, command)
    return subprocess.CompletedProcess(command, status)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--device", required=True)
    parser.add_argument("--server-directory", type=Path, required=True)
    parser.add_argument("--derived-data", type=Path, required=True)
    parser.add_argument("tests", nargs="+")
    args = parser.parse_args()
    metadata = json.loads((args.server_directory / "info.json").read_text())
    if not re.fullmatch(r"rm-device-[0-9a-f]{8}", metadata["project"]):
        parser.error("Use only an owned disposable LAN test deployment.")
    selectors = []
    for selector in args.tests:
        if not re.fullmatch(r"[A-Za-z][A-Za-z0-9_]*(/test[A-Za-z0-9_]*)?", selector):
            parser.error("Select a class or Class/testMethod.")
        feature = selector.split("/")[0]
        if not (ROOT / "ios/RecipeManagerUITests" / (feature + ".swift")).exists():
            parser.error("Select an existing native UI test class.")
        if feature in {"NativePerformanceUITests", "RecipeImageUITests", "CriticalJourneyUITests", "CrossClientUITests", "ImportReviewUITests", "ImportRetryUITests", "ShareHandoffUITests", "ShareImportUITests"}:
            parser.error("This runner does not provide synthetic Photos, AI, ShareHost or a remote cross-client controller. Use a suitable dedicated fixture.")
        selectors.append("-only-testing:RecipeManagerUITests/" + selector)
    context = ssl.create_default_context(cafile=str(args.server_directory / "root-ca.pem"))
    with urllib.request.urlopen(metadata["origin"] + "/api/v1/setup", context=context, timeout=10) as response:
        setup_required = json.load(response)["setupRequired"]
    os.umask(0o077)
    credentials_file = args.server_directory / "device-credentials.json"
    if not credentials_file.exists():
        if not setup_required:
            parser.error("This installation is already configured and has no owned test credentials. Do not reset it.")
        credentials_file.write_text(json.dumps({"adminPassword": secrets.token_urlsafe(32), "memberPassword": secrets.token_urlsafe(32)}))
    credentials = json.loads(credentials_file.read_text())
    if not any("/AccessPermissionUITests" in selector for selector in selectors):
        selectors.insert(0, "-only-testing:RecipeManagerUITests/AccessPermissionUITests")
    if setup_required and any("/AccessPermissionUITests" not in selector for selector in selectors):
        selectors.insert(0, "-only-testing:RecipeManagerUITests/AuthenticationUITests")
    elif any("/AuthenticationUITests" in selector for selector in selectors):
        parser.error("Initial setup already completed. Select subsequent workflows without resetting data.")
    scratch = Path(tempfile.mkdtemp(prefix="recipe-manager-physical-"))
    environment = dict(os.environ, DEVELOPER_DIR=os.environ.get("DEVELOPER_DIR", "/Applications/Xcode.app/Contents/Developer"))
    command = ["xcodebuild", "-project", str(ROOT / "ios/RecipeManager.xcodeproj"), "-scheme", "RecipeManager",
               "-destination", "platform=iOS,id=" + args.device, "-derivedDataPath", str(args.derived_data),
               "-clonedSourcePackagesDirPath", "/private/tmp/recipe-manager-xcode-packages",
               "-onlyUsePackageVersionsFromResolvedFile", "-allowProvisioningUpdates", "-allowProvisioningDeviceRegistration"]
    print("Private physical test artifacts: " + str(scratch), flush=True)
    with (scratch / "build.log").open("w") as log:
        run_owned(command + ["build-for-testing"], env=environment, check=True, stdout=log, stderr=log)
    products = args.derived_data / "Build/Products"
    source = next(products.glob("RecipeManager_iphoneos*.xctestrun"))
    data = plistlib.loads(source.read_bytes())
    if "TestConfigurations" in data:
        targets = [target for configuration in data["TestConfigurations"] for target in configuration["TestTargets"]]
    else:
        targets = [value for key, value in data.items() if key == "RecipeManagerUITests"]
    for target in targets:
        if target.get("BlueprintName") != "RecipeManagerUITests":
            continue
        target.setdefault("EnvironmentVariables", {}).update({
            "RECIPE_MANAGER_RUN_E2E": "1", "RECIPE_MANAGER_PHYSICAL_E2E": "1", "RECIPE_MANAGER_E2E_ORIGIN": metadata["origin"],
            "RECIPE_MANAGER_E2E_SETUP_TOKEN": (args.server_directory / "secrets/production/setup-token").read_text().strip(),
            "RECIPE_MANAGER_E2E_ADMIN_PASSWORD": credentials["adminPassword"],
            "RECIPE_MANAGER_E2E_MEMBER_PASSWORD": credentials["memberPassword"]})
    run = products / "PhysicalHousehold.xctestrun"
    run.write_bytes(plistlib.dumps(data))
    run.chmod(0o600)
    with (scratch / "test.log").open("w") as log:
        result = run_owned(["xcodebuild", "test-without-building", "-xctestrun", str(run),
                                 "-destination", "platform=iOS,id=" + args.device, "-parallel-testing-enabled", "NO",
                                 "-resultBundlePath", str(scratch / "Physical.xcresult"), *selectors],
                                env=environment, stdout=log, stderr=log)
    if result.returncode:
        raise SystemExit("Physical journey failed. Review the retained private artifacts; no household data was reset.")
    print("Selected physical journeys passed. Review separate accessibility, sharing and performance gates.")


if __name__ == "__main__":
    main()
