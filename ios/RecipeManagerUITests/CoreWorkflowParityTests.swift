import XCTest

/// Additional interaction coverage beyond the continuous critical journey.
final class CoreWorkflowParityTests: XCTestCase {
    @MainActor private func fixture() async throws -> (NativeFixtureAPI, XCUIApplication) {
        guard ProcessInfo.processInfo.environment["RECIPE_MANAGER_RUN_E2E"] == "1" else {
            throw XCTSkip("Run the isolated signed CoreWorkflowParityTests fixture.")
        }
        continueAfterFailure = false
        let api = try NativeFixtureAPI(); try await api.login()
        let app = XCUIApplication(); app.launch()
        XCTAssertTrue(app.tabBars.buttons["Recipes"].waitForExistence(timeout: 20))
        return (api, app)
    }

    @MainActor func testIngredientGroupsDuplicationOrderingAndRemovalPersist() async throws {
        let (api, app) = try await fixture()
        let rice = try await api.write("POST", "/api/v1/ingredients", body: ["canonicalName": "Parity rice"], expected: 201)
        let id = try await api.seedRecipe(ingredientId: XCTUnwrap(rice["id"] as? String), title: "Grouped parity recipe")
        featureTap(app.tabBars.buttons["Recipes"], in: app)
        featureTap(app.buttons["Grouped parity recipe"], in: app)
        featureTap(app.buttons["Edit recipe"], in: app)
        featureTap(app.buttons["Ingredient groups"], in: app)
        featureTap(app.buttons["Add ingredient group"], in: app)
        featureEnter(app.textFields["Group heading"].firstMatch, "Vegetables", in: app)
        featureTap(app.buttons["Add ingredient group"], in: app)
        featureEnter(app.textFields.matching(identifier: "Group heading").element(boundBy: 1), "Sauce", in: app)
        featureTap(app.buttons.matching(identifier: "Move group up").element(boundBy: 1), in: app)
        XCTAssertEqual(app.textFields["Group heading"].firstMatch.value as? String, "Sauce")
        featureTap(app.buttons.matching(identifier: "Remove group").element(boundBy: 1), in: app)
        featureTap(app.buttons["Duplicate ingredient 1"], in: app)
        featureEnter(app.textFields["Quantity 2"], "1 1/2", in: app, replace: true)
        featureTap(app.buttons.matching(identifier: "Preparation notes and group").element(boundBy: 1), in: app)
        featureEnter(app.textFields["Preparation notes"], "rinsed", in: app)
        let groupPicker = app.buttons.matching(NSPredicate(format: "identifier BEGINSWITH %@", "recipe-group-")).firstMatch
        featureReveal(groupPicker, in: app, aboveKeyboard: true)
        featureTap(groupPicker, in: app)
        featureTap(app.buttons["Sauce"], in: app)
        featureTap(app.descendants(matching: .any).matching(NSPredicate(format: "identifier BEGINSWITH %@", "recipe-unit-")).element(boundBy: 1), in: app)
        featureTap(app.buttons["g"], in: app)
        featureTap(app.buttons["Move ingredient 2 up"], in: app)
        XCTAssertEqual(app.textFields["Quantity 1"].value as? String, "1 1/2")
        featureTap(app.buttons["Remove ingredient 2"], in: app)
        featureTap(app.buttons["Add ingredient"], in: app)
        featureTap(app.buttons["Remove ingredient 2"], in: app)
        featureTap(app.buttons["Save recipe"], in: app)
        closed("Edit recipe", in: app)
        let (saved, _) = try await api.read("/api/v1/recipes/" + id)
        let groups = try XCTUnwrap(saved["groups"] as? [[String: Any]])
        XCTAssertEqual(groups.count, 1); XCTAssertEqual(groups.first?["title"] as? String, "Sauce")
        let lines = try XCTUnwrap(saved["ingredients"] as? [[String: Any]])
        XCTAssertEqual(lines.count, 1)
        XCTAssertEqual(lines.first?["quantityText"] as? String, "1 1/2")
        XCTAssertEqual(lines.first?["preparation"] as? String, "rinsed")
        XCTAssertEqual(lines.first?["unit"] as? String, "g")
        XCTAssertEqual(lines.first?["groupId"] as? String, groups.first?["id"] as? String)
    }

    @MainActor func testCustomSlotsOtherMealsMoveDuplicateAndDelete() async throws {
        let (api, app) = try await fixture()
        featureTap(app.tabBars.buttons["Meal Planner"], in: app)
        featureTap(app.buttons["Meal slots"], in: app)
        featureEnter(app.textFields["Slot name"], "Tea", in: app)
        featureTap(app.buttons["Save slot"], in: app)
        featureTap(app.buttons["Rename Tea"], in: app)
        featureEnter(app.textFields["Slot name"], "Snack", in: app, replace: true)
        featureTap(app.buttons["Save slot"], in: app)
        featureTap(app.buttons["Move Snack earlier"], in: app)
        featureTap(app.buttons["Done"], in: app)
        featureTap(app.buttons["planner-day-0"], in: app)
        featureTap(app.buttons["planner-add-Snack"], in: app)
        featureTap(app.buttons["Other meal"], in: app)
        featureEnter(app.textFields["Meal description"], "Takeaway parity meal", in: app)
        featureTap(app.buttons["meal-save"], in: app); closed("Other meal", in: app)
        let path = try await weekPath(api)
        let (first, _) = try await api.read(path)
        let original = try XCTUnwrap((first["items"] as? [[String: Any]])?.first { $0["label"] as? String == "Takeaway parity meal" })
        let id = try XCTUnwrap(original["id"] as? String)
        XCTAssertEqual(original["type"] as? String, "other")
        XCTAssertNil(original["cookingEvent"] as? [String: Any]); XCTAssertNil(original["leftovers"] as? [String: Any])
        featureTap(app.buttons["planner-meal-" + id], in: app)
        chooseSlot("Breakfast", app)
        featureTap(app.buttons["Move meal to selected date and slot"], in: app); closed("Other meal", in: app)
        featureTap(app.buttons["planner-meal-" + id], in: app)
        chooseSlot("Lunch", app)
        featureTap(app.buttons["Duplicate meal to selected date and slot"], in: app); closed("Other meal", in: app)
        let (duplicated, _) = try await api.read(path)
        let meals = try XCTUnwrap(duplicated["items"] as? [[String: Any]])
        XCTAssertEqual(meals.filter { $0["label"] as? String == "Takeaway parity meal" }.count, 2)
        let copy = try XCTUnwrap(meals.first { $0["id"] as? String != id && $0["label"] as? String == "Takeaway parity meal" })
        let copyId = try XCTUnwrap(copy["id"] as? String)
        XCTAssertNotEqual(copy["slotId"] as? String, (meals.first { $0["id"] as? String == id })?["slotId"] as? String)
        featureTap(app.buttons["planner-meal-" + copyId], in: app)
        featureEnter(app.textFields["Meal description"], "Edited takeaway copy", in: app, replace: true)
        featureTap(app.buttons["meal-save"], in: app); closed("Other meal", in: app)
        for mealId in [id, copyId] {
            featureTap(app.buttons["planner-meal-" + mealId], in: app)
            featureTap(app.buttons["Delete meal"], in: app); featureConfirm("Delete meal", in: app)
            closed("Other meal", in: app)
        }
        let (empty, _) = try await api.read(path)
        XCTAssertFalse((empty["items"] as? [[String: Any]] ?? []).contains { [id, copyId].contains($0["id"] as? String ?? "") })
        featureTap(app.buttons["Meal slots"], in: app)
        featureTap(app.buttons["Remove Snack"], in: app); featureConfirm("Remove slot", in: app)
        gone(app.buttons["Rename Snack"])
        let (slots, _) = try await api.read("/api/v1/meal-slots")
        XCTAssertFalse((slots["items"] as? [[String: Any]] ?? []).contains { $0["name"] as? String == "Snack" })
    }

    @MainActor func testSelectedRecipeShoppingRemovalRestoreAndLifecycle() async throws {
        let (api, app) = try await fixture()
        let ingredient = try await api.write("POST", "/api/v1/ingredients", body: ["canonicalName": "Parity shopping rice"], expected: 201)
        let recipeId = try await api.seedRecipe(ingredientId: XCTUnwrap(ingredient["id"] as? String), title: "Selected parity recipe")
        featureTap(app.tabBars.buttons["Shopping Lists"], in: app)
        featureTap(app.buttons["shopping-new-list"], in: app)
        featureEnter(app.textFields["shopping-list-name"], "Selected parity shopping", in: app)
        featureTap(app.descendants(matching: .any).matching(identifier: "shopping-source-kind").firstMatch, in: app)
        featureTap(app.buttons["Selected recipes"], in: app)
        featureEnter(app.textFields["shopping-recipe-query"], "Selected parity recipe", in: app)
        featureTap(app.buttons["Search recipes"], in: app)
        featureTap(app.buttons["Add Selected parity recipe"], in: app)
        featureEnter(app.textFields["Servings for Selected parity recipe"], "3", in: app, replace: true)
        featureTap(app.buttons["shopping-create"], in: app); closed("Create shopping list", in: app)
        let (index, _) = try await api.read("/api/v1/shopping-lists")
        let listId = try XCTUnwrap((index["items"] as? [[String: Any]])?.first { $0["name"] as? String == "Selected parity shopping" }?["id"] as? String)
        let (list, _) = try await api.read("/api/v1/shopping-lists/" + listId)
        let row = try XCTUnwrap((list["items"] as? [[String: Any]])?.first)
        XCTAssertEqual(row["displayText"] as? String, "1.5 item")
        XCTAssertEqual(((row["contributions"] as? [[String: Any]])?.first)?["sourceRecipeId"] as? String, recipeId)
        featureTap(app.buttons["Remove Parity shopping rice"], in: app)
        gone(app.buttons["Remove Parity shopping rice"])
        let (removed, _) = try await api.read("/api/v1/shopping-lists/" + listId)
        XCTAssertEqual((removed["items"] as? [[String: Any]])?.first?["removed"] as? Bool, true)
        let show = app.switches["Show removed generated items"]; featureReveal(show, in: app)
        show.coordinate(withNormalizedOffset: CGVector(dx: 0.9, dy: 0.5)).tap()
        featureTap(app.buttons["Restore Parity shopping rice"], in: app)
        gone(app.buttons["Restore Parity shopping rice"])
        featureTap(app.buttons["Rename list"], in: app)
        featureEnter(app.textFields["List name"], "Renamed parity shopping", in: app, replace: true)
        featureTap(app.buttons["Save list name"], in: app); closed("Rename list", in: app)
        let (renamed, _) = try await api.read("/api/v1/shopping-lists/" + listId)
        XCTAssertEqual(renamed["name"] as? String, "Renamed parity shopping")
        XCTAssertEqual((renamed["items"] as? [[String: Any]])?.first?["removed"] as? Bool, false)
        featureTap(app.buttons["Delete list"], in: app); featureConfirm("Delete shopping list", in: app)
        gone(app.buttons["shopping-list-" + listId])
        _ = try await api.read("/api/v1/shopping-lists/" + listId, expected: 404)
    }

    @MainActor private func gone(_ element: XCUIElement) {
        XCTAssertEqual(XCTWaiter.wait(for: [XCTNSPredicateExpectation(predicate: NSPredicate(format: "exists == false"), object: element)], timeout: 15), .completed)
    }
    @MainActor private func chooseSlot(_ name: String, _ app: XCUIApplication) {
        featureTap(app.descendants(matching: .any).matching(identifier: "meal-slot").firstMatch, in: app)
        featureTap(app.buttons[name], in: app)
    }
    @MainActor private func closed(_ title: String, in app: XCUIApplication) {
        XCTAssertEqual(XCTWaiter.wait(for: [XCTNSPredicateExpectation(predicate: NSPredicate(format: "exists == false"), object: app.navigationBars[title])], timeout: 15), .completed)
    }
    @MainActor private func weekPath(_ api: NativeFixtureAPI) async throws -> String {
        let (settings, _) = try await api.read("/api/v1/household/settings")
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = try XCTUnwrap(TimeZone(identifier: try XCTUnwrap(settings["timezone"] as? String)))
        let today = calendar.startOfDay(for: Date())
        let monday = try XCTUnwrap(calendar.date(byAdding: .day, value: -((calendar.component(.weekday, from: today) + 5) % 7), to: today))
        let format = DateFormatter(); format.locale = Locale(identifier: "en_US_POSIX"); format.timeZone = calendar.timeZone; format.dateFormat = "yyyy-MM-dd"
        return "/api/v1/meals?from=\(format.string(from: monday))&to=\(format.string(from: try XCTUnwrap(calendar.date(byAdding: .day, value: 6, to: monday))))"
    }
}
