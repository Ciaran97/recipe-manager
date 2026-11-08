import XCTest

final class AuthenticationUITests: XCTestCase {
    @MainActor func testNativeSetupInvitationAndSharedProfile() throws {
        let environment = ProcessInfo.processInfo.environment
        guard environment["RECIPE_MANAGER_RUN_E2E"] == "1" else {
            throw XCTSkip("Requires the isolated HTTPS fixture; run scripts/test-ios-households.sh.")
        }
        let origin = try XCTUnwrap(environment["RECIPE_MANAGER_E2E_ORIGIN"])
        let setupSecret = try XCTUnwrap(environment["RECIPE_MANAGER_E2E_SETUP_TOKEN"])
        let adminPassword = environment["RECIPE_MANAGER_E2E_ADMIN_PASSWORD"] ?? "e2e-admin-test-passphrase"
        let memberPassword = environment["RECIPE_MANAGER_E2E_MEMBER_PASSWORD"] ?? "e2e-member-test-passphrase"
        continueAfterFailure = false
        let app = XCUIApplication()
        app.launch()
        let serverField = app.textFields["Server HTTPS address"]
        let changingServer = !serverField.waitForExistence(timeout: 3)
        if changingServer { tap(app.buttons["Server"], app) }
        if let current = serverField.value as? String, current.hasPrefix("https://") {
            serverField.tap(); serverField.typeText(String(repeating: XCUIKeyboardKey.delete.rawValue, count: current.count))
        }
        enter(serverField, origin, app)
        tap(app.buttons["Use server"], app)
        if app.buttons["Discard and change server"].waitForExistence(timeout: 2) { tap(app.buttons["Discard and change server"], app) }
        if changingServer { tap(app.buttons["Done"], app) }
        let permission = XCUIApplication(bundleIdentifier: "com.apple.springboard").alerts.firstMatch
        if permission.waitForExistence(timeout: 3), permission.staticTexts.matching(NSPredicate(format: "label CONTAINS[c] %@", "local network")).firstMatch.exists {
            permission.buttons["Allow"].tap()
            if app.buttons["Use server"].waitForExistence(timeout: 2) { tap(app.buttons["Use server"], app) }
        }
        if app.buttons["Retry server connection"].waitForExistence(timeout: 2) { tap(app.buttons["Retry server connection"], app) }
        XCTAssertTrue(app.textFields["household-name"].waitForExistence(timeout: 20))
        enter(app.textFields["household-name"], "Native household", app)
        enter(app.secureTextFields["setup-token"], setupSecret, app)
        enter(app.textFields["email"], "admin@example.test", app)
        enter(app.secureTextFields["password"], adminPassword, app)
        enter(app.secureTextFields["confirm-password"], adminPassword, app)
        tap(app.buttons["submit-auth"], app)
        assertAuthenticated(app)
        XCTAssertTrue(app.tabBars.buttons["Settings"].waitForExistence(timeout: 20))
        app.tabBars.buttons["Settings"].tap()
        tap(app.buttons["Create invitation"], app)
        tap(app.buttons["Share invitation"], app)
        let copy = app.staticTexts["Copy"]
        if !copy.waitForExistence(timeout: 3), app.staticTexts["View More"].waitForExistence(timeout: 3) {
            tap(app.staticTexts["View More"], app)
        }
        XCTAssertTrue(copy.waitForExistence(timeout: 10))
        tap(copy, app)
        XCTAssertEqual(XCTWaiter.wait(for: [XCTNSPredicateExpectation(predicate: NSPredicate(format: "exists == false"), object: copy)], timeout: 10), .completed)
        tap(app.buttons["Sign out all sessions"], app)
        XCTAssertTrue(app.buttons["auth-mode"].waitForExistence(timeout: 20))
        app.buttons["auth-mode"].tap()
        if app.buttons["Join household"].exists { app.buttons["Join household"].tap() }
        else { app.staticTexts["Join household"].tap() }
        tap(app.buttons["Paste"], app)
        let invitation = app.secureTextFields["invitation-token"]
        let pasted = NSPredicate(format: "value != %@ AND value != ''", "Invitation link or token")
        XCTAssertEqual(XCTWaiter.wait(for: [XCTNSPredicateExpectation(predicate: pasted, object: invitation)], timeout: 5), .completed, "The shared invitation URL must populate the review field.")
        enter(app.textFields["Your name (optional)"], "Native member", app)
        enter(app.textFields["email"], "member@example.test", app)
        enter(app.secureTextFields["password"], memberPassword, app)
        enter(app.secureTextFields["confirm-password"], memberPassword, app)
        tap(app.buttons["submit-auth"], app)
        assertAuthenticated(app)
        XCTAssertTrue(app.tabBars.buttons["Settings"].waitForExistence(timeout: 20))
        app.tabBars.buttons["Settings"].tap()
        let name = app.textFields["profile-name"]
        XCTAssertTrue(name.waitForExistence(timeout: 20))
        name.tap()
        name.typeText(String(repeating: XCUIKeyboardKey.delete.rawValue, count: "Native member".count))
        name.typeText("Updated on iOS")
        tap(app.buttons["Save profile"], app)
        XCTAssertTrue(app.staticTexts["Profile saved."].waitForExistence(timeout: 10))
        XCTAssertFalse(app.buttons["Create invitation"].exists)
        let screenshot = XCTAttachment(screenshot: app.screenshot())
        screenshot.name = "Native member settings"; screenshot.lifetime = .keepAlways; add(screenshot)
    }

    @MainActor private func assertAuthenticated(_ app: XCUIApplication) {
        let authenticated = app.tabBars.buttons["Settings"]
        let error = app.staticTexts["auth-error"]
        let completed = NSPredicate { _, _ in authenticated.exists || error.exists }
        XCTAssertEqual(XCTWaiter.wait(for: [XCTNSPredicateExpectation(predicate: completed, object: nil)], timeout: 20), .completed)
        if error.exists { XCTFail(error.label) }
        XCTAssertTrue(authenticated.exists)
    }

    @MainActor private func tap(_ element: XCUIElement, _ app: XCUIApplication) {
        reveal(element, app)
        XCTAssertTrue(element.exists)
        XCTAssertEqual(XCTWaiter.wait(for: [XCTNSPredicateExpectation(predicate: NSPredicate(format: "enabled == true"), object: element)], timeout: 10), .completed)
        element.tap()
    }
    @MainActor private func enter(_ field: XCUIElement, _ value: String, _ app: XCUIApplication) {
        reveal(field, app)
        XCTAssertTrue(field.exists)
        field.tap()
        if app.staticTexts["Use Strong Password?"].waitForExistence(timeout: 1) {
            app.buttons["Close"].tap()
        }
        if !app.keyboards.firstMatch.waitForExistence(timeout: 3) { field.tap() }
        if app.staticTexts["Use Strong Password?"].exists { app.buttons["Close"].tap() }
        XCTAssertTrue(app.keyboards.firstMatch.waitForExistence(timeout: 5), "The input keyboard must be ready before typing.")
        field.typeText(value)
        let filled = NSPredicate(format: "value != %@ AND value != ''", field.placeholderValue ?? "")
        XCTAssertEqual(XCTWaiter.wait(for: [XCTNSPredicateExpectation(predicate: filled, object: field)], timeout: 3), .completed, "The field must retain the entered value.")
    }
    @MainActor private func reveal(_ element: XCUIElement, _ app: XCUIApplication) {
        for _ in 0..<10 {
            if element.exists && element.isHittable { return }
            let start = app.coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: 0.45))
            let end = app.coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: 0.18))
            start.press(forDuration: 0.05, thenDragTo: end)
        }
    }
}
