# API contract generation

The generated OpenAPI 3.1 document describes implemented endpoint metadata. It is intentionally
smaller than the full REST design inventory while implementation is in progress. Do not add fake
endpoints to inflate coverage. Extend the endpoint metadata and regenerate after every endpoint task.

Install locked dependencies with `dotnet restore RecipeManager.slnx --locked-mode`,
`npm ci --prefix src/RecipeManager.Web` and `npm ci --prefix scripts/contracts`. With the pinned
Xcode selected through DEVELOPER_DIR, run `scripts/check-contracts.sh --write`. Review the OpenAPI,
generated TypeScript types and all generated Swift files together. `--check` regenerates into a
scratch directory and fails on any difference. `--typescript` checks OpenAPI/TypeScript on Linux;
macOS CI checks both languages and builds the native package/app. Generators do not contain
business calculations. Angular HttpClient and native networking adapters supply session transport.

The explicit rebuild is required: the .NET document target otherwise caches a previous output path
and can skip generation into a new scratch directory. Generating needs no database or AI key.
Pin tooling in its separate lockfile rather than bypass its TypeScript peer requirements.

Sources: [ASP.NET Core OpenAPI generation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/aspnetcore-openapi?view=aspnetcore-10.0),
[Swift OpenAPI Generator](https://github.com/apple/swift-openapi-generator).
