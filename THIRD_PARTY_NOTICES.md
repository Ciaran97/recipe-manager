# Third-party dependencies and notices

Recipe Manager's own license is pending owner selection. Dependency licenses do not select or grant
a project license. Review exact locked versions and preserve upstream notices before distribution.

| Component | Pinned source of truth | Upstream license/notice source |
| --- | --- | --- |
| ASP.NET Core, Identity, EF Core, .NET libraries | Directory.Packages.props and NuGet lockfiles | Package license metadata; .NET runtime LICENSE.txt and ThirdPartyNotices.txt retained in the base image |
| Npgsql / PostgreSQL EF provider | NuGet lockfiles | PostgreSQL license in package metadata/upstream repositories |
| AngleSharp 1.9.1 | Directory.Packages.props | MIT, AngleSharp/AngleSharp commit recorded in the NuGet package |
| SkiaSharp 4.154.0 and native assets | Directory.Packages.props / NuGet lockfiles | Wrapper MIT license and complete native package LICENSE/THIRD-PARTY-NOTICES preserved under /app/third-party/; publication compatibility review remains required |
| Angular, SignalR, RxJS, tslib and bundled browser dependencies | src/RecipeManager.Web/package-lock.json | Generated 3rdpartylicenses.txt from the production Angular build |
| Swift OpenAPI runtime 1.13.3 / URLSession 1.4.0 | Native Package.resolved | LICENSE.txt and NOTICE.txt from the exact upstream checkouts |
| Swift HTTP Types 1.8.0 / Collections 1.7.5 | Native Package.resolved | Exact checkout LICENSE.txt, including any Swift runtime exception text |
| PostgreSQL 18 and Caddy container images | deploy/compose*.yaml pinned digests | Upstream image/package licenses and contained notices; retain them in the images |
| Development/test tools | npm/NuGet/Swift tool lockfiles | Their own package metadata; distinguish tools from shipped runtime assets |

The browser notice file must accompany the generated browser assets in the deployment image. Native
runtime license/NOTICE files are bundled in `RecipeManager/Resources/ThirdPartyNotices.txt`; verify the built application resource before distribution. Keep generated notices current
when dependency pins change, including transitive/native components. Do not substitute this inventory
for full upstream license text or assert compatibility before the owner review.

See docs/dependency-review.md for audit commands and known tool-only moderate advisories. New
high/critical findings must be resolved rather than suppressed. The publication record tracks the
remaining license/notice work and owner decisions.
