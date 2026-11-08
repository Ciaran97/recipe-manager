# Native iOS installation and server access

Recipe Manager requires both its Angular web client and native SwiftUI client for version 1. The
backend, database, authentication and household records are shared. Device delivery is a separate
macOS/Xcode build and signing step; Docker Compose does not produce an iOS application.

## Toolchain and signing

The project targets iOS 18.0 or newer, uses Swift 6 strict concurrency and has been built with
Xcode 27.2. Install Xcode and its iOS runtime, complete Xcode's first-launch setup, then open
`ios/RecipeManager.xcodeproj`. Select the RecipeManager scheme.

Copy `ios/Signing.xcconfig.example` to `ios/Local.xcconfig` and replace the placeholders with identifiers
registered to your Apple development team. The project includes this ignored override automatically.
Register the application identifier, its `.RecipeManagerShareExtension` identifier and the App Group.
The app and extension must both have that same group in their signed entitlements and provisioning
profiles. App Group storage carries bounded recipe captures and their destination metadata; it does
not share Keychain credentials, household sessions or provider API keys. The ShareHost test target is
not part of the distributed product.

Select your connected iPhone/iPad as the destination and build/run the application and embedded
extension. Use Xcode's signing diagnostics to resolve profile or App Group registration errors.
Trust the development installation as required by the device's management settings. Record the team,
profile, device and OS used for release evidence without publishing private signing material.
Device provisioning and distribution depend on the owner's Apple account; simulator ad-hoc signing
does not demonstrate that these device steps have passed.

## Reach the self-hosted HTTPS server

Give the server a stable HTTPS origin reachable from the phone over the household LAN, a VPN, or a configured [Cloudflare Tunnel](cloudflare-tunnel.md). Use the same canonical URL as the web client.
Use a certificate valid for the exact DNS name and a certificate chain the device trusts. For a
private certificate authority, install its public root through the device's supported certificate
management and enable trust where required. Keep the CA private key on the operator's protected
host. `localhost` on a physical iPhone refers to that phone, not the Mac or server.

Open the origin in Safari first to confirm name resolution, routing, certificate trust and the web
login page. Then enter that same HTTPS origin in the app's server form. A URL path, query, embedded
credential or insecure HTTP origin is rejected. Do not disable certificate validation, add arbitrary
transport exceptions or expose PostgreSQL to make a device connect. A trusted reverse proxy should
forward the API and real-time paths to the server; the production proxy/deployment acceptance gate
is tracked separately.

Local-network access can require the device's Local Network permission. Explain this access as a
connection to the user's chosen household server, and test both permission denial and later grant
on a real device. The app does not discover or scan household devices. See Apple's
[local network privacy guidance](https://developer.apple.com/documentation/technotes/tn3179-understanding-local-network-privacy).
Permission, DNS, routing and certificate failures must be resolved separately; repeatedly signing in
cannot fix an unreachable or untrusted origin.

## Accounts and shared content

Complete the installation setup wizard once, then invite the second member from Settings. Each
person creates their own account. Public registration stays disabled. A member can edit shared
recipes, ingredients, planners and shopping lists; only an administrator manages membership and
household settings. The app stores its own session credentials in Keychain scoped to its origin.
An explicit server/account change discards unsaved input and clears the old saved sign-in. All-session
logout and password/account revocation invalidate backend access and live updates.

To import from another app, share a recipe URL or text to Recipe Manager and save the capture. Open
Recipe Manager, sign in, open Shared recipe captures and explicitly confirm the server, household
and account. Choose extraction, review/correct the complete recipe and save it. The extension does
not call an AI provider or extract a website. AI text processing is optional server configuration,
disclosed before the explicit import action. Captures remain on the device while signed out or
interrupted, expire after seven days and can be explicitly discarded. A committed handoff resolves
the original recipe rather than creating another recipe on retry.

Updates refresh while the app is active and on return to the foreground. Version 1 requires network
access and has no offline-first editing or background-sync guarantee. Shopping list edits and
purchased state are shared with Angular through the authoritative backend.

## Acceptance still required on physical devices

Run the critical recipe/import/meal/leftover/shopping workflows on the minimum and current supported
OS. Verify real Safari URL sharing, ChatGPT text sharing, cancellation, signed-out handoff persistence,
source review, account confirmation, Dynamic Type, VoiceOver, local-network denial/recovery and
foreground stale-data recovery. Test both client directions against the same household. Record the
actual device/OS and results in US7/US8/US9 validation; simulator fixture success does not close these
gates. No physical-device or public-distribution success is claimed by this guide.
