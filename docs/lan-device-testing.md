# Isolated LAN testing on a physical iPhone

Use this for disposable acceptance data, separately from an existing household installation. It does
not satisfy the separate 2-vCPU/4-GiB reference-host performance gate.

Configure the approved development team/bundle identifiers/App Group in ignored `ios/Local.xcconfig`.
Automatic signing must have a valid local Apple Development private key. Build with Xcode automatic
provisioning, verify both app/extension entitlements and confirm the designated device is covered.
The project supports the same App Group on both targets and the nonshipping ShareHost fixture.

Prepare a fresh stack using a locally validated image and the Mac's private LAN IPv4 address:

```sh
python3 scripts/prepare-lan-test-server.py --hostname <Mac-LAN-IP> --address <Mac-LAN-IP> --image <validated-local-image>
```

A `.local` hostname is also supported when the phone can resolve it. Use the IP form when multicast
DNS is unavailable. The HTTPS certificate must match the selected origin; do not bypass TLS checks.
The script creates a new `rm-device-*` Compose project with fresh volumes/network/secrets and retains
private configuration under its printed temporary directory. PostgreSQL has no public port. The
app port binds loopback; the TLS proxy and certificate-only bootstrap bind the supplied LAN address.
The application and API are never served through that HTTP bootstrap. The script tests these limits.
The proxy excludes API responses from compression so opaque ETags remain valid for conditional
reads and writes. Do not enable blanket response encoding: Caddy adds an encoding suffix to ETags,
which the backend correctly rejects as stale. Web assets retain compression.

On the designated phone, download the printed `.mobileconfig` profile, review its uniquely named
root certificate and install it in Settings → General → VPN & Device Management. Then enable its full
trust in Settings → General → About → Certificate Trust Settings. The profile contains only the public
root certificate and is removable. Compare the printed SHA-256 fingerprint if checking its identity.
The CA private key stays in the stack's private TLS volume and is never served. The Mac's global trust
store is not changed. See Apple's [profile installation](https://support.apple.com/en-ie/102400) and
[certificate trust instructions](https://support.apple.com/en-ie/102390); Caddy's
[PKI configuration](https://caddyserver.com/docs/caddyfile/options#pki) defines the unique CA name.

Run selected journeys after the owner confirms phone trust and the device is connected/unlocked:

```sh
python3 scripts/test-ios-device.py --device <designated-device-udid> --server-directory <printed-private-directory> --derived-data <private-device-DerivedData> RecipeEditingUITests PlannerUITests ShoppingUITests
```

The first run includes setup/invitations and creates synthetic accounts with generated private
passwords. Subsequent runs preserve the installation and credentials, select additional workflows
without redoing setup, and never reset household data. Existing fixed-name story tests need fresh
preconditions; inspect a failed journey before retrying it. Physical image tests must not select
unrelated personal photos: the generic runner excludes the simulator's synthetic-Photos fixture,
AI/ShareHost cases and loopback-only cross-client controllers.

App and test-runner Local Network permission is handled explicitly in the owned UI fixture. The
prerequisite brings its own test runner to the foreground before its first network request, because
a background runner cannot present the initial permission prompt. If permission was previously
denied, enable the corresponding app in Settings → Privacy & Security → Local Network. Keep the
phone unlocked while Xcode initializes and runs UI automation. Cancelling the command forwards an
interrupt to its owned process group so automation does not continue after cancellation. Real
requests still use normal authenticated HTTPS, backend validation and household permissions. The
backend contains no test reset endpoint. Private result bundles can include synthetic credentials
and must not be published. Record operation-level passes separately from manual VoiceOver,
Safari/ChatGPT compatibility, minimum OS and measured performance.

Use the generated private `stop.sh` to stop the LAN stack after testing. It preserves volumes. Remove
the test certificate profile from the phone when no longer needed; retain the private stack data
only for the intended test period. Do not delete original deployment volumes as a cleanup shortcut.
