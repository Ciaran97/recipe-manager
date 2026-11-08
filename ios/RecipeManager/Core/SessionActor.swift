import Foundation

enum SessionFailure: Error { case invalidOrigin, invalidPath, sessionChanged, redirect, authenticationRequired, invalidResponse, storage, http(Int) }

struct ServerOrigin: Sendable, Equatable {
    let url: URL
    let key: String
    init(_ input: String) throws {
        guard var parts = URLComponents(string: input), parts.scheme?.lowercased() == "https",
              let host = parts.host, !host.isEmpty, parts.user == nil, parts.password == nil,
              parts.query == nil, parts.fragment == nil, parts.path.isEmpty || parts.path == "/" else {
            throw SessionFailure.invalidOrigin
        }
        parts.scheme = "https"
        parts.host = host.lowercased()
        parts.path = ""
        if parts.port == 443 { parts.port = nil }
        guard let url = parts.url else { throw SessionFailure.invalidOrigin }
        self.url = url
        self.key = url.absoluteString
    }
}

struct SessionCredential: Codable, Sendable {
    let value: String
    let expiresAt: Date
}

protocol CredentialStore: Sendable {
    func load(origin: String) async throws -> SessionCredential?
    func save(_ credential: SessionCredential, origin: String) async throws
    func remove(origin: String) async throws
}

struct RequestSession: Sendable {
    let origin: ServerOrigin
    let generation: UUID
    let sequence: UInt64
    let cookieHeader: String?
}

actor SessionActor {
    let origin: ServerOrigin
    private let store: any CredentialStore
    private var credential: SessionCredential?
    private var generation = UUID()
    private var sequence: UInt64 = 0
    private var lastRenewal: UInt64 = 0

    init(origin: ServerOrigin, store: any CredentialStore) { self.origin = origin; self.store = store }

    func restore() async throws {
        let expected = generation
        let saved = try await store.load(origin: origin.key)
        guard generation == expected else { return }
        if let saved, saved.expiresAt > Date(), !saved.value.contains(";") && !saved.value.contains("\n") {
            credential = saved
        } else {
            credential = nil
            try await store.remove(origin: origin.key)
        }
    }

    func beginRequest() async -> RequestSession {
        sequence += 1
        if let current = credential, current.expiresAt <= Date() {
            generation = UUID(); credential = nil
            try? await store.remove(origin: origin.key)
        }
        return RequestSession(origin: origin, generation: generation, sequence: sequence,
            cookieHeader: credential.map { "RecipeManager.Session=\($0.value)" })
    }

    func isCurrent(_ request: RequestSession) -> Bool { request.generation == generation && request.origin == origin }

    func accept(_ request: RequestSession, response: HTTPURLResponse) async throws {
        guard isCurrent(request), request.sequence >= lastRenewal, response.url?.host == origin.url.host else { return }
        let fields = response.allHeaderFields.reduce(into: [String: String]()) { result, pair in
            if let name = pair.key as? String, let value = pair.value as? String { result[name] = value }
        }
        guard let cookie = HTTPCookie.cookies(withResponseHeaderFields: fields, for: origin.url)
            .first(where: { $0.name == "RecipeManager.Session" }) else { return }
        guard cookie.isSecure, cookie.isHTTPOnly, cookie.path == "/",
            cookie.domain.trimmingCharacters(in: CharacterSet(charactersIn: ".")) == origin.url.host else { return }
        lastRenewal = request.sequence
        let saved = SessionCredential(value: cookie.value, expiresAt: min(cookie.expiresDate ?? Date().addingTimeInterval(14 * 86400), Date().addingTimeInterval(14 * 86400)))
        credential = saved.expiresAt > Date() && !saved.value.isEmpty ? saved : nil
        if let credential { try await store.save(credential, origin: origin.key) }
        else { try await store.remove(origin: origin.key) }
        // Store actors serialize operations. A racing logout is queued after this save and removes it.
        if !isCurrent(request) { try await store.remove(origin: origin.key) }
    }

    func authenticatedTransition() {
        generation = UUID()
        lastRenewal = sequence
    }

    func clear() async throws {
        generation = UUID()
        credential = nil
        lastRenewal = 0
        try await store.remove(origin: origin.key)
    }
}
