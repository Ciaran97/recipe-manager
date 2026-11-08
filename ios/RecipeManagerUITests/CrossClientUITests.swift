import XCTest

final class CrossClientUITests: XCTestCase {
    @MainActor func testNativeAndAngularShoppingWritersAndObservers() async throws {
        guard ProcessInfo.processInfo.environment["RECIPE_MANAGER_RUN_E2E"] == "1" else { throw XCTSkip("Run the signed CrossClientUITests fixture.") }
        continueAfterFailure = false
        let api = try NativeFixtureAPI(); try await api.login()
        let list = try await api.write("POST", "/api/v1/shopping-lists", body: ["name": "Cross-client shopping"], expected: 201)
        let id = try XCTUnwrap(list["id"] as? String), path = "/api/v1/shopping-lists/" + (try XCTUnwrap(list["id"] as? String))
        let (_, version) = try await api.read(path)
        let populated = try await api.write("POST", path + "/items", body: ["label": "Cross-client bread", "quantityText": "1", "kind": "numeric", "unit": "item"], etag: version.value(forHTTPHeaderField: "ETag"), expected: 201)
        let item = try XCTUnwrap((populated["items"] as? [[String: Any]])?.first?["id"] as? String)
        let app = XCUIApplication(); app.launch()
        XCTAssertTrue(app.tabBars.buttons["Shopping Lists"].waitForExistence(timeout: 20)); app.tabBars.buttons["Shopping Lists"].tap()
        featureTap(app.buttons["shopping-list-" + id], in: app)
        let purchased = app.switches["shopping-purchased-" + item]; featureReveal(purchased, in: app)
        try await step("open", list: id, item: item)
        purchased.coordinate(withNormalizedOffset: CGVector(dx: 0.9, dy: 0.5)).tap()
        accepted(purchased, "1", timeout: 5)
        try await step("expect", list: id, item: item, purchased: true)
        try await step("purchase", list: id, item: item, purchased: false)
        accepted(purchased, "0", timeout: 2)
        try await step("native-observed", list: id, item: item)
        let (final, _) = try await api.read(path)
        XCTAssertEqual((final["items"] as? [[String: Any]])?.first?["purchased"] as? Bool, false)
        try await step("done")
    }
    @MainActor private func accepted(_ element: XCUIElement, _ value: String, timeout: TimeInterval) {
        XCTAssertEqual(XCTWaiter.wait(for: [XCTNSPredicateExpectation(predicate: NSPredicate(format: "value == %@", value), object: element)], timeout: timeout), .completed)
    }
    @MainActor private func step(_ command: String, list: String? = nil, item: String? = nil, purchased: Bool? = nil) async throws {
        let token = try XCTUnwrap(ProcessInfo.processInfo.environment["RECIPE_MANAGER_CROSS_TOKEN"])
        var request = URLRequest(url: try XCTUnwrap(URL(string: "https://localhost:7291/step"))); request.httpMethod = "POST"; request.timeoutInterval = 30
        request.setValue(token, forHTTPHeaderField: "X-Test-Control-Token"); request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        var body: [String: Any] = ["command": command]
        if let list { body["listId"] = list }; if let item { body["itemId"] = item }; if let purchased { body["purchased"] = purchased }
        request.httpBody = try JSONSerialization.data(withJSONObject: body)
        let (_, response) = try await URLSession.shared.data(for: request)
        XCTAssertEqual((response as? HTTPURLResponse)?.statusCode, 200)
    }
}
