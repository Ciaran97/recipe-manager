# Native toolchain

- Xcode 27.2 (27B52), Swift 6 language mode with strict concurrency.
- Deployment target: iOS 18.0; iOS Simulator SDK 27.2 verified locally.
- API generator 1.14.3, runtime 1.13.3, URLSession transport 1.4.0, exact pins in Package.swift.
- App and Share Extension build together using the shared RecipeManager scheme.
- ShareHandoff scheme isolates extension-safe tests; feature tests are added incrementally.

Use `DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer` when Command Line Tools is the
system default. No global xcode-select change is required. CI must use this pinned Xcode or an
explicitly reviewed compatible update. Simulator build does not prove device signing or UI tests.
App Group and signing configuration is local in `Local.xcconfig`; never commit provisioning secrets.

```sh
DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer xcodebuild -project ios/RecipeManager.xcodeproj -scheme RecipeManager -destination 'generic/platform=iOS Simulator' CODE_SIGNING_ALLOWED=NO build
```
