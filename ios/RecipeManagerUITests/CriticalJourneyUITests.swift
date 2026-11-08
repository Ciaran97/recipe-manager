import XCTest

final class CriticalJourneyUITests: XCTestCase {
    @MainActor func testANativeShareToAngularAndBack() async throws { try await journey(origin: "native", weekOffset: 0) }
    @MainActor func testBWebImportToNativeAndBack() async throws { try await journey(origin: "web", weekOffset: 7) }

    @MainActor private func journey(origin: String, weekOffset: Int) async throws {
        let environment = ProcessInfo.processInfo.environment
        guard environment["RECIPE_MANAGER_RUN_E2E"] == "1" else { throw XCTSkip("Run the signed CriticalJourneyUITests fixture with its Angular controller.") }
        continueAfterFailure = false
        let api = try NativeFixtureAPI(); try await api.login()
        let (settings, version) = try await api.read("/api/v1/household/settings")
        _ = try await api.write("PUT", "/api/v1/household/settings", body: ["name": try XCTUnwrap(settings["name"]), "timezone": try XCTUnwrap(settings["timezone"]),
            "ai": ["enabled": true, "provider": "OpenAICompatible", "baseUrl": try XCTUnwrap(environment["RECIPE_MANAGER_E2E_AI_ORIGIN"]), "model": "deterministic-fixture", "clearApiKey": false]], etag: version.value(forHTTPHeaderField: "ETag"), expected: 200)
        for (path, property, names) in [("ingredients", "canonicalName", ["Rice", "Salt"]), ("collections", "name", ["Weeknight"]), ("tags", "name", ["Comfort"])] {
            let (page, _) = try await api.read("/api/v1/" + path + "?limit=100")
            let existing = (page["items"] as? [[String: Any]] ?? []).compactMap { $0[property] as? String }
            for name in names where !existing.contains(name) { _ = try await api.write("POST", "/api/v1/" + path, body: [property: name], expected: 201) }
        }
        var calendar = Calendar(identifier: .gregorian); calendar.timeZone = try XCTUnwrap(TimeZone(identifier: try XCTUnwrap(settings["timezone"] as? String)))
        let today = calendar.startOfDay(for: Date())
        let monday = try XCTUnwrap(calendar.date(byAdding: .day, value: -((calendar.component(.weekday, from: today) + 5) % 7) + weekOffset, to: today))
        let formatter = DateFormatter(); formatter.locale = Locale(identifier: "en_US_POSIX"); formatter.timeZone = calendar.timeZone; formatter.dateFormat = "yyyy-MM-dd"
        let from = formatter.string(from: monday), to = formatter.string(from: try XCTUnwrap(calendar.date(byAdding: .day, value: 2, to: monday)))
        let title = origin == "native" ? "Native critical rice" : "Web critical rice"
        let app = XCUIApplication(); app.launch(); XCTAssertTrue(app.tabBars.buttons["Recipes"].waitForExistence(timeout: 20))
        var recipeId: String?
        if origin == "native" {
            try await shareAndReview(app, title: title, environment: environment)
            let (library, _) = try await api.read("/api/v1/recipes")
            recipeId = try XCTUnwrap((library["items"] as? [[String: Any]])?.first { $0["title"] as? String == title }?["id"] as? String)
        }
        var begin: [String: Any] = ["origin": origin, "from": from, "to": to]
        if let recipeId { begin["recipeId"] = recipeId }
        let created = try await control("begin", fields: begin)
        let sourceRecipeId = try XCTUnwrap(created["recipeId"] as? String), wednesdayId = try XCTUnwrap(created["wednesdayId"] as? String)
        if origin == "web" {
            featureTap(app.tabBars.buttons["Recipes"], in: app); featureTap(app.buttons[title], in: app); featureTap(app.buttons["Edit recipe"], in: app)
            featureEnter(app.textFields["Notes"], "Reviewed on iOS", in: app)
            set(app.switches["I have reviewed unresolved or uncertain ingredient quantities"], to: "1", in: app)
            featureTap(app.buttons["Save recipe"], in: app)
            closed("Edit recipe", app)
        }
        featureTap(app.tabBars.buttons["Meal Planner"], in: app)
        if weekOffset != 0 { featureTap(app.buttons["Next week"], in: app) }
        XCTAssertTrue(app.staticTexts["Week of " + from].waitForExistence(timeout: 15))
        featureTap(app.buttons["planner-day-0"], in: app)
        try await cook(app, recipeId: sourceRecipeId, prepared: "4")
        let calendarPath = "/api/v1/meals?from=\(from)&to=\(to)"
        let (mondayPlan, _) = try await api.read(calendarPath)
        let mondayMeal = try XCTUnwrap((mondayPlan["items"] as? [[String: Any]])?.first { $0["date"] as? String == from })
        let mealId = try XCTUnwrap(mondayMeal["id"] as? String), event = try XCTUnwrap(mondayMeal["cookingEvent"] as? [String: Any])
        XCTAssertEqual((mondayPlan["items"] as? [Any])?.count, 1)
        XCTAssertEqual(event["plannedPrepared"] as? String, "4"); XCTAssertEqual(event["plannedEaten"] as? String, "2"); XCTAssertNil(event["actualPrepared"] as? String)
        featureTap(app.buttons["planner-day-1"], in: app); featureTap(app.buttons["planner-add-Dinner"], in: app); featureTap(app.buttons["Eat leftovers"], in: app)
        featureTap(app.buttons["leftovers-source-" + (try XCTUnwrap(event["id"] as? String))], in: app)
        featureEnter(app.textFields["Planned leftover portions"], "2", in: app, replace: true); featureTap(app.buttons["leftovers-save"], in: app); closed("Eat leftovers", app)
        featureTap(app.buttons["planner-day-2"], in: app); try await cook(app, recipeId: wednesdayId, prepared: "2")
        let shopping = try await control("shopping")
        let listId = try XCTUnwrap(shopping["listId"] as? String), itemId = try XCTUnwrap(shopping["itemId"] as? String)
        featureTap(app.tabBars.buttons["Shopping Lists"], in: app); featureTap(app.buttons["shopping-list-" + listId], in: app)
        set(app.switches["shopping-purchased-" + itemId], to: "1", in: app)
        featureTap(app.buttons["shopping-edit-" + itemId], in: app); featureEnter(app.textFields["shopping-quantity"], "600", in: app, replace: true); featureTap(app.buttons["shopping-save-item"], in: app); closed("Edit shopping item", app)
        featureTap(app.buttons["shopping-add-manual"], in: app); featureEnter(app.textFields["shopping-item-name"], "Bread", in: app)
        featureTap(app.descendants(matching: .any).matching(identifier: "shopping-quantity-kind").firstMatch, in: app); featureTap(app.buttons["Known quantity"], in: app)
        featureEnter(app.textFields["shopping-quantity"], "1", in: app); featureTap(app.buttons["shopping-save-manual"], in: app); closed("Add manual item", app)
        _ = try await control("observe-and-edit")
        let (frozen, _) = try await api.read("/api/v1/meals/" + mealId)
        let snapshot = try XCTUnwrap((frozen["cookingEvent"] as? [String: Any])?["recipeSnapshot"] as? [String: Any])
        XCTAssertEqual((snapshot["ingredients"] as? [[String: Any]])?.first?["quantityText"] as? String, "150")
        featureTap(app.tabBars.buttons["Meal Planner"], in: app); featureTap(app.buttons["planner-day-0"], in: app); featureTap(app.buttons["planner-meal-" + mealId], in: app)
        featureTap(app.buttons["Actual quantities, leftovers and recipe refresh"], in: app); featureTap(app.buttons["Review latest recipe"], in: app); featureTap(app.buttons["Compare saved and latest recipe"], in: app)
        let changed = app.staticTexts.matching(NSPredicate(format: "label CONTAINS %@", "175")).firstMatch; featureReveal(changed, in: app); XCTAssertTrue(changed.exists)
        featureTap(app.navigationBars["Recipe changes"].buttons.element(boundBy: 0), in: app); featureTap(app.buttons["Apply reviewed recipe refresh"], in: app); closed("Cooking and leftovers", app)
        featureTap(app.buttons["Cancel"], in: app); closed("Cook recipe", app)
        _ = try await control("reconcile")
        featureTap(app.tabBars.buttons["Shopping Lists"], in: app)
        let purchased = app.switches["shopping-purchased-" + itemId]; featureReveal(purchased, in: app); XCTAssertEqual(purchased.value as? String, "1")
        featureTap(app.buttons["Contributing recipes"].firstMatch, in: app)
        let contribution = app.staticTexts.matching(NSPredicate(format: "label CONTAINS %@", "175 g")).firstMatch; featureReveal(contribution, in: app); XCTAssertTrue(contribution.waitForExistence(timeout: 15))
        let (final, _) = try await api.read("/api/v1/shopping-lists/" + listId)
        let rows = try XCTUnwrap(final["items"] as? [[String: Any]]), overridden = try XCTUnwrap(rows.first { $0["id"] as? String == itemId })
        XCTAssertEqual((overridden["baselineExact"] as? [String: String])?["numerator"], "550"); XCTAssertEqual((overridden["effectiveExact"] as? [String: String])?["numerator"], "600"); XCTAssertEqual(overridden["purchased"] as? Bool, true)
        XCTAssertTrue(rows.contains { $0["label"] as? String == "Bread" && $0["manual"] as? Bool == true }); XCTAssertEqual((final["summary"] as? [String: Any])?["unresolvedCount"] as? Int, 1)
        _ = try await control("done")
        app.terminate()
    }

    @MainActor private func shareAndReview(_ app: XCUIApplication, title: String, environment: [String: String]) async throws {
        app.terminate()
        let host = XCUIApplication(bundleIdentifier: (environment["RECIPE_MANAGER_E2E_APP_BUNDLE_ID"] ?? "local.recipemanager.app") + ".ShareHost")
        host.launchEnvironment["RECIPE_MANAGER_SHARE_TEXT"] = try XCTUnwrap(environment["RECIPE_MANAGER_E2E_AI_TEXT"]); host.launch()
        featureTap(host.buttons["fixture-share-text"], in: host)
        if !host.buttons["Recipe Manager"].waitForExistence(timeout: 3), host.buttons["More"].exists { host.buttons["More"].tap() }
        featureTap(host.buttons["Recipe Manager"].exists ? host.buttons["Recipe Manager"] : host.staticTexts["Recipe Manager"], in: host)
        XCTAssertTrue(host.buttons["share-save"].waitForExistence(timeout: 15))
        if host.buttons["share-candidate"].exists { featureTap(host.buttons["share-candidate"], in: host); featureTap(host.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Recipe text:")).firstMatch, in: host) }
        featureTap(host.buttons["share-save"], in: host)
        if !host.buttons["fixture-share-text"].waitForExistence(timeout: 5), host.buttons["Close"].exists { host.buttons["Close"].tap() }
        app.activate(); featureTap(app.tabBars.buttons["Recipes"], in: app); featureTap(app.buttons["recipe-pending-captures"], in: app)
        featureTap(app.buttons.matching(NSPredicate(format: "identifier BEGINSWITH %@", "capture-review-")).firstMatch, in: app); featureTap(app.buttons["capture-confirm-destination"], in: app); featureTap(app.buttons["import-start"], in: app)
        XCTAssertTrue(app.navigationBars["Review imported recipe"].waitForExistence(timeout: 20))
        featureEnter(app.textFields["recipe-title"], title, in: app, replace: true); featureEnter(app.textFields["Quantity 1"], "150", in: app, replace: true)
        featureTap(app.descendants(matching: .any).matching(identifier: "recipe-measurement-standard").firstMatch, in: app); featureTap(app.buttons["Australian"], in: app)
        featureTap(app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Unit 1")).firstMatch, in: app); featureTap(app.buttons["g"], in: app)
        featureTap(app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Unit 2")).firstMatch, in: app); featureTap(app.buttons["Whole item"], in: app)
        featureTap(app.buttons["Choose collections and tags"], in: app)
        for name in ["Weeknight", "Comfort"] { set(app.switches[name], to: "1", in: app) }
        featureTap(app.buttons["Done"], in: app)
        set(app.switches["I have reviewed unresolved or uncertain ingredient quantities"], to: "1", in: app); featureTap(app.buttons["Save recipe"], in: app)
        XCTAssertTrue(app.staticTexts["No pending captures. Share a recipe URL or text to Recipe Manager from another application."].waitForExistence(timeout: 20))
    }
    @MainActor private func cook(_ app: XCUIApplication, recipeId: String, prepared: String) async throws {
        featureTap(app.buttons["planner-add-Dinner"], in: app); featureTap(app.buttons["Cook recipe"], in: app); featureTap(app.buttons["meal-recipe-" + recipeId], in: app)
        featureEnter(app.textFields["Portions to prepare"], prepared, in: app, replace: true); featureEnter(app.textFields["Portions to eat"], "2", in: app, replace: true); featureTap(app.buttons["meal-save"], in: app); closed("Cook recipe", app)
    }
    @MainActor private func set(_ element: XCUIElement, to value: String, in app: XCUIApplication) {
        featureReveal(element, in: app)
        if element.value as? String != value { element.coordinate(withNormalizedOffset: CGVector(dx: 0.9, dy: 0.5)).tap() }
        XCTAssertEqual(XCTWaiter.wait(for: [XCTNSPredicateExpectation(predicate: NSPredicate(format: "value == %@", value), object: element)], timeout: 15), .completed)
    }
    @MainActor private func closed(_ title: String, _ app: XCUIApplication) { XCTAssertEqual(XCTWaiter.wait(for: [XCTNSPredicateExpectation(predicate: NSPredicate(format: "exists == false"), object: app.navigationBars[title])], timeout: 20), .completed) }
    @MainActor private func control(_ command: String, fields: [String: Any] = [:]) async throws -> [String: Any] {
        var request = URLRequest(url: try XCTUnwrap(URL(string: "https://localhost:7291/step"))); request.httpMethod = "POST"; request.timeoutInterval = 60
        request.setValue(try XCTUnwrap(ProcessInfo.processInfo.environment["RECIPE_MANAGER_CROSS_TOKEN"]), forHTTPHeaderField: "X-Test-Control-Token"); request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        var body = fields; body["command"] = command; request.httpBody = try JSONSerialization.data(withJSONObject: body)
        let (data, response) = try await URLSession.shared.data(for: request); XCTAssertEqual((response as? HTTPURLResponse)?.statusCode, 200)
        return try XCTUnwrap(JSONSerialization.jsonObject(with: data) as? [String: Any])
    }
}
