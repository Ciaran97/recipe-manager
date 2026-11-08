import Foundation

struct IncomingInvitation: Identifiable, Sendable {
    let id = UUID()
    let origin: ServerOrigin
    let token: String
    init(url: URL) throws {
        guard let parts = URLComponents(url: url, resolvingAgainstBaseURL: false) else { throw SessionFailure.invalidOrigin }
        let values = parts.queryItems ?? []
        if url.scheme == "recipemanager", url.host == "invite", let server = values.first(where: { $0.name == "origin" })?.value {
            origin = try ServerOrigin(server)
        } else if url.scheme == "https", url.path == "/invite" {
            var base = parts; base.path = ""; base.query = nil; base.fragment = nil
            origin = try ServerOrigin(base.string ?? "")
        } else { throw SessionFailure.invalidOrigin }
        let token = values.first(where: { $0.name == "token" })?.value ?? ""
        guard token.count <= 1024, url.scheme == "recipemanager" || !token.isEmpty else { throw SessionFailure.invalidResponse }
        self.token = token
    }
    static func token(from input: String, expected: ServerOrigin?) throws -> String {
        let value = input.trimmingCharacters(in: .whitespacesAndNewlines)
        if value.contains("://") || value.contains("?token=") {
            guard let url = URL(string: value, relativeTo: expected?.url)?.absoluteURL else { throw SessionFailure.invalidResponse }
            let incoming = try IncomingInvitation(url: url)
            guard incoming.origin == expected else { throw SessionFailure.invalidOrigin }
            return incoming.token
        }
        guard !value.isEmpty, value.count <= 1024 else { throw SessionFailure.invalidResponse }
        return value
    }
}
