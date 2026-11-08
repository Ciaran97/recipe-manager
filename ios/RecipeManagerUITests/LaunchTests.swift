import XCTest

final class LaunchTests: XCTestCase {
    @MainActor func testApplicationLaunches() {
        let app = XCUIApplication()
        app.launch()
        XCTAssertTrue(app.staticTexts["Recipe Manager"].waitForExistence(timeout: 20))
        let screenshot = XCTAttachment(screenshot: app.screenshot())
        screenshot.name = "Initial server selection"
        screenshot.lifetime = .keepAlways
        add(screenshot)
    }
}
