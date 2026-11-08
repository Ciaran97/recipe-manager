import XCTest

final class RecipeSearchUITests: XCTestCase {
    @MainActor func testSharedOrganiserEditingAssignmentsFilteringAndSafeDeletion() async throws {
        guard ProcessInfo.processInfo.environment["RECIPE_MANAGER_RUN_E2E"] == "1" else { throw XCTSkip("Run the isolated signed recipe search fixture.") }
        continueAfterFailure = false
        let api = try NativeFixtureAPI(); try await api.login()
        let rice = try await api.write("POST", "/api/v1/ingredients", body: ["canonicalName": "Rice"], expected: 201)
        let riceId = try XCTUnwrap(rice["id"] as? String)
        let (_, ingredientResponse) = try await api.read("/api/v1/ingredients/" + riceId)
        _ = try await api.write("POST", "/api/v1/ingredients/" + riceId + "/aliases", body: ["name": "Wholegrain rice"], etag: ingredientResponse.value(forHTTPHeaderField: "ETag"), expected: 200)
        let recipe = try await api.write("POST", "/api/v1/recipes", body: ["title": "Organised native rice", "defaultServings": "2", "measurementStandard": "AU", "groups": [],
            "ingredients": [["position": 0, "ingredientId": riceId, "quantityText": "1", "kind": "numeric", "unit": "cup"]], "instructions": [["position": 0, "text": "Cook rice."]], "collectionIds": [], "tagIds": []], expected: 201)
        let recipeId = try XCTUnwrap(recipe["id"] as? String)
        _ = try await api.seedRecipe(ingredientId: riceId)
        let app = XCUIApplication(); app.launch(); XCTAssertTrue(app.tabBars.buttons["Recipes"].waitForExistence(timeout: 20)); featureTap(app.tabBars.buttons["Recipes"], in: app)
        featureTap(app.buttons["Collections and tags"], in: app)
        featureEnter(app.textFields["organiser-name"], "Weeknight", in: app)
        featureTap(app.buttons["organiser-save"], in: app)
        XCTAssertTrue(app.buttons["Rename Weeknight"].waitForExistence(timeout: 15))
        featureTap(app.buttons["Rename Weeknight"], in: app)
        featureEnter(app.textFields["organiser-name"], "Weekdays", in: app, replace: true)
        featureTap(app.buttons["organiser-save"], in: app)
        XCTAssertTrue(app.buttons["Rename Weekdays"].waitForExistence(timeout: 15))
        featureTap(app.buttons["Tags"], in: app)
        featureEnter(app.textFields["organiser-name"], "Comfort", in: app)
        featureTap(app.buttons["organiser-save"], in: app)
        XCTAssertTrue(app.buttons["Rename Comfort"].waitForExistence(timeout: 15))
        featureTap(app.navigationBars.buttons["Recipes"], in: app)
        featureTap(app.buttons["Organised native rice"], in: app)
        featureTap(app.buttons["Edit recipe"], in: app)
        featureTap(app.buttons["Choose collections and tags"], in: app)
        for name in ["Weekdays", "Comfort"] {
            let toggle = app.switches[name]; featureReveal(toggle, in: app); toggle.coordinate(withNormalizedOffset: CGVector(dx: 0.9, dy: 0.5)).tap()
        }
        featureTap(app.buttons["Done"], in: app)
        featureTap(app.buttons["Save recipe"], in: app)
        let (saved, _) = try await api.read("/api/v1/recipes/" + recipeId)
        XCTAssertEqual((saved["collectionIds"] as? [String])?.count, 1); XCTAssertEqual((saved["tagIds"] as? [String])?.count, 1)
        featureTap(app.navigationBars.buttons["Recipes"], in: app)
        featureTap(app.buttons["Filter by collections and tags"], in: app)
        for name in ["Weekdays", "Comfort"] {
            let toggle = app.switches[name]; featureReveal(toggle, in: app); toggle.coordinate(withNormalizedOffset: CGVector(dx: 0.9, dy: 0.5)).tap()
        }
        featureTap(app.buttons["Done"], in: app)
        XCTAssertEqual(XCTWaiter.wait(for: [XCTNSPredicateExpectation(predicate: NSPredicate(format: "exists == false"), object: app.buttons["Pepper salad"])], timeout: 15), .completed)
        featureEnter(app.searchFields.firstMatch, "WHOLEGRAIN", in: app)
        featureTap(app.keyboards.buttons["Search"], in: app)
        XCTAssertTrue(app.buttons["Organised native rice"].waitForExistence(timeout: 15))
        featureTap(app.buttons["Collections and tags"], in: app)
        featureTap(app.buttons["Delete Weekdays"], in: app); featureConfirm("Delete organiser", in: app)
        XCTAssertTrue(app.staticTexts["Deleted. Recipes were kept."].waitForExistence(timeout: 15))
        let (retained, _) = try await api.read("/api/v1/recipes/" + recipeId)
        XCTAssertEqual(retained["title"] as? String, "Organised native rice")
        XCTAssertEqual((retained["collectionIds"] as? [String])?.count, 0); XCTAssertEqual((retained["tagIds"] as? [String])?.count, 1)
    }
}
