import Foundation
import Testing
import RecipeManagerAPI
import ShareHandoff
@testable import RecipeManager

@Suite(.serialized) @MainActor struct ImportFlowTests {
    private func selection(_ host: String) async -> ServerSelection {
        let configuration = URLSessionConfiguration.ephemeral; configuration.protocolClasses = [StubURLProtocol.self]
        let selection = ServerSelection(credentials: MemoryCredentials(), persist: { _ in }, makeClient: { APIClient(session: $0, configuration: configuration) })
        await selection.requestChange("https://" + host)
        await selection.signedIn(ClientUser(id: UUID(), householdId: UUID(), role: .member))
        return selection
    }
    @Test func providerFailureRetainsTextAndStableRetryIdentity() async throws {
        let host = "native-import-failure.example.test", selection = await selection("native-import-failure.example.test")
        let keys = ImportRequestLog()
        StubURLProtocol.set(host: host) { request in
            var status = 200, body = "{\"token\":\"csrf\"}"
            if request.url?.path == "/api/v1/household/settings" {
                body = "{\"id\":\"00000000-0000-0000-0000-000000000001\",\"name\":\"Home\",\"timezone\":\"UTC\",\"revision\":\"1\",\"ai\":{\"enabled\":true,\"provider\":\"OpenAICompatible\",\"model\":\"model\",\"baseUrl\":\"http://provider/v1\",\"hasApiKey\":false}}"
            } else if request.url?.path == "/api/v1/imports/text" {
                keys.add(request.value(forHTTPHeaderField: "Idempotency-Key") ?? "missing")
                status = 502; body = "{\"code\":\"import_provider_failed\"}"
            }
            return (HTTPURLResponse(url: request.url!, statusCode: status, httpVersion: nil, headerFields: nil)!, Data(body.utf8))
        }
        let model = ImportReviewViewModel(selection: selection)
        await model.loadSettings(); model.method = .text; model.input = "Soup\nsalt to taste"
        await model.start(); await model.start()
        #expect(model.input == "Soup\nsalt to taste"); #expect(model.error?.contains("provider") == true)
        #expect(keys.values.count == 2); #expect(Set(keys.values).count == 1)
    }
    @Test func disabledAiRetainsPastedTextWithoutAnExtractionRequest() async throws {
        let host = "native-import-disabled.example.test", selection = await selection("native-import-disabled.example.test")
        let paths = ImportRequestLog()
        StubURLProtocol.set(host: host) { request in
            paths.add(request.url!.path)
            let body = "{\"id\":\"00000000-0000-0000-0000-000000000001\",\"name\":\"Home\",\"timezone\":\"UTC\",\"revision\":\"1\",\"ai\":{\"enabled\":false,\"provider\":\"OpenAICompatible\",\"model\":\"\",\"baseUrl\":\"\",\"hasApiKey\":false}}"
            return (HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: nil, headerFields: nil)!, Data(body.utf8))
        }
        let model = ImportReviewViewModel(selection: selection)
        await model.loadSettings(); model.method = .text; model.input = "Soup\nsalt to taste"
        await model.start()
        #expect(model.settingsLoaded && !model.aiEnabled)
        #expect(model.input == "Soup\nsalt to taste")
        #expect(model.error?.contains("not configured") == true)
        #expect(model.draft == nil && model.savedRecipe == nil)
        #expect(paths.values == ["/api/v1/household/settings"])
    }
    @Test func committedCaptureResolvesBeforeExpiredSourceAndDisabledAiWithoutAnyUnboundFallback() async throws {
        let host = "native-import-committed.example.test", selection = await selection("native-import-committed.example.test")
        let old = Date().addingTimeInterval(-8 * 86400), root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: root) }
        let store = HandoffStore(directory: root, now: { old })
        let captured = try HandoffEnvelope(kind: .text, content: "Old recipe text", createdAt: old)
        _ = try await store.append(captured)
        let user = try #require(selection.user), origin = try #require(selection.origin)
        let binding = try HandoffDestination(origin: origin.key, householdId: user.householdId, userId: user.id)
        let capture = try await store.bind(captured.handoffId, to: binding), recipeId = UUID().uuidString
        let paths = ImportRequestLog()
        StubURLProtocol.set(host: host) { request in
            paths.add(request.url!.path); #expect(request.httpMethod == "GET")
            let body: String
            if request.url!.path.contains("import-handoffs") {
                body = "{\"handoffId\":\"\(capture.handoffId)\",\"state\":\"Committed\",\"expiresAt\":\"2026-01-01T00:00:00Z\",\"result\":{\"recipeId\":\"\(recipeId)\",\"committedRevision\":\"1\",\"committedAt\":\"2025-12-25T00:00:00Z\"}}"
            } else {
                body = "{\"id\":\"\(recipeId)\",\"revision\":\"1\",\"title\":\"Already saved\",\"description\":null,\"imageAssetId\":null,\"defaultServings\":\"2\",\"prepMinutes\":null,\"cookMinutes\":null,\"notes\":null,\"sourceUrl\":null,\"measurementStandard\":\"AU\",\"policyVersion\":1,\"archived\":false,\"favourite\":false,\"groups\":[],\"ingredients\":[],\"instructions\":[],\"collectionIds\":[],\"tagIds\":[]}"
            }
            return (HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: nil, headerFields: nil)!, Data(body.utf8))
        }
        let model = ImportReviewViewModel(selection: selection, intent: PendingImportIntent(capture: capture, sessionIdentity: selection.sessionIdentity), store: store)
        await model.start()
        #expect(model.savedRecipe?.id == recipeId); #expect(paths.values.count == 2)
        #expect(paths.values.first?.contains("import-handoffs") == true)
        #expect(try await store.load().entries.isEmpty)
    }
}
private final class ImportRequestLog: @unchecked Sendable {
    private let lock = NSLock(); private var storage: [String] = []
    func add(_ value: String) { lock.withLock { storage.append(value) } }
    var values: [String] { lock.withLock { storage } }
}
