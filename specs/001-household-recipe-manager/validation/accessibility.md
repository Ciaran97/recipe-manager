# Accessibility acceptance evidence

T129 remains open. Automated results below do not establish full screen-reader, physical-device or
minimum-OS acceptance. No native audit type or finding is suppressed to manufacture a green suite.

## Angular — verified 2026-11-04

`npm --prefix tests/e2e test -- accessibility.spec.ts` passes with a fresh household and real HTTPS
API/PostgreSQL fixture. Nine screens—library, recipe detail, manual editor, import entry, organisers,
ingredient catalogue, populated planner, populated shopping list and settings—are checked at 320px
and 1280px. All 18 combinations have zero axe 4.14.0 WCAG 2 A/AA, 2.1 AA and 2.2 AA violations and
no horizontal overflow. Keyboard navigation focuses Skip to content, enters main and opens the
manual recipe editor. The latest standalone matrix also passes all ten browser acceptance tests
across its final validation batches.

A long unbroken recipe title first reproduced 320px overflow. Text wrapping, shrinkable form grid
columns and minimum child widths fix it without hiding content. Recipe and shopping screenshots were
visually inspected. Full keyboard journeys, expanded controls and manual screen-reader review remain.

## SwiftUI — verified subsets

Signed simulator checks use the actual application, shared HTTPS backend and isolated PostgreSQL.
Xcode 27.2 / Apple Swift 6.4.2 / iOS 27.0 are the available local tools/runtime.

- Normal system content size `large`: native setup/invitation/profile and DynamicTypeUITests pass.
  The measured recipe-title height is 20.333 points.
- Actual system size `accessibility-extra-extra-extra-large`: the entire owned fixture passes,
  including administrator setup, invitation sharing, member registration/profile editing, recipe
  title growth and calendar navigation. The title measures 125.333 points. Both arrows remain at
  least 44 × 44 points and operable. The final Angular profile observer also passes; no skips.
- The system share sheet groups actions under View More at the largest size. The invitation test
  now opens that observed group before Copy when necessary. The application still uses ShareLink.
- The native catalogue regression passes after background catalogue refresh is deferred while an
  ingredient editor is open; the original editing state/ETag is preserved and refresh occurs after
  dismissal. The input helper waits for readiness and scrolls input centres above the keyboard.
- Both native-first and web-first continuous critical journeys pass on the normal-size simulator.
  This includes real Share Extension capture, import review, planning, leftovers, shopping, explicit
  snapshot refresh and cross-client reconciliation; see acceptance-report.md.

Verified largest-size command:

```sh
RECIPE_MANAGER_E2E_TEXT_SIZE=accessibility-extra-extra-extra-large scripts/test-ios-households.sh DynamicTypeUITests
```

## Unfiltered native audit — still failing

`NativeAccessibilityUITests` audits Recipes, Meal Planner, Shopping Lists and Settings. The original
46 findings decreased to 23, then 20, and the latest completed run reports 11. Calendar arrows now
have explicit labels/44-point hit regions, recipe titles use natural multiline height, and text uses
adaptive system label colors. Library descriptions retain their intentional two-line preview.

The latest remaining findings cover Dynamic Type metadata (filter/toolbar/password/profile text),
clipping predictions (search/profile/password fields), and contrast on read-only household text. Actual measured title growth does not prove every other warning false.
Retain these findings for targeted inspection and correction. The audit remains red.

## Minimum OS and remaining gates

Only iOS 27.0 is installed. Apple's documented `xcodebuild -downloadPlatform iOS -buildVersion 18.0`
was attempted with both `arm64` and `universal`; both exit 70 because that runtime is unavailable in
the current catalogue. No deployment target was raised and no unavailable test was claimed as passed.
A compatible iOS 18.0 test runner/device is still required.

Physical VoiceOver, minimum/current OS coverage, native date/form interaction, the entire critical
journey at large text and complete web keyboard/screen-reader review remain open. Controlled host
share tests do not replace physical Safari/ChatGPT acceptance. Keep T093/T101/T129 gates explicit.

Apple references: [audits](https://developer.apple.com/documentation/accessibility/performing-accessibility-audits-for-your-app),
[foreground styles](https://developer.apple.com/documentation/swiftui/view/foregroundstyle(_:)),
[additional simulator runtimes](https://developer.apple.com/documentation/xcode/downloading-and-installing-additional-xcode-components).

The latest run also includes scaled minimum heights on profile/password inputs, wrapping filter labels and a natively labelled meal-slot toolbar icon. It still records every audit finding without filtering or waiver; the remaining eleven findings keep acceptance open.

## Physical audit — 2026-11-05

The current signed app was audited on the designated iPhone/iOS 27.0.1 after the owner unlocked it. The unfiltered audit records **12 findings**: five contrast, three partially unsupported Dynamic Type and four clipping findings across Recipes and Settings. Findings include the search field, filter/password controls and household labels near the lower screen edge. No issue was filtered or waived. This physical result is separate from the eleven-finding simulator baseline and keeps T093/T129 open. The current recipe-editing physical journey passes separately.
