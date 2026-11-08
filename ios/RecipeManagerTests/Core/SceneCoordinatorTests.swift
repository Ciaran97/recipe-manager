import Foundation
import Testing
@testable import RecipeManager

@Suite(.serialized) @MainActor struct SceneCoordinatorTests {
    @Test func unauthorizedForegroundPollClearsTheCurrentAccount() async throws {
        let configuration = URLSessionConfiguration.ephemeral; configuration.protocolClasses = [StubURLProtocol.self]
        let selection = ServerSelection(credentials: MemoryCredentials(), persist: { _ in }, makeClient: { APIClient(session: $0, configuration: configuration) })
        await selection.requestChange("https://scene-sync.example.test")
        await selection.signedIn(ClientUser(id: UUID(), householdId: UUID(), role: .member))
        StubURLProtocol.set(host: "scene-sync.example.test") { request in
            (HTTPURLResponse(url: request.url!, statusCode: 401, httpVersion: nil, headerFields: nil)!, Data())
        }
        let scene = SceneCoordinator(selection: selection)
        await scene.update(active: true)
        #expect(await eventually { await MainActor.run { selection.user == nil } })
        await scene.update(active: false)
        #expect(scene.change == nil)
    }
    @Test func restoringAnAccountUnderAnAlreadyBoundIdentityStartsThePoller() async throws {
        let configuration = URLSessionConfiguration.ephemeral; configuration.protocolClasses = [StubURLProtocol.self]
        let selection = ServerSelection(credentials: MemoryCredentials(), persist: { _ in }, makeClient: { APIClient(session: $0, configuration: configuration) })
        let identity = selection.sessionIdentity
        StubURLProtocol.set(host: "scene-restore.example.test") { request in
            let data = request.url?.path == "/api/v1/auth/me"
                ? Data("{\"id\":\"00000000-0000-0000-0000-000000000001\",\"householdId\":\"00000000-0000-0000-0000-000000000002\",\"role\":\"Member\"}".utf8)
                : syncResponse().data
            return (HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: nil, headerFields: ["ETag": "\"compound\""])!, data)
        }
        let scene = SceneCoordinator(selection: selection)
        await scene.update(active: true)
        await selection.restore("https://scene-restore.example.test")
        #expect(selection.user != nil); #expect(selection.sessionIdentity == identity)
        await scene.update(active: true)
        #expect(await eventually { await MainActor.run { scene.change != nil } })
        await scene.update(active: false)
    }

}
