# Development dependency review

The pinned Angular 22.2.4 runtime and Angular build 22.2.5/Vitest 4.1.12 resolve the high/critical
advisories reported by the older CLI scaffold. The current lint-tool dependency tree reports
18 moderate transitive findings from minimatch (GHSA-g3hv-75wg-m249). This dependency is used by
the local/CI lint toolchain, not shipped in application browser assets. The installed upstream
lint dependencies still require older minimatch APIs. Do not force a global major-version override
or downgrade TypeScript tooling without compatibility validation. Recheck during release dependency
review; keep audit output visible and reject new high/critical findings in CI.

Source: https://github.com/advisories/GHSA-g3hv-75wg-m249

Run `npm audit --prefix src/RecipeManager.Web` for current findings; dependency audits are not a
substitute for the planned API, tenant isolation, import and revocation security tests.

Contract generation runs in scripts/contracts with its own lockfile: openapi-typescript 7.13.0
requires TypeScript 5, while Angular requires TypeScript 6. This tool package is not a browser runtime
dependency. Its audit currently reports three moderate transitive findings; audit it separately with
`npm audit --prefix scripts/contracts` and review the precise advisories before publication.

The initial .NET 10.0.12 baseline was replaced with Microsoft package/runtime 10.0.14 and
SDK 10.0.403 after the SignalR test dependency triggered NU1903 for
[GHSA-334f-vvp4-7g6p](https://github.com/advisories/GHSA-334f-vvp4-7g6p). Do not suppress
this audit or run the new SignalR backend on the older shared runtime. CI installs the global.json
SDK; local implementation uses a hash-verified isolated SDK if the system installation is older.

Image sanitisation uses SkiaSharp 4.154.0 with matching Linux.NoDependencies native assets, pinned
centrally and in lock files. The official [NuGet package](https://www.nuget.org/packages/SkiaSharp/4.154.0)
and [MIT license](https://github.com/mono/SkiaSharp/blob/main/LICENSE.md) were reviewed on 2026-11-02.
The matching [Linux runtime package](https://www.nuget.org/packages/SkiaSharp.NativeAssets.Linux.NoDependencies/4.154.0)
avoids a fontconfig dependency for server image decoding. Preserve upstream and bundled native
third-party notices when distributing. This selection does not choose the application's publication
license. Native binary loading and image rejection/metadata removal require real integration tests.

## Advisory recheck — 2026-11-04

Locked-package checks complete without updates: NuGet vulnerability listing covers all seven
solution projects with no vulnerability entries. The browser-test package (including axe 4.14.0)
reports zero advisories. The web toolchain retains 18 moderate entries and contract tools retain
three moderate entries; both audit commands pass the existing HIGH threshold. No HIGH or CRITICAL
finding is reported. This is advisory evidence, not completion of license/native-codec notice
review or a guarantee about later package/feed changes.

## Native image notice preservation — 2026-11-04

The exact restored `SkiaSharp.NativeAssets.Linux.NoDependencies` 4.154.0 package includes a
139,775-byte `THIRD-PARTY-NOTICES.txt`. Its notices include libpng, libjpeg-turbo, libwebp and zlib,
as well as other upstream components. Preserve the complete supplied bundle rather than guessing
which notice sections can be removed for a particular native binary. This conservative inclusion
does not assert that every listed component is linked into every platform artifact.

The deployment Dockerfile now copies both SkiaSharp package LICENSE files and that complete bundle
from the locked restore into `/app/third-party/`. Exact versioned copy paths intentionally fail a
build if a package upgrade removes those files; update and review these paths alongside centrally
pinned package versions. The restored native notice file's SHA-256 is
`21504c46c4c58aa64c1055bd2dcbc5f9a136b4b8c412ed3cc6740e22c5b127f5`;
the wrapper LICENSE SHA-256 is
`89101e35a8c66fd4d6dffc1763259161d35cb564c169714ec227a768c89f2938`.
These are packaging checks, not a project-license compatibility determination. Retain the owner
license choice and publication review gate.

Separate local ARM64 and AMD64 images (`recipe-manager:codec-notice-validation` and
`recipe-manager:codec-notice-validation-amd64`) both build successfully. Read-only, network-disabled
containers verify nonempty wrapper/native LICENSE files and the exact checksums above inside both
images. This validates packaging only; it does not rerun the deployment drill or claim publication
approval. The AMD64 container check used Docker emulation on the ARM64 workstation; it is not reference-host evidence. Existing deployment images and persistent volumes were not replaced.

The 2026-11-04 final advisory recheck retains 18 moderate web-tooling entries, three moderate contract-tool entries and zero browser-test entries; all three commands pass the configured HIGH threshold. No forced major-version override or downgrade was applied.
