import Foundation
import XCTest

@MainActor final class NativeFixtureAPI {
    private let origin: URL
    private let session = URLSession(configuration: .ephemeral)
    init() throws {
        origin = try XCTUnwrap(URL(string: try XCTUnwrap(ProcessInfo.processInfo.environment["RECIPE_MANAGER_E2E_ORIGIN"])))
    }
    func login() async throws {
        _ = try await write("POST", "/api/v1/auth/login", body: ["email": "admin@example.test", "password": ProcessInfo.processInfo.environment["RECIPE_MANAGER_E2E_ADMIN_PASSWORD"] ?? "e2e-admin-test-passphrase"], expected: 200)
    }
    func read(_ path: String, expected: Int = 200) async throws -> ([String: Any], HTTPURLResponse) {
        let (data, response) = try await session.data(from: XCTUnwrap(URL(string: path, relativeTo: origin)))
        let http = try XCTUnwrap(response as? HTTPURLResponse); XCTAssertEqual(http.statusCode, expected)
        return (try JSONSerialization.jsonObject(with: data) as? [String: Any] ?? [:], http)
    }
    @discardableResult func write(_ method: String, _ path: String, body: [String: Any]? = nil, etag: String? = nil, expected: Int) async throws -> [String: Any] {
        let (csrf, _) = try await read("/api/v1/auth/csrf")
        var request = URLRequest(url: origin.appendingPathComponent(path)); request.httpMethod = method
        request.setValue(try XCTUnwrap(csrf["token"] as? String), forHTTPHeaderField: "X-XSRF-TOKEN")
        if let etag { request.setValue(etag, forHTTPHeaderField: "If-Match") }
        if let body { request.httpBody = try JSONSerialization.data(withJSONObject: body); request.setValue("application/json", forHTTPHeaderField: "Content-Type") }
        let (data, response) = try await session.data(for: request)
        XCTAssertEqual((response as? HTTPURLResponse)?.statusCode, expected)
        if data.isEmpty { return [:] }
        return try JSONSerialization.jsonObject(with: data) as? [String: Any] ?? [:]
    }
    func ingredient(named name: String) async throws -> [String: Any] {
        var query = URLComponents()
        query.queryItems = [URLQueryItem(name: "q", value: name)]
        let (catalogue, _) = try await read("/api/v1/ingredients?" + (query.percentEncodedQuery ?? ""))
        if let existing = (catalogue["items"] as? [[String: Any]])?.first(where: { $0["canonicalName"] as? String == name }) {
            return existing
        }
        return try await write("POST", "/api/v1/ingredients", body: ["canonicalName": name], expected: 201)
    }
    func seedRecipe(ingredientId: String, title: String = "Pepper salad") async throws -> String {
        let body: [String: Any] = ["title": title, "defaultServings": "2", "measurementStandard": "AU", "groups": [],
            "ingredients": [["position": 0, "ingredientId": ingredientId, "quantityText": "1", "kind": "numeric", "unit": "item", "uncertain": false]],
            "instructions": [["position": 0, "text": "Slice and serve."]], "collectionIds": [], "tagIds": []]
        let result = try await write("POST", "/api/v1/recipes", body: body, expected: 201)
        return try XCTUnwrap(result["id"] as? String)
    }
}

@MainActor extension XCTestCase {
    func featureTap(_ element: XCUIElement, in app: XCUIApplication) {
        _ = element.waitForExistence(timeout: 2)
        featureReveal(element, in: app)
        XCTAssertTrue(element.exists)
        XCTAssertEqual(XCTWaiter.wait(for: [XCTNSPredicateExpectation(predicate: NSPredicate(format: "enabled == true"), object: element)], timeout: 10), .completed)
        element.tap()
    }
    func featureEnter(_ element: XCUIElement, _ text: String, in app: XCUIApplication, replace: Bool = false) {
        featureReveal(element, in: app, aboveKeyboard: true)
        XCTAssertEqual(XCTWaiter.wait(for: [XCTNSPredicateExpectation(predicate: NSPredicate(format: "enabled == true"), object: element)], timeout: 10), .completed)
        element.tap()
        if !app.keyboards.firstMatch.waitForExistence(timeout: 3) { element.tap() }
        XCTAssertTrue(app.keyboards.firstMatch.waitForExistence(timeout: 5))
        if replace, let value = element.value as? String, value != element.placeholderValue {
            // A tap may place the caret in the middle of a long, horizontally
            // scrolled field. Select all through UIKit's supported key command
            // instead of deleting only the prefix and retaining an old suffix.
            element.typeKey("a", modifierFlags: .command)
            element.typeText(XCUIKeyboardKey.delete.rawValue)
            // Clearing may replace a SwiftUI Form field; reacquire keyboard focus.
            element.tap()
        }
        if !text.isEmpty { element.typeText(text) }
        if replace && element.elementType != .secureTextField {
            let value = element.value as? String
            if text.isEmpty {
                XCTAssertTrue(value == nil || value == "" || value == element.placeholderValue,
                    "Clearing must not retain an old prefix or suffix.")
            } else {
                XCTAssertEqual(value, text, "Replacement must not retain an old prefix or suffix.")
            }
        }
    }
    func featureReveal(_ element: XCUIElement, in app: XCUIApplication, aboveKeyboard: Bool = false) {
        // Drag inside the presented form, not its sheet header/error banner. A downward
        // drag outside the scroll surface can dismiss a clean editor instead of scrolling it.
        func scrollSurface() -> XCUIElement? {
            app.collectionViews.allElementsBoundByIndex.last(where: { $0.isHittable })
                ?? app.scrollViews.allElementsBoundByIndex.last(where: { $0.isHittable })
        }
        func scroll(from start: CGFloat, to end: CGFloat) {
            let surface = scrollSurface() ?? app
            surface.coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: start)).press(forDuration: 0.05,
                thenDragTo: surface.coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: end)))
        }
        func visible() -> Bool {
            guard element.exists && element.isHittable, let snapshot = try? element.snapshot() else { return false }
            // SwiftUI may recycle an offscreen Form row between live AX queries.
            // Use one immutable snapshot for geometry while inspecting its viewport.
            var top = app.frame.minY
            var bottom = app.frame.maxY
            // UIKit can expose a clipped form field as hittable. Check its containing
            // scroll viewport as well; navigation-bar search fields remain independent.
            let identifier = snapshot.identifier
            if !identifier.isEmpty, let surface = scrollSurface(),
               surface.descendants(matching: snapshot.elementType).matching(identifier: identifier).firstMatch.exists {
                top = max(top, surface.frame.minY)
                bottom = min(bottom, surface.frame.maxY)
            }
            let keyboard = app.keyboards.firstMatch
            if keyboard.exists && keyboard.frame.height > 0 { bottom = min(bottom, keyboard.frame.minY) }
            let tabs = app.tabBars.firstMatch
            if tabs.exists && tabs.isHittable {
                let isTabButton = snapshot.elementType == .button
                    && tabs.frame.contains(CGPoint(x: snapshot.frame.midX, y: snapshot.frame.midY))
                    && tabs.buttons.matching(NSPredicate(format: "label == %@", snapshot.label)).firstMatch.exists
                if !isTabButton { bottom = min(bottom, tabs.frame.minY) }
            }
            return snapshot.frame.midY > top + 5 && snapshot.frame.midY < bottom - 10
                && snapshot.frame.midX > app.frame.minX && snapshot.frame.midX < app.frame.maxX
        }
        if !element.exists {
            for _ in 0..<3 {
                scroll(from: 0.25, to: 0.7)
                if visible() { return }
            }
        }
        for _ in 0..<12 {
            if visible() { return }
            // A recycled row can yield a zero-sized AX snapshot instead of
            // throwing. Its origin is not evidence that the target is above us.
            // After the initial upward search, continue down through the list.
            let snapshotFrame = element.exists ? (try? element.snapshot())?.frame : nil
            let currentFrame = snapshotFrame.flatMap { $0.width > 0 && $0.height > 0 ? $0 : nil }
            let below = currentFrame.map { $0.midY > app.frame.height * 0.25 } ?? true
            scroll(from: below ? 0.45 : 0.25, to: below ? 0.18 : 0.7)
        }
        XCTAssertTrue(element.exists)
        if aboveKeyboard { XCTAssertTrue(visible(), "The input's centre must be visible above the keyboard before typing.") }
    }
    func featureNotice(_ value: String, in app: XCUIApplication) {
        let notice = app.staticTexts.matching(identifier: "catalogue-notice").firstMatch
        XCTAssertEqual(XCTWaiter.wait(for: [XCTNSPredicateExpectation(predicate: NSPredicate(format: "label CONTAINS %@", value), object: notice)], timeout: 15), .completed)
    }
    func featureConfirm(_ label: String, in app: XCUIApplication) {
        let action = app.sheets.buttons[label]
        if action.waitForExistence(timeout: 3) { action.tap() }
        else {
            let matches = app.buttons.matching(NSPredicate(format: "label == %@", label)); XCTAssertGreaterThan(matches.count, 0)
            matches.element(boundBy: matches.count - 1).tap()
        }
    }
}
