import XCTest

final class NativeSyncUITests: XCTestCase {
    @MainActor func testForegroundShoppingUpdatesDirtyDraftAndBackgroundRecovery() async throws {
        guard ProcessInfo.processInfo.environment["RECIPE_MANAGER_RUN_E2E"] == "1" else { throw XCTSkip("Run the isolated native fixture with NativeSyncUITests.") }
        continueAfterFailure = false
        let api = try NativeFixtureAPI(); try await api.login()
        let list = try await api.write("POST", "/api/v1/shopping-lists", body: ["name": "Live household shopping"], expected: 201)
        let id = try XCTUnwrap(list["id"] as? String), path = "/api/v1/shopping-lists/" + (try XCTUnwrap(list["id"] as? String))
        let (_, version) = try await api.read(path)
        let populated = try await api.write("POST", path + "/items", body: ["label": "Live bread", "quantityText": "1", "kind": "numeric", "unit": "item"], etag: version.value(forHTTPHeaderField: "ETag"), expected: 201)
        let itemId = try XCTUnwrap((populated["items"] as? [[String: Any]])?.first?["id"] as? String)
        let app = XCUIApplication(); app.launch()
        XCTAssertTrue(app.tabBars.buttons["Shopping Lists"].waitForExistence(timeout: 20)); app.tabBars.buttons["Shopping Lists"].tap()
        featureTap(app.buttons["shopping-list-" + id], in: app)
        let purchased = app.switches["shopping-purchased-" + itemId]; featureReveal(purchased, in: app)
        XCTAssertEqual(purchased.value as? String, "0")
        let (_, current) = try await api.read(path)
        _ = try await api.write("PUT", path + "/items/" + itemId, body: ["purchased": true], etag: current.value(forHTTPHeaderField: "ETag"), expected: 200)
        accepted(purchased, "1", timeout: 2)

        featureTap(app.buttons["shopping-add-manual"], in: app)
        featureEnter(app.textFields["shopping-item-name"], "Retain this draft", in: app)
        let (_, updated) = try await api.read(path)
        _ = try await api.write("PUT", path + "/items/" + itemId, body: ["purchased": false], etag: updated.value(forHTTPHeaderField: "ETag"), expected: 200)
        XCTAssertTrue(app.staticTexts["The saved list changed. Your input is retained."].waitForExistence(timeout: 3))
        XCTAssertEqual(app.textFields["shopping-item-name"].value as? String, "Retain this draft")
        featureTap(app.navigationBars["Add manual item"].buttons["Cancel"], in: app)
        featureConfirm("Discard changes", in: app)
        featureTap(app.buttons["Refresh list"], in: app); featureReveal(purchased, in: app); accepted(purchased, "0", timeout: 5)

        XCUIDevice.shared.press(.home)
        let (_, backgroundVersion) = try await api.read(path)
        _ = try await api.write("PUT", path + "/items/" + itemId, body: ["purchased": true], etag: backgroundVersion.value(forHTTPHeaderField: "ETag"), expected: 200)
        app.activate()
        accepted(purchased, "1", timeout: 2)
        let (final, _) = try await api.read("/api/v1/shopping-lists/" + id)
        XCTAssertEqual((final["items"] as? [[String: Any]])?.count, 1)
    }
    @MainActor private func accepted(_ element: XCUIElement, _ value: String, timeout: TimeInterval) {
        XCTAssertEqual(XCTWaiter.wait(for: [XCTNSPredicateExpectation(predicate: NSPredicate(format: "value == %@", value), object: element)], timeout: timeout), .completed)
    }
}
