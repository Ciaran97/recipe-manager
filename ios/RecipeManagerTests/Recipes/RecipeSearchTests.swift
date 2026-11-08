import Foundation
import Testing
import RecipeManagerAPI
@testable import RecipeManager

@Suite(.serialized) @MainActor struct RecipeSearchTests {
    private func selection(_ host: String) async -> ServerSelection {
        let configuration = URLSessionConfiguration.ephemeral; configuration.protocolClasses = [StubURLProtocol.self]
        let selection = ServerSelection(credentials: MemoryCredentials(), persist: { _ in }, makeClient: { APIClient(session: $0, configuration: configuration) })
        await selection.requestChange("https://" + host); await selection.signedIn(ClientUser(id: UUID(), householdId: UUID(), role: .member)); return selection
    }
    @Test func inlineOrganiserCreationRetainsExistingChoices() async throws {
        let selection = await selection("inline-organisers.example.test")
        let id = UUID().uuidString
        StubURLProtocol.set(host: "inline-organisers.example.test") { request in
            let body: String
            if request.url?.path == "/api/v1/auth/csrf" { body = "{\"token\":\"csrf-fixture\"}" }
            else {
                #expect(request.httpMethod == "POST")
                #expect(request.url?.path == "/api/v1/tags")
                #expect(request.value(forHTTPHeaderField: "Idempotency-Key") != nil)
                body = "{\"id\":\"\(id)\",\"name\":\"Quick dinner\",\"revision\":\"1\"}"
            }
            return (HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: nil, headerFields: nil)!, Data(body.utf8))
        }
        let model = RecipeOrganiserOptions(selection: selection)
        model.tags = [.init(id: "existing", name: "Existing", revision: "1")]
        let created = await model.create("tags", name: "Quick dinner")
        #expect(created == id); #expect(model.tags.map(\.id) == ["existing", id]); #expect(model.error == nil)
    }
    @Test func pagingPreservesAllSelectionsAndChangedFiltersStartAtFirstPage() async throws {
        let host = "recipe-search.example.test", selection = await selection("recipe-search.example.test")
        let paths = SearchRequestLog()
        StubURLProtocol.set(host: host) { request in
            paths.add(request.url!)
            return (HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: nil, headerFields: nil)!, Data("{\"items\":[],\"nextCursor\":\"next-page\"}".utf8))
        }
        let model = RecipeLibraryModel(selection: selection)
        model.collectionIds = ["second", "first"]; model.tagIds = ["tag"]; model.query = "rice & beans"; model.favouriteOnly = true
        await model.load(); await model.load(append: true)
        #expect(model.collectionIds == ["second", "first"]); #expect(model.tagIds == ["tag"])
        let second = URLComponents(url: try #require(paths.urls.last), resolvingAgainstBaseURL: false)!.queryItems!
        #expect(second.filter { $0.name == "collectionIds" }.compactMap(\.value).sorted() == ["first", "second"])
        #expect(second.first { $0.name == "q" }?.value == "rice & beans")
        #expect(second.first { $0.name == "cursor" }?.value == "next-page")
        model.query = "new search"; await model.load(append: true)
        let changed = URLComponents(url: try #require(paths.urls.last), resolvingAgainstBaseURL: false)!.queryItems!
        #expect(!changed.contains { $0.name == "cursor" })
    }
    @Test func cancelledSearchCannotInstallLateResultsOrReplaceVisibleState() async throws {
        let host = "cancelled-search.example.test", selection = await selection("cancelled-search.example.test")
        StubURLProtocol.set(host: host) { request in
            Thread.sleep(forTimeInterval: 0.05)
            return (HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: nil, headerFields: nil)!, Data("{\"items\":[],\"nextCursor\":\"stale\"}".utf8))
        }
        let model = RecipeLibraryModel(selection: selection)
        let work = Task { await model.load() }
        try await Task.sleep(for: .milliseconds(5)); model.cancel(); await work.value
        #expect(model.cursor == nil); #expect(!model.loading); #expect(model.error == nil)
    }
}
private final class SearchRequestLog: @unchecked Sendable {
    private let lock = NSLock(); private var values: [URL] = []
    func add(_ value: URL) { lock.withLock { values.append(value) } }
    var urls: [URL] { lock.withLock { values } }
}
