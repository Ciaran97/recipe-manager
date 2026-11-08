import Foundation
import RecipeManagerAPI

private final class NoRedirects: NSObject, URLSessionTaskDelegate, @unchecked Sendable {
    func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse,
        newRequest request: URLRequest, completionHandler: @escaping @Sendable (URLRequest?) -> Void) {
        completionHandler(nil)
    }
}

struct APIProblem: Decodable, Sendable {
    struct Field: Decodable, Sendable { let path: String?; let code: String?; let message: String }
    struct Conflict: Decodable, Sendable { let resourceId: String?; let reason: String? }
    let code: String?
    let errors: [Field]?
    let conflicts: [Conflict]?
}
struct APIRejection: Error, Sendable { let status: Int; let problem: APIProblem?; let data: Data }
private struct CsrfCredentials: Sendable { let token: String; let cookie: String? }

struct APIResponse: Sendable { let data: Data; let status: Int; let etag: String? }

actor APIClient {
    private let session: SessionActor
    private let network: URLSession
    private var csrf: (generation: UUID, credentials: CsrfCredentials)?
    private var bootstrap: (generation: UUID, task: Task<CsrfCredentials, Error>)?

    init(session: SessionActor, configuration: URLSessionConfiguration = .ephemeral) {
        self.session = session
        configuration.httpCookieStorage = nil
        configuration.httpShouldSetCookies = false
        configuration.urlCache = nil
        configuration.requestCachePolicy = .reloadIgnoringLocalCacheData
        self.network = URLSession(configuration: configuration, delegate: NoRedirects(), delegateQueue: nil)
    }

    func send(path: String, method: String = "GET", body: Data? = nil, etag: String? = nil, retryKey: UUID? = nil, contentType: String = "application/json") async throws -> APIResponse {
        try Task.checkCancellation()
        guard path.hasPrefix("/api/v1/"), !path.contains("\\"), !path.contains("#"),
            let relative = URLComponents(string: path), relative.scheme == nil, relative.host == nil else { throw SessionFailure.invalidPath }
        let context = await session.beginRequest()
        let unsafe = !["GET", "HEAD", "OPTIONS"].contains(method)
        let csrf = unsafe ? try await csrfToken(context) : nil
        guard await session.isCurrent(context) else { throw SessionFailure.sessionChanged }
        let url = context.origin.url.appendingPathComponent(relative.path)
        var components = URLComponents(url: url, resolvingAgainstBaseURL: false)!
        components.percentEncodedQuery = relative.percentEncodedQuery
        var request = URLRequest(url: components.url!)
        request.httpMethod = method
        request.httpBody = body
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        if body != nil { request.setValue(contentType, forHTTPHeaderField: "Content-Type") }
        var cookies = [context.cookieHeader].compactMap { $0 }
        if let cookie = csrf?.cookie { cookies.append(cookie) }
        if !cookies.isEmpty { request.setValue(cookies.joined(separator: "; "), forHTTPHeaderField: "Cookie") }
        if let token = csrf?.token { request.setValue(token, forHTTPHeaderField: "X-XSRF-TOKEN") }
        if let etag { request.setValue(etag, forHTTPHeaderField: unsafe ? "If-Match" : "If-None-Match") }
        if let retryKey { request.setValue(retryKey.uuidString, forHTTPHeaderField: "Idempotency-Key") }
        let (data, rawResponse) = try await network.data(for: request)
        guard let response = rawResponse as? HTTPURLResponse, sameOrigin(response.url, context.origin) else { throw SessionFailure.invalidResponse }
        guard await session.isCurrent(context) else { throw SessionFailure.sessionChanged }
        if response.statusCode == 401 {
            self.csrf = nil; bootstrap?.task.cancel(); bootstrap = nil
            try await session.clear()
            throw SessionFailure.authenticationRequired
        }
        guard !(300...399).contains(response.statusCode) || response.statusCode == 304 else { throw SessionFailure.redirect }
        try await session.accept(context, response: response)
        guard (200...299).contains(response.statusCode) || response.statusCode == 304 else { throw APIRejection(status: response.statusCode, problem: try? JSONDecoder().decode(APIProblem.self, from: data), data: data) }
        return APIResponse(data: data, status: response.statusCode, etag: response.value(forHTTPHeaderField: "ETag"))
    }

    private func csrfToken(_ context: RequestSession) async throws -> CsrfCredentials {
        if let csrf, csrf.generation == context.generation { return csrf.credentials }
        if let bootstrap, bootstrap.generation == context.generation { return try await bootstrap.task.value }
        let task = Task { try await self.fetchCsrf(context) }
        bootstrap = (context.generation, task)
        do {
            let credentials = try await task.value
            guard await session.isCurrent(context) else { throw SessionFailure.sessionChanged }
            csrf = (context.generation, credentials)
            if bootstrap?.generation == context.generation { bootstrap = nil }
            return credentials
        } catch {
            if bootstrap?.generation == context.generation { bootstrap = nil }
            throw error
        }
    }

    private func fetchCsrf(_ context: RequestSession) async throws -> CsrfCredentials {
        var request = URLRequest(url: context.origin.url.appendingPathComponent("api/v1/auth/csrf"))
        request.setValue(context.cookieHeader, forHTTPHeaderField: "Cookie")
        let (data, rawResponse) = try await network.data(for: request)
        guard let response = rawResponse as? HTTPURLResponse, sameOrigin(response.url, context.origin), response.statusCode == 200,
            await session.isCurrent(context) else { throw SessionFailure.invalidResponse }
        let fields = response.allHeaderFields.reduce(into: [String: String]()) { result, pair in
            if let name = pair.key as? String, let value = pair.value as? String { result[name] = value }
        }
        let cookie = HTTPCookie.cookies(withResponseHeaderFields: fields, for: context.origin.url)
            .first(where: { $0.name == "RecipeManager.Csrf" && $0.isSecure })
            .map { "\($0.name)=\($0.value)" }
        return CsrfCredentials(token: try JSONDecoder().decode(Components.Schemas.CsrfBootstrap.self, from: data).token, cookie: cookie)
    }

    private func sameOrigin(_ url: URL?, _ origin: ServerOrigin) -> Bool {
        guard let url else { return false }
        return url.scheme == "https" && url.host?.lowercased() == origin.url.host?.lowercased()
            && (url.port ?? 443) == (origin.url.port ?? 443)
    }

    func authenticationEstablished() async {
        bootstrap?.task.cancel(); bootstrap = nil; csrf = nil
        await session.authenticatedTransition()
    }

    func reset() async throws {
        bootstrap?.task.cancel(); bootstrap = nil; csrf = nil
        try await session.clear()
        network.invalidateAndCancel()
    }
}
