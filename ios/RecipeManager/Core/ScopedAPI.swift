import Foundation

/// Binds an editing/reading workflow to its original account and server. In particular, delayed
/// photo preparation must never upload an old household's content to a newly selected server.
@MainActor struct ScopedAPI {
    private let selection: ServerSelection
    private let context: UUID
    init(selection: ServerSelection) { self.selection = selection; context = selection.sessionIdentity }
    func send(path: String, method: String = "GET", body: Data? = nil, etag: String? = nil, retryKey: UUID? = nil, contentType: String = "application/json") async throws -> APIResponse {
        try await selection.request(path: path, method: method, body: body, etag: etag, retryKey: retryKey, contentType: contentType, context: context)
    }
}
