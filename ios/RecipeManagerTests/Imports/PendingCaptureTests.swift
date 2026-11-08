import Foundation
import Testing
import ShareHandoff
@testable import RecipeManager

@Suite(.serialized) @MainActor struct PendingCaptureTests {
    @Test func signedOutCaptureStaysLocalAndCannotBeSubmitted() async throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: root) }
        let store = HandoffStore(directory: root), capture = try HandoffEnvelope(kind: .text, content: "Salt to taste")
        _ = try await store.append(capture)
        let model = PendingCaptureInboxModel(selection: ServerSelection(credentials: MemoryCredentials(), persist: { _ in }), store: store)
        await model.load(); await model.prepare(capture.handoffId)
        #expect(model.entries.count == 1); #expect(model.destination == nil); #expect(model.error != nil)
        #expect(try await store.load().entries.first?.binding == nil)
    }
    @Test func anotherAccountCannotReadOrResumeABoundCapture() async throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: root) }
        let store = HandoffStore(directory: root), capture = try HandoffEnvelope(kind: .url, content: "https://recipes.example.test/recipe")
        _ = try await store.append(capture)
        let binding = try HandoffDestination(origin: "https://home.example.test", householdId: UUID(), userId: UUID())
        _ = try await store.bind(capture.handoffId, to: binding)
        let selection = ServerSelection(credentials: MemoryCredentials(), persist: { _ in })
        await selection.requestChange("https://home.example.test")
        await selection.signedIn(ClientUser(id: UUID(), householdId: UUID(), role: .member))
        let model = PendingCaptureInboxModel(selection: selection, store: store)
        await model.load(); let entry = try #require(model.entries.first)
        #expect(!model.canRead(entry)); await model.prepare(entry.handoffId)
        #expect(model.destination == nil); #expect(model.error != nil)
        #expect(try await store.load().entries.first?.binding == binding)
    }
    @Test func destinationConfirmationRejectsAnAccountChangeBeforeBinding() async throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: root) }
        let store = HandoffStore(directory: root), capture = try HandoffEnvelope(kind: .text, content: "Recipe text")
        _ = try await store.append(capture)
        let config = URLSessionConfiguration.ephemeral; config.protocolClasses = [StubURLProtocol.self]
        let selection = ServerSelection(credentials: MemoryCredentials(), persist: { _ in }, makeClient: { APIClient(session: $0, configuration: config) })
        let user = UUID(), household = UUID()
        await selection.requestChange("https://capture-review.example.test")
        await selection.signedIn(ClientUser(id: user, householdId: household, role: .member))
        StubURLProtocol.set(host: "capture-review.example.test") { request in
            let body = request.url?.path == "/api/v1/profile"
                ? "{\"id\":\"\(user)\",\"name\":\"Member\",\"preferences\":{},\"revision\":\"1\"}"
                : "{\"id\":\"\(household)\",\"name\":\"Home\",\"timezone\":\"UTC\",\"revision\":\"1\",\"ai\":{\"enabled\":false,\"provider\":\"OpenAICompatible\",\"model\":\"\",\"baseUrl\":\"\",\"hasApiKey\":false}}"
            return (HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: nil, headerFields: nil)!, Data(body.utf8))
        }
        let model = PendingCaptureInboxModel(selection: selection, store: store)
        await model.load(); await model.prepare(capture.handoffId)
        #expect(model.destination?.householdName == "Home")
        await selection.signedIn(ClientUser(id: UUID(), householdId: UUID(), role: .member))
        #expect(await model.confirm() == nil)
        #expect(try await store.load().entries.first?.binding == nil)
    }
}
