import Foundation
import Observation
import Network
import SwiftUI

@Observable @MainActor final class SceneCoordinator {
    private(set) var change: SyncDelta?
    private let selection: ServerSelection
    private var sync: SyncCoordinator?
    private var bound: UUID?
    private var epoch: UInt64 = 0
    private var active = false
    private var monitor: NWPathMonitor?
    private var reachable: Bool?
    init(selection: ServerSelection) { self.selection = selection }

    func update(active: Bool) async {
        self.active = active; epoch &+= 1; let current = epoch
        let context = selection.sessionIdentity
        if bound != context {
            let old = sync; sync = nil; bound = context; change = nil
            stopMonitor(); await old?.stop()
            guard epoch == current else { return }
        }
        guard epoch == current else { return }
        // Restoration can authenticate under the identity already observed while signed out.
        if sync == nil && selection.user != nil {
            let requests = ScopedAPI(selection: selection)
            sync = SyncCoordinator(fetch: { tag in try await requests.send(path: "/api/v1/household/sync", etag: tag) },
                deliver: { [weak self] event in await self?.receive(event, context: context) })
        }
        if active && selection.user != nil {
            startMonitor(context: context); await sync?.start()
        } else {
            stopMonitor(); await sync?.stop()
            if selection.user == nil { change = nil; sync = nil }
        }
    }
    private func receive(_ event: SyncEvent, context: UUID) async {
        guard active, bound == context, selection.sessionIdentity == context, selection.user != nil else { return }
        switch event {
        case .changed(let delta): change = delta
        case .authenticationRequired:
            _ = await selection.clearAccount()
            change = nil
        }
    }
    private func startMonitor(context: UUID) {
        guard monitor == nil else { return }
        let monitor = NWPathMonitor(); self.monitor = monitor; reachable = nil
        monitor.pathUpdateHandler = { [weak self] path in
            let reachable = path.status == .satisfied
            Task { @MainActor [weak self] in
                guard let self, self.active, self.bound == context else { return }
                let previous = self.reachable; self.reachable = reachable
                // Reachability is only a recovery hint. Local servers may be reachable without Internet.
                if previous == false && reachable { await self.reconnect(context: context) }
            }
        }
        monitor.start(queue: DispatchQueue(label: "local.recipemanager.reachability"))
    }
    private func stopMonitor() { monitor?.cancel(); monitor = nil; reachable = nil }
    private func reconnect(context: UUID) async {
        epoch &+= 1; let current = epoch
        await sync?.stop()
        guard epoch == current, active, bound == context, selection.sessionIdentity == context, selection.user != nil else { return }
        await sync?.start()
    }
}

private struct HouseholdSyncEnvironmentKey: EnvironmentKey {
    static let defaultValue: SceneCoordinator? = nil
}
extension EnvironmentValues {
    var householdSync: SceneCoordinator? {
        get { self[HouseholdSyncEnvironmentKey.self] }
        set { self[HouseholdSyncEnvironmentKey.self] = newValue }
    }
}

private struct HouseholdInvalidationModifier: ViewModifier {
    @Environment(\.householdSync) private var scene
    @State private var visible = false
    let domains: Set<SyncDomain>
    let personal: Bool
    let perform: @MainActor (SyncDelta) -> Void
    func body(content: Content) -> some View {
        content.onAppear { visible = true }.onDisappear { visible = false }
            .onChange(of: scene?.change?.sequence) { _, _ in
                guard visible, let change = scene?.change, !change.initial,
                      !domains.isDisjoint(with: change.domains) || personal && change.personal else { return }
                perform(change)
            }
    }
}
extension View {
    func onHouseholdChange(domains: Set<SyncDomain>, personal: Bool = false, perform: @escaping @MainActor (SyncDelta) -> Void) -> some View {
        modifier(HouseholdInvalidationModifier(domains: domains, personal: personal, perform: perform))
    }
}
