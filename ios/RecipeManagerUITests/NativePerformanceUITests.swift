import Foundation
import XCTest

/// Test-only UI agent for the private MP-SYNC-01 controller. No application endpoints or domain mocks.
final class NativePerformanceUITests: XCTestCase {
    @MainActor func testControllerDrivenRealClient() async throws {
        let environment = ProcessInfo.processInfo.environment
        guard environment["RECIPE_MANAGER_RUN_PERFORMANCE"] == "1" else {
            throw XCTSkip("Requires the owned reference-host performance controller and disposable simulator.")
        }
        continueAfterFailure = false
        let origin = try XCTUnwrap(environment["RECIPE_MANAGER_PERF_ORIGIN"])
        let control = try XCTUnwrap(URL(string: try XCTUnwrap(environment["RECIPE_MANAGER_PERF_CONTROL"])))
        let token = try XCTUnwrap(environment["RECIPE_MANAGER_PERF_TOKEN"])
        let agent = try XCTUnwrap(environment["RECIPE_MANAGER_PERF_AGENT"])
        let configuration = URLSessionConfiguration.ephemeral
        configuration.timeoutIntervalForRequest = 40
        configuration.httpCookieStorage = nil
        let session = URLSession(configuration: configuration)
        defer { session.invalidateAndCancel() }
        func request(_ route: String, body: [String: Any]? = nil) async throws -> [String: Any] {
            var outgoing = URLRequest(url: control.appendingPathComponent("agents/\(agent)/\(route)"))
            outgoing.setValue(token, forHTTPHeaderField: "X-Performance-Control")
            if let body {
                outgoing.httpMethod = "POST"
                outgoing.setValue("application/json", forHTTPHeaderField: "Content-Type")
                outgoing.httpBody = try JSONSerialization.data(withJSONObject: body)
            }
            let (data, response) = try await session.data(for: outgoing)
            let http = try XCTUnwrap(response as? HTTPURLResponse)
            guard http.statusCode == 200 else { throw PerformanceAgentError.controller }
            return data.isEmpty ? [:] : try XCTUnwrap(JSONSerialization.jsonObject(with: data) as? [String: Any])
        }
        let app = XCUIApplication()
        app.launch()
        _ = try await request("status", body: ["stage": "app-launched"])
        featureEnter(app.textFields["Server HTTPS address"], origin, in: app)
        featureTap(app.buttons["Use server"], in: app)
        _ = try await request("status", body: ["stage": "server-selected"])
        XCTAssertTrue(app.textFields["email"].waitForExistence(timeout: 30))
        featureEnter(app.textFields["email"], try XCTUnwrap(environment["RECIPE_MANAGER_PERF_EMAIL"]), in: app)
        featureEnter(app.secureTextFields["password"], try XCTUnwrap(environment["RECIPE_MANAGER_PERF_PASSWORD"]), in: app)
        featureTap(app.buttons["submit-auth"], in: app)
        XCTAssertTrue(app.tabBars.buttons["Recipes"].waitForExistence(timeout: 30))
        _ = try await request("ready", body: [:])
        while true {
            let command = try await request("next")
            guard let id = command["id"] as? String, let action = command["action"] as? String else { continue }
            if action == "stop" { _ = try await request("result", body: ["id": id, "ok": true]); return }
            let entity = command["entity"] as? String ?? ""
            switch action {
            case "prepare":
                if entity == "recipe" {
                    featureTap(app.tabBars.buttons["Recipes"], in: app)
                    if app.buttons["Edit recipe"].exists { featureTap(app.navigationBars.buttons.firstMatch, in: app) }
                    let title = try XCTUnwrap(command["title"] as? String)
                    featureEnter(app.searchFields.firstMatch, title, in: app, replace: true)
                    featureTap(app.keyboards.buttons["Search"], in: app)
                    let row = app.buttons["recipe-library-" + (try XCTUnwrap(command["recipeId"] as? String))]
                    XCTAssertTrue(row.waitForExistence(timeout: 20))
                    featureTap(row, in: app)
                    XCTAssertTrue(app.buttons["Edit recipe"].waitForExistence(timeout: 20))
                } else if entity == "shopping" {
                    featureTap(app.tabBars.buttons["Shopping Lists"], in: app)
                    featureTap(app.buttons["shopping-list-" + (try XCTUnwrap(command["listId"] as? String))], in: app)
                    let item = app.buttons["shopping-edit-" + (try XCTUnwrap(command["itemId"] as? String))]
                    featureReveal(item, in: app)
                    XCTAssertTrue(item.exists && item.isHittable)
                } else if entity == "meal" {
                    featureTap(app.tabBars.buttons["Meal Planner"], in: app)
                    featureTap(app.buttons["planner-day-0"], in: app)
                    let meal = app.buttons["planner-meal-" + (try XCTUnwrap(command["mealId"] as? String))]
                    featureReveal(meal, in: app)
                    XCTAssertTrue(meal.exists && meal.isHittable)
                } else { throw PerformanceAgentError.command }
            case "write":
                let marker = try XCTUnwrap(command["marker"] as? String)
                if entity == "recipe" {
                    featureTap(app.buttons["Edit recipe"], in: app)
                    featureEnter(app.textFields["recipe-title"], marker, in: app, replace: true)
                    featureTap(app.buttons["Save recipe"], in: app)
                    XCTAssertTrue(app.buttons["Edit recipe"].waitForExistence(timeout: 20))
                } else if entity == "shopping" {
                    featureTap(app.buttons["shopping-edit-" + (try XCTUnwrap(command["itemId"] as? String))], in: app)
                    featureEnter(app.textFields["Item name"], marker, in: app, replace: true)
                    featureTap(app.buttons["shopping-save-item"], in: app)
                    XCTAssertEqual(XCTWaiter.wait(for: [XCTNSPredicateExpectation(predicate: NSPredicate(format: "exists == false"), object: app.buttons["shopping-save-item"])], timeout: 20), .completed)
                } else if entity == "meal" {
                    featureTap(app.buttons["planner-meal-" + (try XCTUnwrap(command["mealId"] as? String))], in: app)
                    featureEnter(app.textFields["Meal description"], marker, in: app, replace: true)
                    featureTap(app.buttons["meal-save"], in: app)
                    XCTAssertEqual(XCTWaiter.wait(for: [XCTNSPredicateExpectation(predicate: NSPredicate(format: "exists == false"), object: app.buttons["meal-save"])], timeout: 20), .completed)
                } else { throw PerformanceAgentError.command }
            case "observe", "assertAbsent":
                let marker = try XCTUnwrap(command["marker"] as? String)
                let element = entity == "recipe" ? app.navigationBars[marker] : app.staticTexts[marker]
                if action == "observe" {
                    try await waitVisible(element)
                } else { XCTAssertFalse(element.exists) }
            case "background":
                XCUIDevice.shared.press(.home)
                let background = NSPredicate { _, _ in app.state == .runningBackground || app.state == .runningBackgroundSuspended }
                XCTAssertEqual(XCTWaiter.wait(for: [XCTNSPredicateExpectation(predicate: background, object: nil)], timeout: 10), .completed)
            case "foreground":
                app.activate()
                XCTAssertEqual(app.state, .runningForeground)
            case "network":
                var probe = URLRequest(url: try XCTUnwrap(URL(string: origin + "/health/ready")))
                probe.cachePolicy = .reloadIgnoringLocalCacheData
                let (_, response) = try await session.data(for: probe)
                XCTAssertEqual((response as? HTTPURLResponse)?.statusCode, 200)
            case "prepareSearch":
                featureTap(app.tabBars.buttons["Recipes"], in: app)
                if app.buttons["Edit recipe"].exists { featureTap(app.navigationBars.buttons["Recipes"], in: app) }
                featureEnter(app.searchFields.firstMatch, try XCTUnwrap(command["title"] as? String), in: app, replace: true)
                XCTAssertTrue(app.keyboards.buttons["Search"].exists)
            case "submitSearch":
                app.keyboards.buttons["Search"].tap()
                try await waitVisible(app.staticTexts[try XCTUnwrap(command["title"] as? String)])
            case "calendarProbe":
                app.tabBars.buttons["Meal Planner"].tap()
                try await waitVisible(app.buttons["planner-day-0"])
                featureReveal(app.buttons["planner-meal-" + (try XCTUnwrap(command["mealId"] as? String))], in: app)
                try await waitVisible(app.buttons["planner-meal-" + (try XCTUnwrap(command["mealId"] as? String))])
            case "toggleFavourite":
                let wasFavourite = app.buttons["Remove favourite"].exists
                featureTap(app.buttons[wasFavourite ? "Remove favourite" : "Favourite recipe"], in: app)
                try await waitVisible(app.buttons[wasFavourite ? "Favourite recipe" : "Remove favourite"])
            case "beginDraft":
                featureTap(app.buttons["Edit recipe"], in: app)
                featureEnter(app.textFields["recipe-title"], try XCTUnwrap(command["marker"] as? String), in: app, replace: true)
            case "assertDraft":
                XCTAssertEqual(app.textFields["recipe-title"].value as? String, command["marker"] as? String)
            case "saveStale":
                featureTap(app.buttons["Save recipe"], in: app)
                XCTAssertTrue(app.staticTexts["recipe-editor-error"].waitForExistence(timeout: 20))
                XCTAssertEqual(app.textFields["recipe-title"].value as? String, command["marker"] as? String)
            case "cancelDraft":
                featureTap(app.navigationBars["Edit recipe"].buttons["Cancel"], in: app)
                featureConfirm("Discard changes", in: app)
            case "assertBackground":
                XCTAssertTrue(app.state == .runningBackground || app.state == .runningBackgroundSuspended)
            default: throw PerformanceAgentError.command
            }
            _ = try await request("result", body: ["id": id, "ok": true])
        }
    }
    @MainActor private func waitVisible(_ element: XCUIElement) async throws {
        // Frequent real AX checks reduce test-controller polling overhead. No
        // device timestamp is returned or used in the latency calculation.
        for _ in 0..<300 {
            if element.exists && element.isHittable { return }
            try await Task.sleep(for: .milliseconds(100))
        }
        XCTFail("The accepted state did not become visibly available.")
        throw PerformanceAgentError.command
    }
}

private enum PerformanceAgentError: Error { case controller, command }
