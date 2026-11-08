import XCTest

final class ShoppingUITests: XCTestCase {
    @MainActor func testPlannedSnapshotUnknownAmountsManualEditsAndReconciliation() async throws {
        guard ProcessInfo.processInfo.environment["RECIPE_MANAGER_RUN_E2E"] == "1" else { throw XCTSkip("Run the isolated native fixture with ShoppingUITests.") }
        continueAfterFailure = false
        let api = try NativeFixtureAPI(); try await api.login()
        let suffix = String(UUID().uuidString.prefix(8))
        let ingredientName = "Shopping salt " + suffix
        let listName = "Native shopping " + suffix
        let ingredient = try await api.write("POST", "/api/v1/ingredients", body: ["canonicalName": ingredientName], expected: 201)
        let ingredientId = try XCTUnwrap(ingredient["id"] as? String)
        let recipe = try await api.write("POST", "/api/v1/recipes", body: [
            "title": "Salt for shopping", "defaultServings": "2", "measurementStandard": "AU", "groups": [],
            "ingredients": [
                ["position": 0, "ingredientId": ingredientId, "quantityText": "5", "kind": "numeric", "unit": "g", "uncertain": false],
                ["position": 1, "ingredientId": ingredientId, "quantityText": "to taste", "kind": "unknown", "unit": "g", "uncertain": true]
            ], "instructions": [["position": 0, "text": "Season."]], "collectionIds": [], "tagIds": [], "acknowledgeUncertainIngredients": true
        ], expected: 201)
        let recipeId = try XCTUnwrap(recipe["id"] as? String)
        let (settings, _) = try await api.read("/api/v1/household/settings")
        var calendar = Calendar(identifier: .gregorian); calendar.timeZone = try XCTUnwrap(TimeZone(identifier: try XCTUnwrap(settings["timezone"] as? String)))
        let today = calendar.startOfDay(for: Date())
        let monday = try XCTUnwrap(calendar.date(byAdding: .day, value: -((calendar.component(.weekday, from: today) + 5) % 7), to: today))
        let formatter = DateFormatter(); formatter.locale = Locale(identifier: "en_US_POSIX"); formatter.timeZone = calendar.timeZone; formatter.dateFormat = "yyyy-MM-dd"
        let date = formatter.string(from: monday)
        let (slots, _) = try await api.read("/api/v1/meal-slots")
        let slot = try XCTUnwrap((slots["items"] as? [[String: Any]])?.first { $0["name"] as? String == "Dinner" }?["id"] as? String)
        let meal = try await api.write("POST", "/api/v1/meals/cooking", body: ["recipeId": recipeId, "date": date, "slotId": slot, "plannedPrepared": "4", "plannedEaten": "2"], expected: 201)
        let mealId = try XCTUnwrap(meal["id"] as? String)
        let event = try XCTUnwrap(meal["cookingEvent"] as? [String: Any])
        _ = try await api.write("POST", "/api/v1/meals/leftovers", body: ["sourceCookingEventId": try XCTUnwrap(event["id"] as? String), "sourceRevision": try XCTUnwrap(event["revision"] as? String), "date": formatter.string(from: try XCTUnwrap(calendar.date(byAdding: .day, value: 1, to: monday))), "slotId": slot, "plannedPortions": "2"], expected: 201)
        let app = XCUIApplication(); app.launch()
        XCTAssertTrue(app.tabBars.buttons["Shopping Lists"].waitForExistence(timeout: 20)); app.tabBars.buttons["Shopping Lists"].tap()
        featureTap(app.buttons["shopping-new-list"], in: app)
        featureEnter(app.textFields["shopping-list-name"], listName, in: app)
        featureTap(app.buttons["shopping-create"], in: app)
        closed("Create shopping list", app)
        let (index, _) = try await api.read("/api/v1/shopping-lists")
        let listId = try XCTUnwrap((index["items"] as? [[String: Any]])?.first { $0["name"] as? String == listName }?["id"] as? String)
        let (list, _) = try await api.read("/api/v1/shopping-lists/" + listId)
        let rows = try XCTUnwrap(list["items"] as? [[String: Any]])
        let ownRequirements = rows.filter { $0["ingredientId"] as? String == ingredientId }
        XCTAssertEqual(ownRequirements.count, 2)
        let known = try XCTUnwrap(ownRequirements.first { $0["unresolved"] as? Bool == false }); let knownId = try XCTUnwrap(known["id"] as? String)
        let unknown = try XCTUnwrap(ownRequirements.first { $0["unresolved"] as? Bool == true }); let unknownId = try XCTUnwrap(unknown["id"] as? String)
        XCTAssertEqual(known["displayText"] as? String, "10 g"); XCTAssertEqual(unknown["displayText"] as? String, "to taste")
        toggle(app.switches["shopping-purchased-" + knownId], app, expected: "1")
        featureTap(app.buttons["shopping-edit-" + knownId], in: app)
        featureEnter(app.textFields["shopping-quantity"], "12.5", in: app, replace: true)
        featureTap(app.buttons["shopping-save-item"], in: app); closed("Edit shopping item", app)
        featureTap(app.buttons["shopping-add-manual"], in: app)
        featureEnter(app.textFields["shopping-item-name"], "Native bread", in: app)
        featureEnter(app.textFields["shopping-quantity"], "as needed", in: app)
        featureTap(app.buttons["shopping-save-manual"], in: app); closed("Add manual item", app)
        let (_, version) = try await api.read("/api/v1/meals/" + mealId)
        _ = try await api.write("PUT", "/api/v1/meals/\(mealId)/cooking", body: ["recipeId": recipeId, "date": date, "slotId": slot, "plannedPrepared": "6", "plannedEaten": "2"], etag: version.value(forHTTPHeaderField: "ETag"), expected: 200)
        featureTap(app.buttons["Review updated requirements"], in: app); featureTap(app.buttons["shopping-preview"], in: app)
        let reviewedAmount = app.descendants(matching: .any).matching(identifier: "shopping-updated-" + (try XCTUnwrap(known["key"] as? String))).firstMatch
        featureReveal(reviewedAmount, in: app)
        XCTAssertTrue((reviewedAmount.label + " " + (reviewedAmount.value as? String ?? "")).contains("15 g"))
        toggle(app.switches["shopping-review-confirm"], app, expected: "1")
        featureTap(app.buttons["shopping-apply"], in: app); closed("Review requirements", app)
        let (final, _) = try await api.read("/api/v1/shopping-lists/" + listId)
        let finalRows = try XCTUnwrap(final["items"] as? [[String: Any]])
        let saved = try XCTUnwrap(finalRows.first { $0["id"] as? String == knownId })
        XCTAssertEqual(saved["purchased"] as? Bool, true); XCTAssertEqual(saved["displayText"] as? String, "12.5 g")
        XCTAssertEqual((saved["baselineExact"] as? [String: String])?["numerator"], "15")
        XCTAssertEqual(finalRows.first { $0["id"] as? String == unknownId }?["purchased"] as? Bool, false)
        XCTAssertTrue(finalRows.contains { $0["label"] as? String == "Native bread" })
        let (_, ingredientVersion) = try await api.read("/api/v1/ingredients/" + ingredientId)
        _ = try await api.write("PUT", "/api/v1/ingredients/" + ingredientId, body: ["canonicalName": "Corrected shopping salt " + suffix], etag: ingredientVersion.value(forHTTPHeaderField: "ETag"), expected: 200)
        let (_, correctedVersion) = try await api.read("/api/v1/ingredients/" + ingredientId)
        _ = try await api.write("DELETE", "/api/v1/ingredients/" + ingredientId, etag: correctedVersion.value(forHTTPHeaderField: "ETag"), expected: 409)
        featureTap(app.buttons["Refresh list"], in: app)
        let savedEdit = app.buttons["shopping-edit-" + knownId]
        featureReveal(savedEdit, in: app)
        XCTAssertEqual(savedEdit.label, "Edit " + ingredientName)
    }
    @MainActor private func closed(_ title: String, _ app: XCUIApplication) {
        XCTAssertEqual(XCTWaiter.wait(for: [XCTNSPredicateExpectation(predicate: NSPredicate(format: "exists == false"), object: app.navigationBars[title])], timeout: 15), .completed)
    }
    @MainActor private func toggle(_ element: XCUIElement, _ app: XCUIApplication, expected: String) {
        featureReveal(element, in: app)
        element.coordinate(withNormalizedOffset: CGVector(dx: 0.9, dy: 0.5)).tap()
        XCTAssertEqual(XCTWaiter.wait(for: [XCTNSPredicateExpectation(predicate: NSPredicate(format: "value == %@", expected), object: element)], timeout: 10), .completed)
    }
}
