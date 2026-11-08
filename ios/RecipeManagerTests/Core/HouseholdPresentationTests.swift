import Foundation
import Testing
import RecipeManagerAPI
@testable import RecipeManager

@Suite(.serialized) @MainActor
struct HouseholdPresentationTests {
    private func selection(store: MemoryCredentials = MemoryCredentials()) -> ServerSelection {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [StubURLProtocol.self]
        return ServerSelection(credentials: store, persist: { _ in }, makeClient: { APIClient(session: $0, configuration: configuration) })
    }
    private nonisolated static func response(_ request: URLRequest, _ json: String, status: Int = 200, headers: [String: String] = [:]) -> (HTTPURLResponse, Data) {
        (HTTPURLResponse(url: request.url!, statusCode: status, httpVersion: nil, headerFields: headers)!, Data(json.utf8))
    }

    @Test func mismatchedSetupPasswordsDoNotSendAccountData() async {
        let selection = selection()
        await selection.requestChange("https://auth.example.test")
        StubURLProtocol.set(host: "auth.example.test") { request in Issue.record("No network request expected"); return Self.response(request, "{}") }
        let model = AuthViewModel(selection: selection)
        model.mode = .setup; model.email = "user@example.test"; model.password = "private-test-password"; model.confirmation = "different-password"
        await model.submit()
        #expect(model.error != nil)
        #expect(selection.user == nil)
    }

    @Test func failedServerCheckCanRetryWithoutDiscardingEnteredCredentials() async {
        let selection = selection()
        await selection.requestChange("https://auth-retry.example.test")
        StubURLProtocol.set(host: "auth-retry.example.test") { _ in throw URLError(.notConnectedToInternet) }
        let model = AuthViewModel(selection: selection)
        model.email = "user@example.test"; model.password = "private-test-password"
        await model.load()
        #expect(model.connectionFailed); #expect(model.error != nil); #expect(!model.loading)
        StubURLProtocol.set(host: "auth-retry.example.test") { request in
            Self.response(request, "{\"setupRequired\":true}")
        }
        await model.load()
        #expect(!model.connectionFailed); #expect(model.error == nil); #expect(model.setupRequired)
        #expect(model.mode == .setup); #expect(model.email == "user@example.test")
        #expect(model.password == "private-test-password")
    }

    @Test func nativeLoginUsesGeneratedContractAndClearsPasswordAfterSuccess() async {
        let store = MemoryCredentials()
        let selection = selection(store: store)
        await selection.requestChange("https://auth.example.test")
        StubURLProtocol.set(host: "auth.example.test") { request in
            if request.url!.path == "/api/v1/auth/csrf" { return Self.response(request, "{\"token\":\"csrf-fixture\"}") }
            #expect(request.url!.path == "/api/v1/auth/login")
            #expect(request.value(forHTTPHeaderField: "X-XSRF-TOKEN") == "csrf-fixture")
            return Self.response(request, "{\"id\":\"10000000-0000-0000-0000-000000000001\",\"householdId\":\"20000000-0000-0000-0000-000000000001\",\"role\":\"Member\"}", headers: ["Set-Cookie": "RecipeManager.Session=signed-in; Secure; HttpOnly; Path=/"])
        }
        let model = AuthViewModel(selection: selection)
        model.email = "user@example.test"; model.password = "private-test-password"
        await model.submit()
        #expect(selection.user?.role == .member)
        #expect(model.password.isEmpty)
        #expect(await store.load(origin: "https://auth.example.test")?.value == "signed-in")
    }

    @Test func invitationRequiresOriginConfirmationBeforeSendingAnything() async throws {
        let selection = selection()
        await selection.requestChange("https://auth.example.test")
        selection.hasPendingInput = true
        let link = URL(string: "recipemanager://invite?origin=https%3A%2F%2Fother.example.test&token=invitation-fixture")!
        selection.receiveInvitation(link)
        #expect(selection.origin?.key == "https://auth.example.test")
        #expect(selection.hasPendingInput)
        let invitation = try #require(selection.pendingInvitation)
        await selection.confirmInvitation(invitation)
        #expect(selection.origin?.key == "https://other.example.test")
        #expect(selection.invitationToken == "invitation-fixture")
        #expect(!selection.hasPendingInput)
        #expect(throws: SessionFailure.self) { try IncomingInvitation.token(from: "https://foreign.example/invite?token=secret", expected: selection.origin) }
    }

    @Test func originOnlyNativeOpenCarriesNoBearerTokenAndOffersPaste() async throws {
        let selection = selection()
        let link = try #require(URL(string: "recipemanager://invite?origin=https%3A%2F%2Fauth.example.test"))
        let invitation = try IncomingInvitation(url: link)
        #expect(invitation.token.isEmpty)
        await selection.confirmInvitation(invitation)
        let model = AuthViewModel(selection: selection)
        #expect(model.mode == .invite)
        #expect(model.token.isEmpty)
    }

    @Test func configuredKeyIsPreservedAndStaleProfileEditRemainsUnsaved() async throws {
        let selection = selection()
        await selection.requestChange("https://auth.example.test")
        await selection.signedIn(ClientUser(id: UUID(), householdId: UUID(), role: .administrator))
        let settings = "{\"id\":\"20000000-0000-0000-0000-000000000001\",\"name\":\"Home\",\"timezone\":\"UTC\",\"revision\":\"3\",\"ai\":{\"enabled\":true,\"provider\":\"OpenAICompatible\",\"model\":\"local\",\"baseUrl\":\"http://localhost:11434/v1\",\"hasApiKey\":true}}"
        StubURLProtocol.set(host: "auth.example.test") { request in
            switch request.url!.path {
            case "/api/v1/auth/csrf": return Self.response(request, "{\"token\":\"csrf-fixture\"}")
            case "/api/v1/profile":
                if request.httpMethod == "PUT" {
                    #expect(request.value(forHTTPHeaderField: "If-Match") == "\"opaque-profile\"")
                    return Self.response(request, "{\"code\":\"stale_revision\",\"errors\":[]}", status: 412)
                }
                return Self.response(request, "{\"id\":\"10000000-0000-0000-0000-000000000001\",\"name\":\"Name\",\"preferences\":{},\"revision\":\"1\"}", headers: ["ETag": "\"opaque-profile\""])
            case "/api/v1/household/settings":
                if request.httpMethod == "PUT" {
                    #expect(request.value(forHTTPHeaderField: "If-Match") == "\"opaque-settings\"")
                    let body = try JSONSerialization.jsonObject(with: Self.body(request)) as! [String: Any]
                    #expect((body["ai"] as? [String: Any])?["apiKey"] == nil)
                }
                return Self.response(request, settings, headers: ["ETag": "\"opaque-settings\""])
            default: return Self.response(request, "{\"items\":[],\"nextCursor\":null}")
            }
        }
        let model = SettingsViewModel(selection: selection)
        await model.load()
        #expect(model.error == nil)
        #expect(model.newApiKey.isEmpty)
        #expect(model.household?.ai.hasApiKey == true)
        await model.saveHousehold()
        #expect(model.error == nil)
        model.name = "Unsaved edit"
        await model.saveProfile()
        #expect(model.name == "Unsaved edit")
        #expect(model.isDirty)
        #expect(model.profile?.revision == "1")
        #expect(model.error?.contains("Reload") == true)
    }

    @Test func timestampsAndLargeRevisionStringsDecodeWithoutQuantityCoercion() throws {
        let json = "{\"id\":\"10000000-0000-0000-0000-000000000001\",\"expiresAt\":\"2026-11-01T00:00:00.1234567+00:00\",\"acceptedAt\":null,\"revokedAt\":null,\"revision\":\"9007199254740993\",\"etag\":\"opaque\"}"
        let invitation = try APIJSON.decoder().decode(Components.Schemas.InvitationRead.self, from: Data(json.utf8))
        #expect(invitation.revision == "9007199254740993")
        #expect(invitation.expiresAt.formatted(.iso8601).hasPrefix("2026-11-01T00:00:00"))
    }

    private nonisolated static func body(_ request: URLRequest) -> Data {
        if let data = request.httpBody { return data }
        guard let stream = request.httpBodyStream else { return Data() }
        stream.open(); defer { stream.close() }
        var data = Data(); var buffer = [UInt8](repeating: 0, count: 1024)
        while stream.hasBytesAvailable { let count = stream.read(&buffer, maxLength: buffer.count); if count <= 0 { break }; data.append(contentsOf: buffer.prefix(count)) }
        return data
    }
}
