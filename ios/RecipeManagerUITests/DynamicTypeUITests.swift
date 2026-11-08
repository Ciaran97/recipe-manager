import XCTest

final class DynamicTypeUITests: XCTestCase {
    @MainActor func testSystemTextSizeChangesRecipeTextAndCalendarTargetsStayUsable() async throws {
        let environment = ProcessInfo.processInfo.environment
        guard environment["RECIPE_MANAGER_RUN_E2E"] == "1" else {
            throw XCTSkip("Run the isolated signed fixture at normal and largest accessibility content sizes.")
        }
        continueAfterFailure = false
        let api = try NativeFixtureAPI(); try await api.login()
        let ingredient = try await api.write("POST", "/api/v1/ingredients", body: ["canonicalName": "Dynamic Type rice"], expected: 201)
        _ = try await api.seedRecipe(ingredientId: XCTUnwrap(ingredient["id"] as? String), title: "Scaling recipe")
        let app = XCUIApplication(); app.launch()
        XCTAssertTrue(app.tabBars.buttons["Recipes"].waitForExistence(timeout: 20))
        featureTap(app.tabBars.buttons["Recipes"], in: app)
        let title = app.staticTexts["Scaling recipe"].firstMatch
        featureReveal(title, in: app)
        XCTAssertTrue(title.isHittable)
        let size = try XCTUnwrap(environment["RECIPE_MANAGER_E2E_TEXT_SIZE"])
        if size == "accessibility-extra-extra-extra-large" { XCTAssertGreaterThan(title.frame.height, 40) }
        else if size == "large" { XCTAssertLessThan(title.frame.height, 40) }
        else { XCTFail("Use large or accessibility-extra-extra-extra-large for the controlled comparison.") }
        let metric = XCTAttachment(data: try JSONSerialization.data(withJSONObject: ["systemTextSize": size, "titleHeight": title.frame.height]), uniformTypeIdentifier: "public.json")
        metric.name = "system-text-size-measurement.json"; metric.lifetime = .keepAlways; add(metric)
        let screenshot = XCTAttachment(screenshot: app.screenshot()); screenshot.name = "System text size " + size; screenshot.lifetime = .keepAlways; add(screenshot)
        featureTap(app.tabBars.buttons["Meal Planner"], in: app)
        for label in ["Previous week", "Next week"] {
            let button = app.buttons[label]; featureReveal(button, in: app)
            XCTAssertGreaterThanOrEqual(button.frame.width, 44)
            XCTAssertGreaterThanOrEqual(button.frame.height, 44)
            featureTap(button, in: app)
        }
    }
}
