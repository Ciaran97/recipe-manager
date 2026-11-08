import XCTest

final class RecipeEditingUITests: XCTestCase {
    @MainActor func testStructuredEditingServingPreviewAndRecipeLifecycle() async throws {
        guard ProcessInfo.processInfo.environment["RECIPE_MANAGER_RUN_E2E"] == "1" else { throw XCTSkip("Run the isolated native fixture with RecipeEditingUITests.") }
        continueAfterFailure = false
        let api = try NativeFixtureAPI(); try await api.login()
        _ = try await api.ingredient(named: "Rice")
        let title = "Native rice " + String(UUID().uuidString.prefix(8))
        let editedTitle = title + " edited"
        let app = XCUIApplication(); app.launch()
        XCTAssertTrue(app.tabBars.buttons["Recipes"].waitForExistence(timeout: 20)); app.tabBars.buttons["Recipes"].tap()
        featureTap(app.buttons["New recipe"], in: app)
        featureEnter(app.textFields["recipe-title"], title, in: app)
        featureEnter(app.textFields["Ingredient name 1"], "Rice", in: app)
        featureTap(app.buttons["Use ingredient Rice"], in: app)
        XCTAssertEqual(XCTWaiter.wait(for: [XCTNSPredicateExpectation(predicate: NSPredicate(format: "exists == false"), object: app.buttons["Use ingredient Rice"])], timeout: 10), .completed)
        featureEnter(app.textFields["Quantity 1"], "1/2", in: app)
        featureTap(app.descendants(matching: .any).matching(NSPredicate(format: "identifier BEGINSWITH %@", "recipe-unit-")).firstMatch, in: app); featureTap(app.buttons["Cup"], in: app)
        featureEnter(app.descendants(matching: .any).matching(NSPredicate(format: "identifier BEGINSWITH %@", "recipe-instruction-")).firstMatch, "Cook the rice.", in: app)
        featureTap(app.buttons["Save recipe"], in: app)
        featureTap(app.buttons[title], in: app)
        featureEnter(app.textFields["Preview servings"], "4", in: app, replace: true)
        featureTap(app.buttons["Scale ingredients"], in: app)
        XCTAssertTrue(app.staticTexts["1 cup Rice"].waitForExistence(timeout: 10))
        let (library, _) = try await api.read("/api/v1/recipes")
        let original = try XCTUnwrap((library["items"] as? [[String: Any]])?.first { $0["title"] as? String == title })
        let id = try XCTUnwrap(original["id"] as? String)
        let (recipe, _) = try await api.read("/api/v1/recipes/" + id)
        XCTAssertEqual((recipe["ingredients"] as? [[String: Any]])?.first?["quantityText"] as? String, "1/2")
        featureTap(app.buttons["Edit recipe"], in: app)
        featureEnter(app.textFields["recipe-title"], editedTitle, in: app, replace: true)
        featureTap(app.buttons["Save recipe"], in: app)
        featureTap(app.buttons["Favourite recipe"], in: app)
        XCTAssertTrue(app.buttons["Remove favourite"].waitForExistence(timeout: 10))
        featureTap(app.buttons["Duplicate recipe"], in: app)
        featureTap(app.buttons["Archive recipe"], in: app)
        XCTAssertTrue(app.buttons["Restore recipe"].waitForExistence(timeout: 10))
        featureTap(app.buttons["Delete recipe"], in: app); featureConfirm("Delete recipe", in: app)
        XCTAssertTrue(app.navigationBars[editedTitle].waitForExistence(timeout: 10))
        let (latest, _) = try await api.read("/api/v1/recipes")
        let saved = try XCTUnwrap((latest["items"] as? [[String: Any]])?.first { $0["id"] as? String == id })
        XCTAssertEqual(saved["title"] as? String, editedTitle); XCTAssertEqual(saved["favourite"] as? Bool, false) // The fixture reads as Administrator; native is signed in as Member.
    }
    @MainActor func testUnspecifiedQuantitySavesWithoutSelectingAUnit() async throws {
        guard ProcessInfo.processInfo.environment["RECIPE_MANAGER_RUN_E2E"] == "1" else { throw XCTSkip("Run the isolated native recipe fixture.") }
        continueAfterFailure = false
        let api = try NativeFixtureAPI(); try await api.login()
        let name = "Unspecified salt " + String(UUID().uuidString.prefix(8))
        let ingredient = try await api.ingredient(named: name)
        let ingredientId = try XCTUnwrap(ingredient["id"] as? String)
        let title = "Unspecified quantity " + String(UUID().uuidString.prefix(8))
        let app = XCUIApplication(); app.launch()
        featureTap(app.tabBars.buttons["Recipes"], in: app)
        featureTap(app.buttons["New recipe"], in: app)
        featureEnter(app.textFields["recipe-title"], title, in: app)
        featureEnter(app.textFields["Ingredient name 1"], name, in: app)
        featureTap(app.buttons["Use ingredient " + name], in: app)
        let unspecified = app.switches["Quantity unspecified 1"]
        featureReveal(unspecified, in: app)
        unspecified.coordinate(withNormalizedOffset: CGVector(dx: 0.9, dy: 0.5)).tap()
        featureEnter(app.textFields["Quantity note 1"], "to taste", in: app)
        // Deliberately leave the unit picker untouched.
        featureEnter(app.descendants(matching: .any).matching(NSPredicate(format: "identifier BEGINSWITH %@", "recipe-instruction-")).firstMatch, "Season to taste.", in: app)
        let reviewed = app.switches["I have reviewed unresolved or uncertain ingredient quantities"]
        featureReveal(reviewed, in: app)
        reviewed.coordinate(withNormalizedOffset: CGVector(dx: 0.9, dy: 0.5)).tap()
        featureTap(app.buttons["Save recipe"], in: app)
        XCTAssertEqual(XCTWaiter.wait(for: [XCTNSPredicateExpectation(predicate: NSPredicate(format: "exists == false"), object: app.buttons["Save recipe"])], timeout: 20), .completed)
        XCTAssertTrue(app.buttons["New recipe"].waitForExistence(timeout: 20))
        let query = try XCTUnwrap(title.addingPercentEncoding(withAllowedCharacters: .alphanumerics))
        let (library, _) = try await api.read("/api/v1/recipes?q=" + query)
        let saved = try XCTUnwrap((library["items"] as? [[String: Any]])?.first { $0["title"] as? String == title })
        let line = try XCTUnwrap((saved["ingredients"] as? [[String: Any]])?.first)
        XCTAssertEqual(line["kind"] as? String, "unknown")
        XCTAssertEqual(line["quantityText"] as? String, "to taste")
        XCTAssertEqual(line["unit"] as? String, "item") // Existing neutral wire marker, not an inferred quantity.
        let id = try XCTUnwrap(saved["id"] as? String)
        featureTap(app.buttons["recipe-library-" + id], in: app)
        featureTap(app.buttons["Delete recipe"], in: app); featureConfirm("Delete recipe", in: app)
        XCTAssertTrue(app.buttons["New recipe"].waitForExistence(timeout: 15))
        let (_, version) = try await api.read("/api/v1/ingredients/" + ingredientId)
        _ = try await api.write("DELETE", "/api/v1/ingredients/" + ingredientId, etag: version.value(forHTTPHeaderField: "ETag"), expected: 204)
    }

}
