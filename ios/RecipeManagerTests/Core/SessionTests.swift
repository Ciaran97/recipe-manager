import Foundation
import Testing
@testable import RecipeManager

@Suite(.serialized)
struct SessionTests {
    @Test func serverOriginRequiresHTTPSAndRejectsEmbeddedCredentials() throws {
        #expect(throws: SessionFailure.self) { try ServerOrigin("http://example.test") }
        #expect(throws: SessionFailure.self) { try ServerOrigin("https://user:password@example.test") }
        #expect(throws: SessionFailure.self) { try ServerOrigin("https://example.test/path") }
        #expect(try ServerOrigin("https://EXAMPLE.test:443/").key == "https://example.test")
    }

    @Test func expiryRemovesSavedCredential() async throws {
        let origin = try ServerOrigin("https://example.test")
        let store = MemoryCredentials()
        await store.save(SessionCredential(value: "expired", expiresAt: .distantPast), origin: origin.key)
        let session = SessionActor(origin: origin, store: store)
        try await session.restore()
        #expect(await session.beginRequest().cookieHeader == nil)
        #expect(await store.load(origin: origin.key) == nil)
    }

    @Test func logoutPreventsLateRenewalAndNeverPersistsPasswords() async throws {
        let origin = try ServerOrigin("https://example.test")
        let store = MemoryCredentials()
        let session = SessionActor(origin: origin, store: store)
        let request = await session.beginRequest()
        try await session.clear()
        try await session.accept(request, response: response(cookie: "late"))
        #expect(await store.load(origin: origin.key) == nil)
        #expect(await session.beginRequest().cookieHeader == nil)
    }

    @Test func olderResponseCannotOverwriteNewerSessionCookie() async throws {
        let origin = try ServerOrigin("https://example.test")
        let store = MemoryCredentials()
        let session = SessionActor(origin: origin, store: store)
        let older = await session.beginRequest()
        let newer = await session.beginRequest()
        try await session.accept(newer, response: response(cookie: "newer"))
        try await session.accept(older, response: response(cookie: "older"))
        #expect(await session.beginRequest().cookieHeader == "RecipeManager.Session=newer")
        #expect(await store.load(origin: origin.key)?.value == "newer")
        #expect(await store.load(origin: "https://other.test") == nil)
    }

    @Test func signInTransitionPreservesCookieAndRejectsPreSignInRenewal() async throws {
        let origin = try ServerOrigin("https://example.test")
        let session = SessionActor(origin: origin, store: MemoryCredentials())
        let old = await session.beginRequest()
        try await session.accept(await session.beginRequest(), response: response(cookie: "signed-in"))
        await session.authenticatedTransition()
        try await session.accept(old, response: response(cookie: "old"))
        #expect(await session.beginRequest().cookieHeader == "RecipeManager.Session=signed-in")
    }

    @Test func unsafeRequestsBootstrapCSRFAndSendStringQuantities() async throws {
        let origin = try ServerOrigin("https://example.test")
        let session = SessionActor(origin: origin, store: MemoryCredentials())
        let config = URLSessionConfiguration.ephemeral
        config.protocolClasses = [StubURLProtocol.self]
        StubURLProtocol.set { request in
            if request.url!.path == "/api/v1/auth/csrf" {
                return (HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: nil, headerFields: ["Set-Cookie": "RecipeManager.Csrf=cookie-fixture; Secure; HttpOnly; Path=/; SameSite=Strict"])!, Data("{\"token\":\"csrf-fixture\"}".utf8))
            }
            #expect(request.value(forHTTPHeaderField: "Cookie") == "RecipeManager.Csrf=cookie-fixture")
            #expect(request.value(forHTTPHeaderField: "X-XSRF-TOKEN") == "csrf-fixture")
            #expect(request.value(forHTTPHeaderField: "If-Match") == "\"recipe:2\"")
            #expect(String(data: Self.requestBody(request), encoding: .utf8) == "{\"servings\":\"1.5\"}")
            return (HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: nil, headerFields: [:])!, Data("{}".utf8))
        }
        let api = APIClient(session: session, configuration: config)
        _ = try await api.send(path: "/api/v1/recipes/a", method: "PUT", body: Data("{\"servings\":\"1.5\"}".utf8), etag: "\"recipe:2\"")
    }

    @Test func unauthorizedResponseRevokesLocalCredential() async throws {
        let origin = try ServerOrigin("https://example.test")
        let store = MemoryCredentials()
        let session = SessionActor(origin: origin, store: store)
        try await session.accept(await session.beginRequest(), response: response(cookie: "active"))
        let config = URLSessionConfiguration.ephemeral
        config.protocolClasses = [StubURLProtocol.self]
        StubURLProtocol.set { request in
            (HTTPURLResponse(url: request.url!, statusCode: 401, httpVersion: nil, headerFields: [:])!, Data())
        }
        let api = APIClient(session: session, configuration: config)
        await #expect(throws: SessionFailure.self) { try await api.send(path: "/api/v1/auth/me") }
        #expect(await session.beginRequest().cookieHeader == nil)
        #expect(await store.load(origin: origin.key) == nil)
    }

    @Test func redirectsAreNotFollowedAndCancellationPropagates() async throws {
        let origin = try ServerOrigin("https://example.test")
        let session = SessionActor(origin: origin, store: MemoryCredentials())
        let config = URLSessionConfiguration.ephemeral
        config.protocolClasses = [StubURLProtocol.self]
        StubURLProtocol.set { request in
            (HTTPURLResponse(url: request.url!, statusCode: 302, httpVersion: nil, headerFields: ["Location": "https://foreign.test"])!, Data())
        }
        let api = APIClient(session: session, configuration: config)
        await #expect(throws: SessionFailure.self) { try await api.send(path: "/api/v1/recipes") }
        let cancelled = Task { try Task.checkCancellation(); return try await api.send(path: "/api/v1/recipes") }
        cancelled.cancel()
        do { _ = try await cancelled.value; Issue.record("Expected cancellation") }
        catch is CancellationError {} catch let error as URLError { #expect(error.code == .cancelled) }
        catch { Issue.record("Unexpected cancellation error: \(type(of: error))") }
    }

    private static func requestBody(_ request: URLRequest) -> Data {
        if let body = request.httpBody { return body }
        guard let stream = request.httpBodyStream else { return Data() }
        stream.open()
        defer { stream.close() }
        var body = Data()
        var buffer = [UInt8](repeating: 0, count: 1024)
        while stream.hasBytesAvailable {
            let count = stream.read(&buffer, maxLength: buffer.count)
            if count <= 0 { break }
            body.append(contentsOf: buffer.prefix(count))
        }
        return body
    }

    private func response(cookie: String) -> HTTPURLResponse {
        HTTPURLResponse(url: URL(string: "https://example.test/api/v1/auth/login")!, statusCode: 200, httpVersion: nil,
            headerFields: ["Set-Cookie": "RecipeManager.Session=\(cookie); Path=/; Secure; HttpOnly; SameSite=Strict"])!
    }
}

actor MemoryCredentials: CredentialStore {
    private var values: [String: SessionCredential] = [:]
    func load(origin: String) -> SessionCredential? { values[origin] }
    func save(_ credential: SessionCredential, origin: String) { values[origin] = credential }
    func remove(origin: String) { values.removeValue(forKey: origin) }
}

final class StubURLProtocol: URLProtocol, @unchecked Sendable {
    private final class State: @unchecked Sendable {
        let lock = NSLock()
        var handlers: [String: @Sendable (URLRequest) throws -> (HTTPURLResponse, Data)] = [:]
    }
    private static let state = State()
    static func set(host: String = "example.test", _ handler: @escaping @Sendable (URLRequest) throws -> (HTTPURLResponse, Data)) {
        state.lock.withLock { state.handlers[host] = handler }
    }
    override class func canInit(with request: URLRequest) -> Bool { true }
    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }
    override func startLoading() {
        do {
            let handler = Self.state.lock.withLock { Self.state.handlers[request.url!.host!]! }
            let (response, body) = try handler(request)
            client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
            client?.urlProtocol(self, didLoad: body)
            client?.urlProtocolDidFinishLoading(self)
        } catch { client?.urlProtocol(self, didFailWithError: error) }
    }
    override func stopLoading() {}
}
