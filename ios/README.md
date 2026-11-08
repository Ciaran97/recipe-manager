# Recipe Manager for iOS

The SwiftUI application and embedded Share Extension are first-class version 1 clients of the same
ASP.NET Core backend as Angular. The checked-in Xcode project targets iOS 18.0 or newer and Swift 6.
The current implementation was built and tested with Xcode 27.2 on macOS; Linux/Compose builds the
server and web client, and does not build or sign the iOS targets. Hosted CI separately pins
macOS 26 and Xcode 26.6; see [toolchain details](TOOLCHAIN.md).

Open `RecipeManager.xcodeproj` in Xcode. For a device, copy `Signing.xcconfig.example` to the ignored
`Local.xcconfig`, set your own team/bundle/App Group identifiers and provision the application and
extension with the same App Group. Never commit signing credentials or private profiles. See
[installation and HTTPS access](../docs/ios-installation.md) for the complete steps and release gates.

From the repository root, the simulator unit command is:

```sh
export DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer
xcodebuild -project ios/RecipeManager.xcodeproj -scheme RecipeManager \
  -destination 'platform=iOS Simulator,id=YOUR_SIMULATOR_UUID' \
  CODE_SIGNING_ALLOWED=YES CODE_SIGN_IDENTITY=- test \
  -only-testing:RecipeManagerTests -only-testing:ShareHandoffTests
```

Choose a simulator UUID from `xcrun simctl list devices available`; use a disposable device for
acceptance. The signed fixture creates its own simulator, HTTPS certificate and PostgreSQL database:

```sh
scripts/test-ios-households.sh ImportReviewUITests ShareImportUITests
scripts/test-ios-households.sh RecipeSearchUITests
```

These commands require the pinned .NET SDK, Docker, Node/npm, restored dependencies, Playwright
Chromium and Xcode. A custom Chromium installation requires `PLAYWRIGHT_BROWSERS_PATH`. The fixture
refuses occupied ports rather than resetting a running installation. Failure artifacts are private
and their path is printed; inspect before sharing because screenshots can contain fixture input.
The ShareHost scheme is a nonshipping test host for the real system share sheet.

Run `scripts/check-contracts.sh --check` with the same .NET/Swift toolchains to detect generated API
contract drift. Change shared business behavior in the backend; Swift owns presentation, state and
networking rather than ingredient calculations, leftover accounting or shopping consolidation.
