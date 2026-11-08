import Foundation
import Observation
import RecipeManagerAPI
import ShareHandoff

struct PendingImportIntent: Sendable, Equatable {
    let capture: HandoffEnvelope
    let sessionIdentity: UUID
}
struct CaptureDestinationPreview: Identifiable, Sendable {
    var id: UUID { captureId }
    let captureId: UUID
    let sessionIdentity: UUID
    let binding: HandoffDestination
    let householdName: String
    let accountName: String
}

@Observable @MainActor final class PendingCaptureInboxModel {
    var entries: [HandoffEnvelope] = []
    var expiredCount = 0
    var destination: CaptureDestinationPreview?
    var busy = false
    var error: String?
    private let selection: ServerSelection
    private let store: HandoffStore?
    init(selection: ServerSelection, store: HandoffStore? = nil) {
        self.selection = selection
        self.store = store ?? (try? HandoffStore(directory: HandoffContainer.directory()))
    }
    func canRead(_ entry: HandoffEnvelope) -> Bool {
        guard let binding = entry.binding else { return true }
        return selection.origin?.key == binding.origin && selection.user?.id == binding.userId
            && selection.user?.householdId == binding.householdId
    }
    func load() async {
        guard !busy else { return }; busy = true; error = nil
        defer { busy = false }
        do {
            guard let store else { throw HandoffFailure.persistence }
            let result = try await store.load(); entries = result.entries; expiredCount = result.expired.count
        } catch { failure(error) }
    }
    func prepare(_ id: UUID) async {
        guard !busy else { return }; destination = nil; error = nil
        guard let user = selection.user, let origin = selection.origin else { error = "Sign in to your server before reviewing a capture."; return }
        guard let capture = entries.first(where: { $0.handoffId == id }) else { error = "This capture is no longer available. Reload saved captures."; return }
        guard canRead(capture) else { error = "This capture belongs to another account or server. Sign in there to continue, or explicitly discard it from this device."; return }
        busy = true; defer { busy = false }
        let context = selection.sessionIdentity
        let requests = ScopedAPI(selection: selection)
        do {
            let profile = try APIJSON.decoder().decode(Components.Schemas.ProfileRead.self, from: await requests.send(path: "/api/v1/profile").data)
            let household = try APIJSON.decoder().decode(Components.Schemas.HouseholdSettingsRead.self, from: await requests.send(path: "/api/v1/household/settings").data)
            guard selection.sessionIdentity == context, UUID(uuidString: profile.id) == user.id, UUID(uuidString: household.id) == user.householdId else { throw SessionFailure.sessionChanged }
            let binding = try HandoffDestination(origin: origin.key, householdId: user.householdId, userId: user.id)
            destination = .init(captureId: id, sessionIdentity: context, binding: binding, householdName: household.name,
                accountName: profile.name.isEmpty ? "Current \(user.role.rawValue) account" : profile.name)
        } catch { failure(error) }
    }
    func confirm() async -> PendingImportIntent? {
        guard !busy, let destination else { return nil }
        guard isCurrent(destination) else { self.destination = nil; error = apiErrorMessage(SessionFailure.sessionChanged); return nil }
        busy = true; error = nil; defer { busy = false }
        do {
            guard let store else { throw HandoffFailure.persistence }
            let capture = try await store.bind(destination.captureId, to: destination.binding)
            guard isCurrent(destination) else {
                entries = []; self.destination = nil
                throw SessionFailure.sessionChanged
            }
            self.destination = nil
            entries = entries.map { $0.handoffId == capture.handoffId ? capture : $0 }
            return .init(capture: capture, sessionIdentity: destination.sessionIdentity)
        } catch { failure(error); return nil }
    }
    func cancelDestination() { destination = nil }
    func accountChanged() { destination = nil; entries = []; error = nil }
    func discard(_ id: UUID) async {
        guard !busy else { return }; busy = true; error = nil
        defer { busy = false }
        do {
            guard let store else { throw HandoffFailure.persistence }
            try await store.discard(id); entries.removeAll { $0.handoffId == id }
            if destination?.captureId == id { destination = nil }
        } catch { failure(error) }
    }
    private func isCurrent(_ preview: CaptureDestinationPreview) -> Bool {
        selection.sessionIdentity == preview.sessionIdentity && selection.origin?.key == preview.binding.origin
            && selection.user?.id == preview.binding.userId && selection.user?.householdId == preview.binding.householdId
    }
    private func failure(_ error: Error) {
        guard let failure = error as? HandoffFailure else { self.error = apiErrorMessage(error); return }
        switch failure {
        case .destinationConflict: self.error = "This capture is already bound to another account or server."
        case .expired, .missingCapture: self.error = "This capture expired or was discarded. Reload saved captures."
        case .unsupportedSchema: self.error = "Update Recipe Manager to read these saved captures."
        case .corruptQueue: self.error = "Saved captures could not be read. They have been retained; retry or update Recipe Manager."
        default: self.error = "Could not access saved captures. Retry without discarding the original shared content."
        }
    }
}
