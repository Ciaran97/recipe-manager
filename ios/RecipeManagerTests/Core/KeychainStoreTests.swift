import Foundation
import Testing
@testable import RecipeManager

struct KeychainStoreTests {
    @Test func deviceCredentialRoundTripAndRemoval() async throws {
        let store = KeychainStore()
        let origin = "https://keychain-\(UUID().uuidString.lowercased()).example.test"
        let credential = SessionCredential(value: "test-only-session", expiresAt: Date().addingTimeInterval(60))
        try await store.remove(origin: origin)
        #expect(try await store.load(origin: origin) == nil)
        do {
            try await store.save(credential, origin: origin)
            let restored = try await store.load(origin: origin)
            #expect(restored?.value == credential.value)
            #expect(restored?.expiresAt == credential.expiresAt)
            try await store.save(SessionCredential(value: "updated-test-session", expiresAt: credential.expiresAt), origin: origin)
            #expect(try await store.load(origin: origin)?.value == "updated-test-session")
            try await store.remove(origin: origin)
            #expect(try await store.load(origin: origin) == nil)
        } catch {
            try? await store.remove(origin: origin)
            throw error
        }
    }
}
