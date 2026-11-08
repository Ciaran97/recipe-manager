import Foundation
import Testing
@testable import RecipeManager

@MainActor struct ServerSelectionTests {
    @Test func invalidOriginDoesNotCreateClient() async {
        let selection = ServerSelection(credentials: MemoryCredentials(), persist: { _ in })
        await selection.requestChange("http://example.test")
        #expect(selection.api == nil)
        #expect(selection.error != nil)
    }
    @Test func dirtyInputRequiresExplicitConfirmationAndCanBeCancelled() async {
        let selection = ServerSelection(credentials: MemoryCredentials(), persist: { _ in })
        await selection.requestChange("https://first.test")
        selection.hasPendingInput = true
        await selection.requestChange("https://second.test")
        #expect(selection.origin?.key == "https://first.test")
        #expect(selection.pendingOrigin?.key == "https://second.test")
        selection.cancelChange()
        #expect(selection.hasPendingInput)
        #expect(selection.pendingOrigin == nil)
        await selection.requestChange("https://second.test")
        await selection.confirmChange()
        #expect(selection.origin?.key == "https://second.test")
        #expect(!selection.hasPendingInput)
        #expect(selection.user == nil)
    }
    @Test func scopedDraftRejectsChangedServerBeforeNetworking() async throws {
        let selection = ServerSelection(credentials: MemoryCredentials(), persist: { _ in })
        await selection.requestChange("https://first.test")
        let scope = ScopedAPI(selection: selection)
        await selection.requestChange("https://second.test")
        await #expect {
            try await scope.send(path: "/api/v1/recipes", method: "POST", body: Data("{}".utf8))
        } throws: { error in if case SessionFailure.sessionChanged = error { true } else { false } }
    }
    @Test func scopedDraftRejectsNewAccountOnSameServer() async throws {
        let selection = ServerSelection(credentials: MemoryCredentials(), persist: { _ in })
        await selection.requestChange("https://first.test")
        let scope = ScopedAPI(selection: selection)
        await selection.signedIn(ClientUser(id: UUID(), householdId: UUID(), role: .member))
        await #expect {
            try await scope.send(path: "/api/v1/images", method: "POST", body: Data())
        } throws: { error in if case SessionFailure.sessionChanged = error { true } else { false } }
    }
}
