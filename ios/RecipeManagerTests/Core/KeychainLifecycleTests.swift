import Foundation
import Testing
@testable import RecipeManager

@Suite(.serialized) @MainActor struct KeychainLifecycleTests {
    private func selection(_ store: any CredentialStore) -> ServerSelection {
        let configuration = URLSessionConfiguration.ephemeral; configuration.protocolClasses = [StubURLProtocol.self]
        StubURLProtocol.set(host: "restore-old.example.test") { request in
            (HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: nil, headerFields: nil)!, Data("{\"id\":\"00000000-0000-0000-0000-000000000001\",\"householdId\":\"00000000-0000-0000-0000-000000000002\",\"role\":\"Member\"}".utf8))
        }
        return ServerSelection(credentials: store, persist: { _ in }, makeClient: { APIClient(session: $0, configuration: configuration) })
    }
    @Test func aServerChangeWinsOverAnOutstandingStartupCredentialRead() async throws {
        let store = SuspendedCredentialRead(), selection = selection(store)
        let restoration = Task { await selection.restore("https://restore-old.example.test") }
        #expect(await eventually { await store.started })
        await selection.requestChange("https://restore-new.example.test")
        let identity = selection.sessionIdentity
        await store.release(); await restoration.value
        #expect(selection.origin?.key == "https://restore-new.example.test")
        #expect(selection.user == nil); #expect(selection.sessionIdentity == identity); #expect(selection.error == nil)
        #expect(!(await store.containsOldCredential()))
    }
    @Test func clearingAnAccountCancelsOutstandingStartupCredentialRestoration() async throws {
        let store = SuspendedCredentialRead(), selection = selection(store)
        let restoration = Task { await selection.restore("https://restore-old.example.test") }
        #expect(await eventually { await store.started })
        #expect(await selection.clearAccount())
        await store.release(); await restoration.value
        #expect(selection.user == nil); #expect(selection.api == nil); #expect(selection.origin == nil)
        #expect(!(await store.containsOldCredential()))
    }
    @Test func aNewAccountIsNotReplacedByTheOlderStartupIdentity() async throws {
        let store = SuspendedCredentialRead(), selection = selection(store)
        let restoration = Task { await selection.restore("https://restore-old.example.test") }
        #expect(await eventually { await store.started })
        await selection.requestChange("https://restore-new.example.test")
        let user = ClientUser(id: UUID(), householdId: UUID(), role: .administrator)
        await selection.signedIn(user)
        let identity = selection.sessionIdentity
        await store.release(); await restoration.value
        #expect(selection.user?.id == user.id); #expect(selection.user?.householdId == user.householdId)
        #expect(selection.origin?.key == "https://restore-new.example.test"); #expect(selection.sessionIdentity == identity)
    }
}
private actor SuspendedCredentialRead: CredentialStore {
    private var values = ["https://restore-old.example.test": SessionCredential(value: "old-fixture-ticket", expiresAt: Date().addingTimeInterval(3600))]
    private var continuation: CheckedContinuation<Void, Never>?
    private(set) var started = false
    func load(origin: String) async -> SessionCredential? {
        let value = values[origin]; started = true
        await withCheckedContinuation { continuation = $0 }; return value
    }
    func save(_ credential: SessionCredential, origin: String) { values[origin] = credential }
    func remove(origin: String) { values.removeValue(forKey: origin) }
    func release() { continuation?.resume(); continuation = nil }
    func containsOldCredential() -> Bool { values["https://restore-old.example.test"] != nil }
}
