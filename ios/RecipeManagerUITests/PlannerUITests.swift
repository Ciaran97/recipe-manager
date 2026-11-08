import XCTest

final class PlannerUITests: XCTestCase {
    @MainActor func testExplicitReservationsPartialConsumptionReleaseAndRecipeRefresh() async throws {
        guard ProcessInfo.processInfo.environment["RECIPE_MANAGER_RUN_E2E"] == "1" else { throw XCTSkip("Run the isolated native fixture with PlannerUITests.") }
        continueAfterFailure = false
        let api = try NativeFixtureAPI(); try await api.login()
        let ingredient = try await api.ingredient(named: "Red capsicum")
        let recipeId = try await api.seedRecipe(ingredientId: XCTUnwrap(ingredient["id"] as? String))
        let (settings, _) = try await api.read("/api/v1/household/settings")
        var calendar = Calendar(identifier: .gregorian); calendar.timeZone = try XCTUnwrap(TimeZone(identifier: try XCTUnwrap(settings["timezone"] as? String)))
        let today = calendar.startOfDay(for: Date())
        let monday = try XCTUnwrap(calendar.date(byAdding: .day, value: -((calendar.component(.weekday, from: today) + 5) % 7), to: today))
        let format = DateFormatter(); format.locale = Locale(identifier: "en_US_POSIX"); format.timeZone = calendar.timeZone; format.dateFormat = "yyyy-MM-dd"
        let from = format.string(from: monday), to = format.string(from: try XCTUnwrap(calendar.date(byAdding: .day, value: 6, to: monday)))
        let path = "/api/v1/meals?from=\(from)&to=\(to)"
        let (before, _) = try await api.read(path)
        let existingIds = Set((before["items"] as? [[String: Any]] ?? []).compactMap { $0["id"] as? String })
        let app = XCUIApplication(); app.launch()
        XCTAssertTrue(app.tabBars.buttons["Meal Planner"].waitForExistence(timeout: 20))
        app.tabBars.buttons["Meal Planner"].tap()
        featureTap(app.buttons["planner-day-0"], in: app)
        featureTap(app.buttons["planner-add-Dinner"], in: app); featureTap(app.buttons["Cook recipe"], in: app)
        featureTap(app.buttons["meal-recipe-\(recipeId)"], in: app)
        featureEnter(app.textFields["Portions to prepare"], "4", in: app, replace: true)
        featureEnter(app.textFields["Portions to eat"], "2", in: app, replace: true)
        featureTap(app.buttons["meal-save"], in: app)
        closed("Cook recipe", in: app)

        let (planned, _) = try await api.read(path)
        let added = (planned["items"] as? [[String: Any]] ?? []).filter { !existingIds.contains($0["id"] as? String ?? "") }
        let cook = try XCTUnwrap(added.first { $0["type"] as? String == "cook" })
        let mealId = try XCTUnwrap(cook["id"] as? String)
        let event = try XCTUnwrap(cook["cookingEvent"] as? [String: Any]); let sourceId = try XCTUnwrap(event["id"] as? String)
        XCTAssertEqual(event["plannedPrepared"] as? String, "4"); XCTAssertNil(event["actualPrepared"] as? String)
        XCTAssertEqual(added.count, 1)

        featureTap(app.buttons["planner-day-1"], in: app)
        featureTap(app.buttons["planner-add-Dinner"], in: app); featureTap(app.buttons["Eat leftovers"], in: app)
        featureTap(app.buttons["leftovers-source-\(sourceId)"], in: app)
        featureTap(app.buttons["leftovers-save"], in: app)
        closed("Eat leftovers", in: app)
        let (reserved, _) = try await api.read(path)
        let leftover = try XCTUnwrap((reserved["items"] as? [[String: Any]])?.first { $0["type"] as? String == "leftovers" && !existingIds.contains($0["id"] as? String ?? "") })
        let leftoverId = try XCTUnwrap(leftover["id"] as? String)
        featureTap(app.buttons["planner-meal-\(leftoverId)"], in: app)
        featureEnter(app.textFields["Actually consumed portions"], "1.5", in: app, replace: true)
        featureEnter(app.textFields["Reservation after confirmation"], "0.5", in: app, replace: true)
        let confirm = app.switches["leftovers-confirm-source"]
        featureReveal(confirm, in: app)
        confirm.coordinate(withNormalizedOffset: CGVector(dx: 0.9, dy: 0.5)).tap()
        XCTAssertEqual(XCTWaiter.wait(for: [XCTNSPredicateExpectation(predicate: NSPredicate(format: "value == '1'"), object: confirm)], timeout: 5), .completed)
        featureTap(app.buttons["Confirm leftover consumption"], in: app)
        closed("Eat leftovers", in: app)
        let (consumed, _) = try await api.read("/api/v1/cooking-events/\(sourceId)/leftovers")
        XCTAssertEqual(consumed["actualPrepared"] as? String, "4")
        let confirmedAllocation = try XCTUnwrap((consumed["allocations"] as? [[String: Any]])?.first?["allocation"] as? [String: Any])
        XCTAssertEqual(confirmedAllocation["consumedPortions"] as? String, "1.5")
        XCTAssertEqual(confirmedAllocation["reservedRemainder"] as? String, "0.5")
        featureTap(app.buttons["planner-meal-\(leftoverId)"], in: app)
        featureTap(app.buttons["Release unused reservation"], in: app)
        featureTap(app.buttons["leftovers-save"], in: app)
        closed("Eat leftovers", in: app)
        let (balance, _) = try await api.read("/api/v1/cooking-events/\(sourceId)/leftovers")
        XCTAssertEqual(balance["availablePortions"] as? String, "0.5")
        let allocation = try XCTUnwrap((balance["allocations"] as? [[String: Any]])?.first?["allocation"] as? [String: Any])
        XCTAssertEqual(allocation["plannedPortions"] as? String, "2"); XCTAssertEqual(allocation["consumedPortions"] as? String, "1.5"); XCTAssertEqual(allocation["reservedRemainder"] as? String, "0")

        let (recipe, response) = try await api.read("/api/v1/recipes/\(recipeId)")
        var edited = recipe
        for key in ["id", "revision", "archived", "favourite", "policyVersion"] { edited.removeValue(forKey: key) }
        // Use the complete write projection, excluding computed read-only ingredient fields.
        edited["ingredients"] = (recipe["ingredients"] as? [[String: Any]])?.map { line in
            line.filter { ["id", "groupId", "position", "ingredientId", "quantityText", "kind", "unit", "preparation", "uncertain"].contains($0.key) }
        }
        edited["title"] = "Pepper salad updated"
        _ = try await api.write("PUT", "/api/v1/recipes/\(recipeId)", body: edited, etag: response.value(forHTTPHeaderField: "ETag"), expected: 200)
        let (frozen, _) = try await api.read("/api/v1/meals/\(mealId)")
        XCTAssertEqual(((frozen["cookingEvent"] as? [String: Any])?["recipeSnapshot"] as? [String: Any])?["title"] as? String, "Pepper salad")
        featureTap(app.buttons["planner-day-0"], in: app)
        featureTap(app.buttons["planner-meal-\(mealId)"], in: app)
        featureTap(app.buttons["Actual quantities, leftovers and recipe refresh"], in: app)
        featureTap(app.buttons["Review latest recipe"], in: app)
        featureTap(app.buttons["Compare saved and latest recipe"], in: app)
        XCTAssertTrue(app.staticTexts["Pepper salad updated"].waitForExistence(timeout: 10))
        app.navigationBars["Recipe changes"].buttons.element(boundBy: 0).tap()
        closed("Recipe changes", in: app)
        featureTap(app.buttons["Apply reviewed recipe refresh"], in: app)
        closed("Cooking and leftovers", in: app)
        let (refreshed, _) = try await api.read("/api/v1/meals/\(mealId)")
        XCTAssertEqual(((refreshed["cookingEvent"] as? [String: Any])?["recipeSnapshot"] as? [String: Any])?["title"] as? String, "Pepper salad updated")
        featureTap(app.buttons["Cancel"], in: app)
        featureTap(app.buttons["planner-day-2"], in: app)
        featureTap(app.buttons["planner-add-Dinner"], in: app); featureTap(app.buttons["Eat leftovers"], in: app)
        featureTap(app.buttons["leftovers-source-\(sourceId)"], in: app)
        featureEnter(app.textFields["Planned leftover portions"], "0.5", in: app, replace: true)
        featureTap(app.buttons["leftovers-save"], in: app)
        closed("Eat leftovers", in: app)
        let (newBalance, _) = try await api.read("/api/v1/cooking-events/\(sourceId)/leftovers")
        let secondId = try XCTUnwrap((newBalance["allocations"] as? [[String: Any]])?.first { $0["mealId"] as? String != leftoverId }?["mealId"] as? String)
        featureTap(app.buttons["planner-day-0"], in: app); featureTap(app.buttons["planner-meal-\(mealId)"], in: app)
        featureTap(app.buttons["Actual quantities, leftovers and recipe refresh"], in: app)
        featureEnter(app.textFields["Actually prepared portions"], "3", in: app, replace: true)
        featureEnter(app.textFields["Actually eaten at cooking meal"], "1.5", in: app, replace: true)
        featureTap(app.buttons["Confirm cooking quantities"], in: app)
        featureTap(app.descendants(matching: .any).matching(identifier: "resolution-\(secondId)").firstMatch, in: app)
        featureTap(app.buttons["Release unused reservation"], in: app)
        featureTap(app.buttons["Apply source change and resolutions"], in: app)
        closed("Cooking and leftovers", in: app)
        let (corrected, _) = try await api.read("/api/v1/cooking-events/\(sourceId)/leftovers")
        XCTAssertEqual(corrected["actualPrepared"] as? String, "3"); XCTAssertEqual(corrected["actualEaten"] as? String, "1.5")
        XCTAssertEqual(corrected["availablePortions"] as? String, "0")
        let correctedAllocations = try XCTUnwrap(corrected["allocations"] as? [[String: Any]])
        XCTAssertEqual((correctedAllocations.first { $0["mealId"] as? String == leftoverId }?["allocation"] as? [String: Any])?["consumedPortions"] as? String, "1.5")
        XCTAssertEqual((correctedAllocations.first { $0["mealId"] as? String == secondId }?["allocation"] as? [String: Any])?["reservedRemainder"] as? String, "0")
    }
    @MainActor private func closed(_ title: String, in app: XCUIApplication) {
        XCTAssertEqual(XCTWaiter.wait(for: [XCTNSPredicateExpectation(predicate: NSPredicate(format: "exists == false"), object: app.navigationBars[title])], timeout: 15), .completed, "The accepted action must close its editor before continuing.")
    }
}
