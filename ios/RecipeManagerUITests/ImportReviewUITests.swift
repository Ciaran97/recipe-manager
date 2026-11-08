import XCTest

final class ImportReviewUITests: XCTestCase {
    @MainActor func testNativeTextImportReviewsSourceMeasuresUnknownsAndPersistsOnlyAfterCorrection() async throws {
        guard ProcessInfo.processInfo.environment["RECIPE_MANAGER_RUN_E2E"] == "1" else { throw XCTSkip("Run the signed isolated native import fixture.") }
        continueAfterFailure = false
        let environment = ProcessInfo.processInfo.environment
        let baseUrl = try XCTUnwrap(environment["RECIPE_MANAGER_E2E_AI_ORIGIN"]), source = try XCTUnwrap(environment["RECIPE_MANAGER_E2E_AI_TEXT"])
        let api = try NativeFixtureAPI(); try await api.login()
        let (settings, response) = try await api.read("/api/v1/household/settings")
        _ = try await api.write("PUT", "/api/v1/household/settings", body: ["name": try XCTUnwrap(settings["name"]), "timezone": try XCTUnwrap(settings["timezone"]),
            "ai": ["enabled": true, "provider": "OpenAICompatible", "baseUrl": baseUrl, "model": "deterministic-fixture", "clearApiKey": false]], etag: response.value(forHTTPHeaderField: "ETag"), expected: 200)
        for name in ["Rice", "Salt"] { _ = try await api.write("POST", "/api/v1/ingredients", body: ["canonicalName": name], expected: 201) }
        let app = XCUIApplication(); app.launch()
        XCTAssertTrue(app.tabBars.buttons["Recipes"].waitForExistence(timeout: 20)); app.tabBars.buttons["Recipes"].tap()
        featureTap(app.buttons["recipe-import-menu"], in: app)
        featureTap(app.buttons["Import URL or text"], in: app)
        let blockedURL = "http://127.0.0.1/private"
        featureEnter(app.textFields["import-url"], blockedURL, in: app)
        if app.toolbars.buttons["Done"].waitForExistence(timeout: 2) { app.toolbars.buttons["Done"].tap() }
        featureTap(app.buttons["import-start"], in: app)
        XCTAssertEqual(XCTWaiter.wait(for: [XCTNSPredicateExpectation(predicate: NSPredicate(format: "label CONTAINS %@", "public HTTP or HTTPS"), object: app.staticTexts["import-error"])], timeout: 15), .completed)
        XCTAssertEqual(app.textFields["import-url"].value as? String, blockedURL)
        let (afterBlockedURL, _) = try await api.read("/api/v1/recipes")
        XCTAssertEqual((afterBlockedURL["items"] as? [Any])?.count, 0)
        featureTap(app.descendants(matching: .any).matching(identifier: "import-method").firstMatch, in: app)
        featureTap(app.buttons["Pasted text with AI"], in: app)
        featureEnter(app.textViews["import-text"], "FAIL_PROVIDER", in: app, replace: true)
        if app.toolbars.buttons["Done"].waitForExistence(timeout: 2) { app.toolbars.buttons["Done"].tap() }
        featureTap(app.buttons["import-start"], in: app)
        XCTAssertEqual(XCTWaiter.wait(for: [XCTNSPredicateExpectation(predicate: NSPredicate(format: "label CONTAINS %@", "provider"), object: app.staticTexts["import-error"])], timeout: 15), .completed)
        XCTAssertEqual(app.textViews["import-text"].value as? String, "FAIL_PROVIDER")
        featureTap(app.buttons["import-clear-text"], in: app)
        featureEnter(app.textViews["import-text"], source, in: app)
        XCTAssertEqual(app.textViews["import-text"].value as? String, source)
        if app.toolbars.buttons["Done"].waitForExistence(timeout: 2) { app.toolbars.buttons["Done"].tap() }
        featureTap(app.buttons["import-start"], in: app)
        XCTAssertTrue(app.navigationBars["Review imported recipe"].waitForExistence(timeout: 20))
        let (before, _) = try await api.read("/api/v1/recipes"); XCTAssertEqual((before["items"] as? [Any])?.count, 0)
        featureEnter(app.textFields["recipe-title"], "Native imported rice", in: app, replace: true)
        featureTap(app.descendants(matching: .any).matching(identifier: "recipe-measurement-standard").firstMatch, in: app)
        featureTap(app.buttons["Australian"], in: app)
        // Unspecified salt needs no unit selection; preserve its qualifier through save.
        let acknowledgement = app.switches["I have reviewed unresolved or uncertain ingredient quantities"]
        featureReveal(acknowledgement, in: app); acknowledgement.coordinate(withNormalizedOffset: CGVector(dx: 0.9, dy: 0.5)).tap()
        XCTAssertEqual(XCTWaiter.wait(for: [XCTNSPredicateExpectation(predicate: NSPredicate(format: "value == '1'"), object: acknowledgement)], timeout: 10), .completed)
        featureTap(app.buttons["Save recipe"], in: app)
        XCTAssertEqual(XCTWaiter.wait(for: [XCTNSPredicateExpectation(predicate: NSPredicate(format: "exists == false"), object: app.navigationBars["Review imported recipe"])], timeout: 15), .completed)
        let (saved, _) = try await api.read("/api/v1/recipes")
        let recipes = try XCTUnwrap(saved["items"] as? [[String: Any]]); XCTAssertEqual(recipes.count, 1)
        let recipe = try XCTUnwrap(recipes.first); XCTAssertEqual(recipe["title"] as? String, "Native imported rice"); XCTAssertEqual(recipe["measurementStandard"] as? String, "AU")
        let ingredients = try XCTUnwrap(recipe["ingredients"] as? [[String: Any]])
        XCTAssertEqual(ingredients[0]["quantityText"] as? String, "1/2"); XCTAssertEqual(ingredients[0]["unit"] as? String, "cup")
        XCTAssertEqual(ingredients[1]["kind"] as? String, "unknown"); XCTAssertEqual(ingredients[1]["quantityText"] as? String, "to taste")
    }
}
