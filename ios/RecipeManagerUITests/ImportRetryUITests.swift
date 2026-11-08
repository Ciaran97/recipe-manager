import XCTest

final class ImportRetryUITests: XCTestCase {
    @MainActor func testLostCommittedResponseResumesAfterAppRestartWithMissingDraftAndAiDisabled() async throws {
        guard ProcessInfo.processInfo.environment["RECIPE_MANAGER_RUN_E2E"] == "1" else { throw XCTSkip("Run the signed isolated share import fixture.") }
        continueAfterFailure = false
        let environment = ProcessInfo.processInfo.environment
        let source = try XCTUnwrap(environment["RECIPE_MANAGER_E2E_AI_TEXT"])
        let api = try NativeFixtureAPI(); try await api.login()
        let (settings, response) = try await api.read("/api/v1/household/settings")
        _ = try await api.write("PUT", "/api/v1/household/settings", body: ["name": try XCTUnwrap(settings["name"]), "timezone": try XCTUnwrap(settings["timezone"]),
            "ai": ["enabled": true, "provider": "OpenAICompatible", "baseUrl": try XCTUnwrap(environment["RECIPE_MANAGER_E2E_AI_ORIGIN"]), "model": "deterministic-fixture", "clearApiKey": false]], etag: response.value(forHTTPHeaderField: "ETag"), expected: 200)
        let (catalogue, _) = try await api.read("/api/v1/ingredients")
        let existingNames = (catalogue["items"] as? [[String: Any]] ?? []).compactMap { $0["canonicalName"] as? String }
        for name in ["Rice", "Salt"] where !existingNames.contains(name) {
            _ = try await api.write("POST", "/api/v1/ingredients", body: ["canonicalName": name], expected: 201)
        }
        let (initial, _) = try await api.read("/api/v1/recipes")
        let originalCount = try XCTUnwrap(initial["items"] as? [Any]).count
        let app = XCUIApplication(); app.launch(); XCTAssertTrue(app.tabBars.buttons["Recipes"].waitForExistence(timeout: 20)); app.terminate()
        let host = XCUIApplication(bundleIdentifier: (environment["RECIPE_MANAGER_E2E_APP_BUNDLE_ID"] ?? "local.recipemanager.app") + ".ShareHost")
        host.launchEnvironment["RECIPE_MANAGER_SHARE_TEXT"] = source; host.launch()
        featureTap(host.buttons["fixture-share-text"], in: host)
        if !host.buttons["Recipe Manager"].waitForExistence(timeout: 3), host.buttons["More"].exists { host.buttons["More"].tap() }
        featureTap(host.buttons["Recipe Manager"].exists ? host.buttons["Recipe Manager"] : host.staticTexts["Recipe Manager"], in: host)
        XCTAssertTrue(host.buttons["share-save"].waitForExistence(timeout: 15))
        if host.buttons["share-candidate"].exists {
            featureTap(host.buttons["share-candidate"], in: host)
            featureTap(host.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Recipe text:")).firstMatch, in: host)
        }
        featureTap(host.buttons["share-save"], in: host)
        if !host.buttons["fixture-share-text"].waitForExistence(timeout: 5), host.buttons["Close"].exists { host.buttons["Close"].tap() }
        app.activate(); featureTap(app.tabBars.buttons["Recipes"], in: app)
        featureTap(app.buttons["recipe-pending-captures"], in: app)
        let review = app.buttons.matching(NSPredicate(format: "identifier BEGINSWITH %@", "capture-review-")).firstMatch
        XCTAssertTrue(review.waitForExistence(timeout: 15))
        let handoffId = String(review.identifier.dropFirst("capture-review-".count))
        featureTap(review, in: app); featureTap(app.buttons["capture-confirm-destination"], in: app)
        featureTap(app.buttons["import-start"], in: app)
        XCTAssertTrue(app.navigationBars["Review imported recipe"].waitForExistence(timeout: 20))
        let (before, _) = try await api.read("/api/v1/recipes"); XCTAssertEqual((before["items"] as? [Any])?.count, originalCount)
        featureEnter(app.textFields["recipe-title"], "Recovered shared rice", in: app, replace: true)
        featureTap(app.descendants(matching: .any).matching(identifier: "recipe-measurement-standard").firstMatch, in: app); featureTap(app.buttons["Australian"], in: app)
        featureTap(app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Unit 2")).firstMatch, in: app); featureTap(app.buttons["Whole item"], in: app)
        let acknowledgement = app.switches["I have reviewed unresolved or uncertain ingredient quantities"]
        featureReveal(acknowledgement, in: app); acknowledgement.coordinate(withNormalizedOffset: CGVector(dx: 0.9, dy: 0.5)).tap()
        featureTap(app.buttons["Save recipe"], in: app)
        XCTAssertTrue(app.buttons["import-retry-save"].waitForExistence(timeout: 20))
        app.terminate()
        host.activate()
        let savedDraft = host.staticTexts["fixture-draft-" + handoffId]
        XCTAssertTrue(savedDraft.waitForExistence(timeout: 15))
        let draftId = savedDraft.label
        XCTAssertNotNil(UUID(uuidString: draftId))
        let owner = try NativeFixtureAPI()
        _ = try await owner.write("POST", "/api/v1/auth/login", body: ["email": "member@example.test", "password": "e2e-member-test-passphrase"], expected: 200)
        let (_, draftResponse) = try await owner.read("/api/v1/imports/" + draftId)
        _ = try await owner.write("DELETE", "/api/v1/imports/" + draftId, etag: draftResponse.value(forHTTPHeaderField: "ETag"), expected: 204)
        _ = try await owner.read("/api/v1/imports/" + draftId, expected: 410)
        let (currentSettings, currentResponse) = try await api.read("/api/v1/household/settings")
        _ = try await api.write("PUT", "/api/v1/household/settings", body: ["name": try XCTUnwrap(currentSettings["name"]), "timezone": try XCTUnwrap(currentSettings["timezone"]),
            "ai": ["enabled": false, "provider": "OpenAICompatible", "baseUrl": try XCTUnwrap(environment["RECIPE_MANAGER_E2E_AI_ORIGIN"]), "model": "deterministic-fixture", "clearApiKey": false]], etag: currentResponse.value(forHTTPHeaderField: "ETag"), expected: 200)
        app.launch(); featureTap(app.tabBars.buttons["Recipes"], in: app)
        featureTap(app.buttons["recipe-pending-captures"], in: app)
        featureTap(app.buttons["capture-review-" + handoffId], in: app)
        featureTap(app.buttons["capture-confirm-destination"], in: app)
        XCTAssertTrue(app.staticTexts["No pending captures. Share a recipe URL or text to Recipe Manager from another application."].waitForExistence(timeout: 20))
        let (saved, _) = try await api.read("/api/v1/recipes"); let recipes = try XCTUnwrap(saved["items"] as? [[String: Any]]); XCTAssertEqual(recipes.count, originalCount + 1)
        XCTAssertEqual(recipes.filter { $0["title"] as? String == "Recovered shared rice" }.count, 1)
        _ = try await api.read("/api/v1/import-handoffs/" + handoffId, expected: 404)
        let (handoff, _) = try await owner.read("/api/v1/import-handoffs/" + handoffId)
        XCTAssertEqual(handoff["state"] as? String, "Committed")
    }
}
