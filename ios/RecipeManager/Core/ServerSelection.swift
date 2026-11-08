import Foundation
import Observation

enum ClientRole: String, Codable, Sendable { case administrator = "Administrator", member = "Member" }
struct ClientUser: Decodable, Sendable { let id: UUID; let householdId: UUID; let role: ClientRole }

@Observable @MainActor
final class ServerSelection {
    private(set) var sessionIdentity = UUID()
    private(set) var pendingInvitation: IncomingInvitation?
    private(set) var invitationToken: String?
    private(set) var origin: ServerOrigin?
    private(set) var api: APIClient?
    private(set) var user: ClientUser?
    private(set) var pendingOrigin: ServerOrigin?
    private(set) var isLoading = false
    var hasPendingInput = false
    var error: String?
    private var transition = UUID()
    private var restoringOrigin: ServerOrigin?
    private let credentials: any CredentialStore
    private let makeClient: @MainActor (SessionActor) -> APIClient
    private let persist: @MainActor (String) -> Void

    init(credentials: any CredentialStore = KeychainStore(), persist: @escaping @MainActor (String) -> Void = { UserDefaults.standard.set($0, forKey: "serverOrigin") }, makeClient: @escaping @MainActor (SessionActor) -> APIClient = { APIClient(session: $0) }) {
        self.credentials = credentials
        self.persist = persist
        self.makeClient = makeClient
    }

    func requestChange(_ text: String) async {
        do {
            let next = try ServerOrigin(text.trimmingCharacters(in: .whitespacesAndNewlines))
            guard next != origin || api == nil else { return }
            if hasPendingInput { pendingOrigin = next; return }
            try await apply(next)
        } catch SessionFailure.sessionChanged { }
        catch { self.error = apiErrorMessage(error) }
    }

    func cancelChange() { pendingOrigin = nil }
    func confirmChange(_ selected: ServerOrigin? = nil) async {
        guard let pendingOrigin = selected ?? pendingOrigin else { return }
        self.pendingOrigin = nil
        hasPendingInput = false
        do { try await apply(pendingOrigin) }
        catch SessionFailure.sessionChanged { }
        catch { self.error = "Could not clear the previous session. Try again." }
    }

    private func beginTransition() -> UUID { transition = UUID(); isLoading = true; return transition }
    private func requireTransition(_ value: UUID) throws { if transition != value { throw SessionFailure.sessionChanged } }
    private func apply(_ next: ServerOrigin) async throws {
        let oldClient = api, oldOrigin = restoringOrigin ?? origin
        let current = beginTransition()
        // Stop old views/polls immediately, while retaining the cleanup origin until removal succeeds.
        sessionIdentity = UUID(); user = nil; api = nil; restoringOrigin = oldOrigin
        defer { if transition == current { isLoading = false } }
        do {
            try await oldClient?.reset(); try requireTransition(current)
            if let oldOrigin { try await credentials.remove(origin: oldOrigin.key); try requireTransition(current) }
            let session = SessionActor(origin: next, store: credentials)
            try await session.clear(); try requireTransition(current)
            restoringOrigin = nil; origin = next; api = makeClient(session)
            sessionIdentity = UUID(); persist(next.key); error = nil
        } catch {
            try requireTransition(current)
            throw error
        }
    }

    func restore(_ text: String?) async {
        guard origin == nil, api == nil, !isLoading, let text, let saved = try? ServerOrigin(text) else { return }
        let current = beginTransition(); restoringOrigin = saved
        defer { if transition == current { isLoading = false; restoringOrigin = nil } }
        let session = SessionActor(origin: saved, store: credentials)
        do {
            try await session.restore(); try requireTransition(current)
            let client = makeClient(session)
            origin = saved; api = client
            let response = try await client.send(path: "/api/v1/auth/me")
            try requireTransition(current)
            await client.authenticationEstablished(); try requireTransition(current)
            guard api === client, origin == saved else { return }
            user = try JSONDecoder().decode(ClientUser.self, from: response.data)
        } catch SessionFailure.sessionChanged { }
        catch SessionFailure.authenticationRequired { if transition == current { user = nil } }
        catch { if transition == current { self.error = "Could not reach your server. Check the address and connection." } }
    }

    func signedIn(_ user: ClientUser) async {
        guard let client = api else { return }
        let current = beginTransition()
        defer { if transition == current { isLoading = false } }
        await client.authenticationEstablished()
        guard transition == current, api === client else { return }
        sessionIdentity = UUID(); restoringOrigin = nil
        self.user = user; invitationToken = nil; hasPendingInput = false; error = nil
    }

    func receiveInvitation(_ url: URL) {
        do { pendingInvitation = try IncomingInvitation(url: url) }
        catch { self.error = "This invitation link could not be accepted. Confirm its server and try pasting it." }
    }
    func cancelInvitation() { pendingInvitation = nil }
    func confirmInvitation(_ invitation: IncomingInvitation) async {
        // The sheet explicitly confirms discarding input and clearing this device's saved account.
        guard await clearAccount() else { return }
        do {
            if origin != invitation.origin { try await apply(invitation.origin) }
            invitationToken = invitation.token
            hasPendingInput = false
            pendingInvitation = nil
            sessionIdentity = UUID()
        } catch { self.error = apiErrorMessage(error) }
    }

    func request(path: String, method: String = "GET", body: Data? = nil, etag: String? = nil, retryKey: UUID? = nil, contentType: String = "application/json", context: UUID? = nil) async throws -> APIResponse {
        if let context, context != sessionIdentity { throw SessionFailure.sessionChanged }
        guard let client = api else { throw SessionFailure.invalidOrigin }
        do { return try await client.send(path: path, method: method, body: body, etag: etag, retryKey: retryKey, contentType: contentType) }
        catch SessionFailure.authenticationRequired {
            if user != nil && api === client { await clearAccount(); error = "Your session ended. Sign in again." }
            throw SessionFailure.authenticationRequired
        }
    }

    @discardableResult func clearAccount() async -> Bool {
        let oldClient = api, selected = origin, cleanup = restoringOrigin ?? origin
        let current = beginTransition()
        sessionIdentity = UUID(); user = nil; api = nil; restoringOrigin = cleanup
        defer { if transition == current { isLoading = false } }
        do {
            try await oldClient?.reset(); try requireTransition(current)
            if let cleanup { try await credentials.remove(origin: cleanup.key); try requireTransition(current) }
            restoringOrigin = nil; hasPendingInput = false; invitationToken = nil
            if let selected { api = makeClient(SessionActor(origin: selected, store: credentials)) }
            sessionIdentity = UUID()
            return true
        } catch {
            guard transition == current else { return false }
            self.error = "Could not clear saved credentials. Unlock the device and try again."; return false
        }
    }
}
