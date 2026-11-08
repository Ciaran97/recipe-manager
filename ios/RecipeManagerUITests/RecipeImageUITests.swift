import XCTest

final class RecipeImageUITests: XCTestCase {
    @MainActor func testNativePhotoSelectionReplacementRemovalAndRecipeDetails() async throws {
        guard ProcessInfo.processInfo.environment["RECIPE_MANAGER_RUN_E2E"] == "1" else {
            throw XCTSkip("Run the isolated signed RecipeImageUITests fixture with its two synthetic photos.")
        }
        continueAfterFailure = false
        let api = try NativeFixtureAPI(); try await api.login()
        let ingredient = try await api.write("POST", "/api/v1/ingredients", body: ["canonicalName": "Photo parity rice"], expected: 201)
        let id = try await api.seedRecipe(ingredientId: XCTUnwrap(ingredient["id"] as? String), title: "Image parity recipe")
        let app = XCUIApplication(); app.launch()
        XCTAssertTrue(app.tabBars.buttons["Recipes"].waitForExistence(timeout: 20))
        featureTap(app.tabBars.buttons["Recipes"], in: app)
        featureTap(app.buttons["Image parity recipe"], in: app)
        featureTap(app.buttons["Edit recipe"], in: app)
        featureEnter(app.textFields["Description"], "A synthetic image acceptance recipe.", in: app)
        featureEnter(app.textFields["Preparation minutes"], "5", in: app)
        featureEnter(app.textFields["Cooking minutes"], "20", in: app)
        selectPhoto(0, app)
        featureTap(app.buttons["Save recipe"], in: app)
        waitClosed(app)
        let (first, _) = try await api.read("/api/v1/recipes/" + id)
        let firstImage = try XCTUnwrap(first["imageAssetId"] as? String)
        XCTAssertEqual(first["description"] as? String, "A synthetic image acceptance recipe.")
        XCTAssertEqual(first["prepMinutes"] as? Int, 5); XCTAssertEqual(first["cookMinutes"] as? Int, 20)
        assertDetailPhoto(firstImage, in: app)
        featureTap(app.buttons["Edit recipe"], in: app)
        selectPhoto(1, app, previous: firstImage)
        featureTap(app.buttons["Save recipe"], in: app); waitClosed(app)
        let (replacement, _) = try await api.read("/api/v1/recipes/" + id)
        let replacementImage = try XCTUnwrap(replacement["imageAssetId"] as? String)
        XCTAssertNotEqual(firstImage, replacementImage)
        assertDetailPhoto(replacementImage, in: app)
        featureTap(app.buttons["Edit recipe"], in: app)
        featureTap(app.buttons["Remove photo from recipe"], in: app)
        featureEnter(app.textFields["Notes"], "Photo deliberately removed.", in: app)
        featureEnter(app.textFields["Source URL"], "https://example.test/recipe", in: app)
        featureTap(app.buttons["Save recipe"], in: app); waitClosed(app)
        let (removed, _) = try await api.read("/api/v1/recipes/" + id)
        XCTAssertNil(removed["imageAssetId"] as? String)
        XCTAssertEqual(removed["notes"] as? String, "Photo deliberately removed.")
        XCTAssertEqual(removed["sourceUrl"] as? String, "https://example.test/recipe")
        // Images use stable asset identifiers, not the recipe title. Bring the
        // detail header back into view before checking that removal is visible.
        for _ in 0..<3 { app.collectionViews.firstMatch.swipeDown() }
        let remainingPhoto = app.images.matching(NSPredicate(format: "identifier BEGINSWITH %@", "recipe-image-")).firstMatch
        XCTAssertEqual(XCTWaiter.wait(for: [XCTNSPredicateExpectation(predicate: NSPredicate(format: "exists == false"), object: remainingPhoto)], timeout: 15), .completed)
    }

    @MainActor private func assertDetailPhoto(_ id: String, in app: XCUIApplication) {
        let photo = app.images["recipe-image-" + id]
        // Editing is opened from the actions near the bottom of the recipe. A List
        // can legitimately recycle its offscreen photo after the sheet closes.
        for _ in 0..<8 {
            if photo.exists && photo.isHittable { break }
            app.collectionViews.firstMatch.swipeDown()
        }
        if !photo.waitForExistence(timeout: 15) {
            let attachment = XCTAttachment(screenshot: app.screenshot())
            attachment.name = "Recipe detail expected replacement photo"
            attachment.lifetime = .keepAlways
            add(attachment)
        }
        XCTAssertTrue(photo.exists, "The detail must render the newly saved asset, not a stale photo.")
    }

    @MainActor private func selectPhoto(_ index: Int, _ app: XCUIApplication, previous: String? = nil) {
        featureTap(app.buttons["Choose recipe photo"], in: app)
        XCTAssertTrue(app.navigationBars["Photos"].waitForExistence(timeout: 15))
        let photo = app.images.matching(NSPredicate(format: "label BEGINSWITH %@", "Photo,")).element(boundBy: index)
        XCTAssertTrue(photo.waitForExistence(timeout: 15))
        photo.tap()
        XCTAssertEqual(XCTWaiter.wait(for: [XCTNSPredicateExpectation(predicate: NSPredicate(format: "exists == false"), object: app.navigationBars["Photos"])], timeout: 15), .completed)
        let predicate = NSPredicate(format: "identifier BEGINSWITH %@ AND identifier != %@", "recipe-image-", previous.map { "recipe-image-" + $0 } ?? "")
        XCTAssertTrue(app.images.matching(predicate).firstMatch.waitForExistence(timeout: 20))
        XCTAssertTrue(app.buttons["Remove photo from recipe"].waitForExistence(timeout: 20))
        XCTAssertEqual(XCTWaiter.wait(for: [XCTNSPredicateExpectation(predicate: NSPredicate(format: "enabled == true"), object: app.buttons["Save recipe"])], timeout: 20), .completed)
    }
    @MainActor private func waitClosed(_ app: XCUIApplication) {
        XCTAssertEqual(XCTWaiter.wait(for: [XCTNSPredicateExpectation(predicate: NSPredicate(format: "exists == false"), object: app.navigationBars["Edit recipe"])], timeout: 15), .completed)
    }
}
