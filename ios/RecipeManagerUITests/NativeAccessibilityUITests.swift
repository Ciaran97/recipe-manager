import XCTest

final class NativeAccessibilityUITests: XCTestCase {
    @MainActor func testCoreTabsPassNativeAccessibilityAudits() async throws {
        guard ProcessInfo.processInfo.environment["RECIPE_MANAGER_RUN_E2E"] == "1" else {
            throw XCTSkip("Run scripts/test-ios-households.sh NativeAccessibilityUITests against its isolated signed fixture.")
        }
        continueAfterFailure = true
        let api = try NativeFixtureAPI()
        try await api.login()
        let ingredient = try await api.ingredient(named: "Rice")
        let title = "Accessibility fixture " + String(UUID().uuidString.prefix(8))
        let recipeId = try await api.seedRecipe(ingredientId: XCTUnwrap(ingredient["id"] as? String), title: title)
        let app = XCUIApplication()
        app.launch()
        XCTAssertTrue(app.tabBars.buttons["Recipes"].waitForExistence(timeout: 20))
        for tab in ["Recipes", "Meal Planner", "Shopping Lists", "Settings"] {
            featureTap(app.tabBars.buttons[tab], in: app)
            XCTAssertTrue(app.navigationBars.firstMatch.waitForExistence(timeout: 15))
            let ready: XCUIElement
            switch tab {
            case "Recipes": ready = app.buttons["Apply filters"]
            case "Meal Planner": ready = app.buttons["planner-day-0"]
            case "Shopping Lists": ready = app.buttons["shopping-new-list"]
            default: ready = app.buttons["Save profile"]
            }
            XCTAssertTrue(ready.waitForExistence(timeout: 20))
            XCTAssertEqual(XCTWaiter.wait(for: [XCTNSPredicateExpectation(predicate: NSPredicate(format: "enabled == true"), object: ready)], timeout: 20), .completed,
                "Audit settled, usable content rather than an in-flight disabled form.")
            if tab == "Recipes" { XCTAssertTrue(app.buttons["recipe-library-" + recipeId].waitForExistence(timeout: 15)) }
            let screenshot = XCTAttachment(screenshot: app.screenshot())
            screenshot.name = "Accessibility-" + tab
            screenshot.lifetime = .keepAlways
            add(screenshot)
            var finding = 0
            try app.performAccessibilityAudit { issue in
                finding += 1
                var details = issue.compactDescription + "\n" + issue.detailedDescription
                if let element = issue.element, element.exists {
                    details += "\n" + element.debugDescription
                }
                let report = XCTAttachment(string: details)
                report.name = "Accessibility-" + tab + "-finding-" + String(finding)
                report.lifetime = .keepAlways
                self.add(report)
                return false // Record every issue; no audit type or finding is waived.
            }
        }
    }
}
