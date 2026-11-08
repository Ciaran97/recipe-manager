import XCTest

/// Physical-device prerequisite; selected only by the owned LAN test runner.
final class AccessPermissionUITests: XCTestCase {
    @MainActor func testLocalNetworkAccessForOwnedClients() async throws {
        guard ProcessInfo.processInfo.environment["RECIPE_MANAGER_PHYSICAL_E2E"] == "1" else {
            throw XCTSkip("Requires the designated physical phone and owned LAN deployment.")
        }
        continueAfterFailure = false
        // A background UI-test runner cannot present its first Local Network permission prompt.
        // Bring only that owned runner forward before the request; use the normal OS Allow action.
        let identifier = try XCTUnwrap(Bundle.main.bundleIdentifier)
        XCTAssertTrue(identifier.hasSuffix(".xctrunner"))
        XCUIApplication(bundleIdentifier: identifier).activate()
        let api = try NativeFixtureAPI()
        let probe = Task<Void, Error> { _ = try await api.read("/api/v1/setup") }
        await Task.yield()
        let permission = XCUIApplication(bundleIdentifier: "com.apple.springboard").alerts.firstMatch
        if permission.waitForExistence(timeout: 5), permission.staticTexts.matching(NSPredicate(format: "label CONTAINS[c] %@", "local network")).firstMatch.exists {
            permission.buttons["Allow"].tap()
        }
        _ = try? await probe.value
        // A new session verifies that the decision permits real HTTPS, rather than trusting a flag.
        let verified = try NativeFixtureAPI()
        _ = try await verified.read("/api/v1/setup")
        XCUIApplication().activate()
    }
}
