import XCTest

final class RecipeLifecycleParityTests: XCTestCase {
    @MainActor func testCompleteDuplicateIsIndependentRestoreAndReferencedDeletionAreSafe() async throws {
        guard ProcessInfo.processInfo.environment["RECIPE_MANAGER_RUN_E2E"] == "1" else {
            throw XCTSkip("Run the isolated signed RecipeLifecycleParityTests fixture.")
        }
        continueAfterFailure = false
        let api = try NativeFixtureAPI(); try await api.login()
        let ingredient = try await api.write("POST", "/api/v1/ingredients", body: ["canonicalName": "Lifecycle rice"], expected: 201)
        let collection = try await api.write("POST", "/api/v1/collections", body: ["name": "Lifecycle collection"], expected: 201)
        let tag = try await api.write("POST", "/api/v1/tags", body: ["name": "Lifecycle tag"], expected: 201)
        let groupId = UUID().uuidString
        let recipe = try await api.write("POST", "/api/v1/recipes", body: [
            "title": "Complete lifecycle recipe", "description": "Complete duplicate fixture", "defaultServings": "2",
            "measurementStandard": "US", "prepMinutes": 5, "cookMinutes": 10, "notes": "Keep these notes.",
            "sourceUrl": "https://example.test/lifecycle", "groups": [["id": groupId, "position": 0, "title": "Sauce"]],
            "ingredients": [["position": 0, "ingredientId": try XCTUnwrap(ingredient["id"] as? String), "quantityText": "1/2", "kind": "numeric", "unit": "cup", "groupId": groupId, "preparation": "rinsed", "uncertain": false]],
            "instructions": [["position": 0, "text": "Rinse rice."], ["position": 1, "text": "Cook rice."]],
            "collectionIds": [try XCTUnwrap(collection["id"] as? String)], "tagIds": [try XCTUnwrap(tag["id"] as? String)]
        ], expected: 201)
        let id = try XCTUnwrap(recipe["id"] as? String)
        let app = XCUIApplication(); app.launch()
        XCTAssertTrue(app.tabBars.buttons["Recipes"].waitForExistence(timeout: 20))
        featureTap(app.tabBars.buttons["Recipes"], in: app)
        featureTap(app.buttons["recipe-library-" + id], in: app)
        featureTap(app.buttons["Duplicate recipe"], in: app)
        var copy: [String: Any]?
        for _ in 0..<30 {
            let (index, _) = try await api.read("/api/v1/recipes?q=Complete%20lifecycle%20recipe")
            copy = (index["items"] as? [[String: Any]])?.first { $0["id"] as? String != id && $0["title"] as? String == "Complete lifecycle recipe" }
            if copy != nil { break }
            try await Task.sleep(for: .milliseconds(100))
        }
        let copyId = try XCTUnwrap(copy?["id"] as? String)
        let (duplicate, _) = try await api.read("/api/v1/recipes/" + copyId)
        for key in ["title", "description", "defaultServings", "notes", "sourceUrl", "measurementStandard"] {
            XCTAssertEqual(duplicate[key] as? String, recipe[key] as? String, key)
        }
        XCTAssertEqual(duplicate["prepMinutes"] as? Int, 5); XCTAssertEqual(duplicate["cookMinutes"] as? Int, 10)
        XCTAssertEqual(duplicate["collectionIds"] as? [String], recipe["collectionIds"] as? [String])
        XCTAssertEqual(duplicate["tagIds"] as? [String], recipe["tagIds"] as? [String])
        let copyGroups = try XCTUnwrap(duplicate["groups"] as? [[String: Any]])
        XCTAssertEqual(copyGroups.first?["title"] as? String, "Sauce")
        XCTAssertNotEqual(copyGroups.first?["id"] as? String, groupId.lowercased())
        let lines = try XCTUnwrap(duplicate["ingredients"] as? [[String: Any]])
        XCTAssertEqual(lines.first?["quantityText"] as? String, "1/2")
        XCTAssertEqual(lines.first?["preparation"] as? String, "rinsed")
        XCTAssertEqual(lines.first?["groupId"] as? String, copyGroups.first?["id"] as? String)
        XCTAssertEqual((duplicate["instructions"] as? [[String: Any]])?.compactMap { $0["text"] as? String }, ["Rinse rice.", "Cook rice."])
        featureTap(app.buttons["Edit recipe"], in: app)
        featureEnter(app.textFields["recipe-title"], "Independent lifecycle copy", in: app, replace: true)
        featureEnter(app.textFields["Quantity 1"], "2.5", in: app, replace: true)
        featureTap(app.buttons["Save recipe"], in: app)
        XCTAssertTrue(app.navigationBars["Independent lifecycle copy"].waitForExistence(timeout: 15))
        let (original, _) = try await api.read("/api/v1/recipes/" + id)
        XCTAssertEqual(original["title"] as? String, "Complete lifecycle recipe")
        XCTAssertEqual((original["ingredients"] as? [[String: Any]])?.first?["quantityText"] as? String, "1/2")
        let (edited, _) = try await api.read("/api/v1/recipes/" + copyId)
        XCTAssertEqual(edited["title"] as? String, "Independent lifecycle copy")
        XCTAssertEqual((edited["ingredients"] as? [[String: Any]])?.first?["quantityText"] as? String, "2.5")
        featureTap(app.buttons["Archive recipe"], in: app)
        featureTap(app.buttons["Restore recipe"], in: app)
        XCTAssertTrue(app.buttons["Archive recipe"].waitForExistence(timeout: 15))
        let (restored, _) = try await api.read("/api/v1/recipes/" + copyId)
        XCTAssertEqual(restored["archived"] as? Bool, false)
        let (slots, _) = try await api.read("/api/v1/meal-slots")
        let dinner = try XCTUnwrap((slots["items"] as? [[String: Any]])?.first { $0["name"] as? String == "Dinner" }?["id"] as? String)
        let (settings, _) = try await api.read("/api/v1/household/settings")
        let format = DateFormatter(); format.locale = Locale(identifier: "en_US_POSIX"); format.timeZone = try XCTUnwrap(TimeZone(identifier: try XCTUnwrap(settings["timezone"] as? String))); format.dateFormat = "yyyy-MM-dd"
        let meal = try await api.write("POST", "/api/v1/meals/cooking", body: ["recipeId": id, "date": format.string(from: Date()), "slotId": dinner, "plannedPrepared": "4", "plannedEaten": "2"], expected: 201)
        let mealId = try XCTUnwrap(meal["id"] as? String)
        featureTap(app.navigationBars.buttons.element(boundBy: 0), in: app)
        XCTAssertTrue(app.navigationBars["Complete lifecycle recipe"].waitForExistence(timeout: 15))
        featureTap(app.buttons["Delete recipe"], in: app); featureConfirm("Delete recipe", in: app)
        XCTAssertTrue(app.staticTexts["This recipe is used by a meal or shopping list. Archive it instead."].waitForExistence(timeout: 15))
        _ = try await api.read("/api/v1/recipes/" + id)
        let (retainedMeal, _) = try await api.read("/api/v1/meals/" + mealId)
        XCTAssertEqual(((retainedMeal["cookingEvent"] as? [String: Any])?["recipeSnapshot"] as? [String: Any])?["title"] as? String, "Complete lifecycle recipe")
    }
}
