# Automated verification

Use the pinned SDK, Node and Xcode versions in README.md. Restore locked dependencies before checks:

```sh
dotnet restore RecipeManager.slnx --locked-mode
npm ci --prefix src/RecipeManager.Web
npm ci --prefix tests/e2e
npm ci --prefix scripts/contracts
dotnet test RecipeManager.slnx
npm test --prefix src/RecipeManager.Web -- --watch=false
npm run lint --prefix src/RecipeManager.Web
npm run build --prefix src/RecipeManager.Web
scripts/check-contracts.sh --check
```

Backend integration tests require Docker and create disposable PostgreSQL containers; they never
use the development database. The maintenance test launches a separate real API process and tests
its private Unix socket and operator CLI. Revocation tests include a real protected SignalR fixture;
production-hub coverage is extended when those endpoints land.

## Browser acceptance

```sh
npm --prefix tests/e2e exec playwright install chromium
npm --prefix tests/e2e test
# Or select one isolated feature:
npm --prefix tests/e2e test -- recipes.spec.ts
```

Playwright owns fresh API/Angular servers on ports 7285/4201 and a disposable PostgreSQL container.
Each spec gets a fresh installation and a private temporary runtime directory containing short-lived
localhost TLS credentials. The runner removes that directory after the owned fixture stops.
Do not point these tests at a production or developer installation. Existing listeners are never
reused. Only the test browser accepts the generated fixture certificate.

## Native and cross-client household acceptance

On a Mac with Xcode 27.2, the iOS simulator runtime, Docker and the browser dependencies above:

```sh
scripts/test-ios-households.sh
# Include a real feature journey after setup:
scripts/test-ios-households.sh RecipeEditingUITests
scripts/test-ios-households.sh CatalogueUITests
```

The script creates its own simulator, trusts the generated fixture certificate only in that
simulator, and builds the app with simulator ad hoc signing so Keychain is exercised normally.
It runs the actual setup, invitation sharing/pasting, member registration and profile-edit journey,
then checks the native profile edit from Angular against the same disposable household. Skipped
native journeys fail this gate. Cleanup stops owned processes and deletes the owned simulator.
Failure logs/results remain in a private temporary directory; treat UI recordings as sensitive,
including test setup secrets. Do not upload unredacted recordings or .xctestrun files.

The regular native unit/launch suite does not start the backend and explicitly skips this full
journey without its fixture. Hosted macOS runners currently execute native unit/launch tests;
the Docker-backed native journey is an additional local/release gate until a Mac runner with Docker
is configured. Browser acceptance runs in the Linux backend CI job. No hosted execution has been
claimed from local results. Feature acceptance gates are recorded under specs/001-household-recipe-manager/validation.
