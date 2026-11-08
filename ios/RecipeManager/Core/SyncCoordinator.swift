import Foundation
import RecipeManagerAPI

enum SyncDomain: Sendable, Hashable { case recipes, planner, shopping, settings, membership }
struct SyncDelta: Sendable {
    let sequence: UInt64
    let revisions: Components.Schemas.HouseholdSyncRead
    let domains: Set<SyncDomain>
    let personal: Bool
    let initial: Bool
}
enum SyncEvent: Sendable { case changed(SyncDelta), authenticationRequired }

/// One foreground polling task, scoped by its owner to one authenticated account/server.
/// Responses contain revision metadata only; feature models fetch authoritative accepted data.
actor SyncCoordinator {
    typealias Fetch = @Sendable (String?) async throws -> APIResponse
    typealias Deliver = @Sendable (SyncEvent) async -> Void
    typealias Pause = @Sendable (Duration) async throws -> Void
    private let fetch: Fetch
    private let deliver: Deliver
    private let pause: Pause
    private let jitter: @Sendable () -> Double
    private var runner: Task<Void, Never>?
    private var generation: UInt64 = 0
    private var sequence: UInt64 = 0
    private var tag: String?
    private var accepted: Components.Schemas.HouseholdSyncRead?
    init(fetch: @escaping Fetch, deliver: @escaping Deliver,
         pause: @escaping Pause = { try await Task.sleep(for: $0) },
         jitter: @escaping @Sendable () -> Double = { Double.random(in: 0...0.2) }) {
        self.fetch = fetch; self.deliver = deliver; self.pause = pause; self.jitter = jitter
    }
    func start() {
        guard runner == nil else { return }
        generation &+= 1; let current = generation
        runner = Task { await self.run(current) }
    }
    func stop() async {
        generation &+= 1; let stoppedAt = generation, old = runner
        old?.cancel()
        await old?.value
        if generation == stoppedAt { runner = nil }
    }
    nonisolated static func delay(failures: Int, jitter: Double) -> Duration {
        if failures == 0 { return .seconds(1) }
        let seconds = min(30, pow(2, Double(min(max(1, failures), 5))) + min(0.2, max(0, jitter)))
        return .seconds(seconds)
    }
    private func run(_ current: UInt64) async {
        defer { if generation == current { runner = nil } }
        var failures = 0
        var revalidate = accepted != nil
        while generation == current && !Task.isCancelled {
            do {
                let response = try await fetch(tag)
                guard generation == current && !Task.isCancelled else { return }
                if response.status == 200 {
                    guard let tag = response.etag, !tag.isEmpty else { throw SessionFailure.invalidResponse }
                    let next = try APIJSON.decoder().decode(Components.Schemas.HouseholdSyncRead.self, from: response.data)
                    let previous = accepted
                    let changed = revalidate ? Self.allDomains : Self.changed(previous, next)
                    let personal = revalidate || previous?.ownPreferencesRevision != next.ownPreferencesRevision
                    self.tag = tag; accepted = next
                    if previous == nil || !changed.isEmpty || personal {
                        sequence &+= 1
                        await deliver(.changed(.init(sequence: sequence, revisions: next, domains: changed, personal: personal, initial: previous == nil)))
                    }
                } else if response.status == 304 {
                    // A reply may have been accepted just before background cancellation while
                    // its UI delivery was suppressed. Revalidate visible state on every restart.
                    if revalidate, let accepted {
                        sequence &+= 1
                        await deliver(.changed(.init(sequence: sequence, revisions: accepted, domains: Self.allDomains, personal: true, initial: false)))
                    }
                } else { throw SessionFailure.invalidResponse }
                revalidate = false; failures = 0
            } catch {
                guard generation == current && !Task.isCancelled else { return }
                if case SessionFailure.authenticationRequired = error {
                    await deliver(.authenticationRequired); return
                }
                if let rejected = error as? APIRejection, rejected.status == 401 || rejected.status == 403 {
                    await deliver(.authenticationRequired); return
                }
                failures += 1
            }
            do { try await pause(Self.delay(failures: failures, jitter: jitter())) }
            catch { return }
        }
    }
    private static let allDomains: Set<SyncDomain> = [.recipes, .planner, .shopping, .settings, .membership]
    private static func changed(_ previous: Components.Schemas.HouseholdSyncRead?, _ next: Components.Schemas.HouseholdSyncRead) -> Set<SyncDomain> {
        var result = Set<SyncDomain>()
        if previous?.recipesRevision != next.recipesRevision { result.insert(.recipes) }
        if previous?.plannerRevision != next.plannerRevision { result.insert(.planner) }
        if previous?.shoppingRevision != next.shoppingRevision { result.insert(.shopping) }
        if previous?.settingsRevision != next.settingsRevision { result.insert(.settings) }
        if previous?.membershipRevision != next.membershipRevision { result.insert(.membership) }
        return result
    }
}
