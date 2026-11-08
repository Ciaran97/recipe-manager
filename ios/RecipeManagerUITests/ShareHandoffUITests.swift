import XCTest

final class ShareHandoffUITests: XCTestCase {
    @MainActor func testSignedURLTextAndMixedCaptureWithCancellation() throws {
        guard ProcessInfo.processInfo.environment["RECIPE_MANAGER_RUN_E2E"] == "1" else { throw XCTSkip("Run the signed ShareHandoffUITests fixture.") }
        continueAfterFailure = false
        let main = XCUIApplication(); main.launch()
        XCTAssertTrue(main.tabBars.buttons["Settings"].waitForExistence(timeout: 20)); main.tabBars.buttons["Settings"].tap()
        featureTap(main.buttons["Sign out all sessions"], in: main)
        XCTAssertTrue(main.buttons["auth-mode"].waitForExistence(timeout: 20)); main.terminate()
        let identifier = ProcessInfo.processInfo.environment["RECIPE_MANAGER_E2E_APP_BUNDLE_ID"] ?? "local.recipemanager.app"
        let host = XCUIApplication(bundleIdentifier: identifier + ".ShareHost")
        host.launch()
        XCTAssertTrue(host.buttons["fixture-share-url"].waitForExistence(timeout: 15))
        XCTAssertFalse(host.staticTexts["fixture-error"].exists)
        XCTAssertEqual(host.staticTexts["fixture-capture-count"].label, "Pending captures: 0")
        capture("fixture-share-url", kind: "Recipe URL:", in: host)
        count(1, in: host)
        XCTAssertTrue(host.staticTexts.matching(NSPredicate(format: "label CONTAINS %@", "https://recipes.example.test/shared-recipe")).firstMatch.exists)
        capture("fixture-share-text", kind: "Recipe text:", in: host)
        count(2, in: host)
        XCTAssertTrue(host.staticTexts.matching(NSPredicate(format: "label CONTAINS %@", "1/3 cup rice")).firstMatch.exists)
        present("fixture-share-mixed", in: host)
        featureTap(host.buttons["share-cancel"], in: host)
        closeSheetIfNeeded(host)
        count(2, in: host)
        capture("fixture-share-mixed", kind: "Recipe text:", in: host)
        count(3, in: host)
    }
    @MainActor private func present(_ button: String, in host: XCUIApplication) {
        featureTap(host.buttons[button], in: host)
        let target = host.buttons["Recipe Manager"]
        if !target.waitForExistence(timeout: 3) {
            let more = host.buttons["More"]
            if more.exists { more.tap() }
        }
        let manager = host.buttons["Recipe Manager"].exists ? host.buttons["Recipe Manager"] : host.staticTexts["Recipe Manager"]
        featureTap(manager, in: host)
        XCTAssertTrue(host.buttons["share-save"].waitForExistence(timeout: 15))
    }
    @MainActor private func capture(_ button: String, kind: String, in host: XCUIApplication) {
        present(button, in: host)
        let picker = host.buttons["share-candidate"]
        if picker.exists {
            featureTap(picker, in: host)
            featureTap(host.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", kind)).firstMatch, in: host)
        }
        featureTap(host.buttons["share-save"], in: host)
        closeSheetIfNeeded(host)
    }
    @MainActor private func closeSheetIfNeeded(_ host: XCUIApplication) {
        if !host.buttons["fixture-share-url"].waitForExistence(timeout: 5), host.buttons["Close"].exists { host.buttons["Close"].tap() }
    }
    @MainActor private func count(_ expected: Int, in host: XCUIApplication) {
        XCTAssertEqual(XCTWaiter.wait(for: [XCTNSPredicateExpectation(predicate: NSPredicate(format: "label == %@", "Pending captures: \(expected)"), object: host.staticTexts["fixture-capture-count"])], timeout: 10), .completed)
    }
}
