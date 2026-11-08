import Foundation
import Testing
@testable import RecipeManager

@MainActor struct InvitationPasteTests {
    @Test func acceptsURLAndTextItemProviders() async throws {
        let url = try #require(URL(string: "https://recipes.example.test/invite?token=test-only-invitation"))
        let link: String? = await withCheckedContinuation { continuation in
            InvitationPaste.load([NSItemProvider(object: url as NSURL)]) { continuation.resume(returning: $0) }
        }
        #expect(link == url.absoluteString)
        let token: String? = await withCheckedContinuation { continuation in
            InvitationPaste.load([NSItemProvider(object: "test-only-token" as NSString)]) { continuation.resume(returning: $0) }
        }
        #expect(token == "test-only-token")
    }
}
