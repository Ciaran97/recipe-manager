import Foundation
import Testing
@testable import RecipeManager

struct PersonalSyncTests {
    @Test func aPersonalRevisionChangeInvalidatesOnlyPersonalState() async {
        let probe = SyncProbe([syncResponse(), syncResponse(personal: "2", tag: "\"same-household-new-user-revision\"")])
        let sync = SyncCoordinator(fetch: { try await probe.fetch($0) }, deliver: { await probe.receive($0) }, pause: { _ in try await Task.sleep(for: .milliseconds(20)) }, jitter: { 0 })
        await sync.start(); #expect(await eventually { await probe.recorded().count >= 2 }); await sync.stop()
        let values = await probe.recorded()
        guard case .changed(let change) = values[1] else { Issue.record("Missing personal change"); return }
        #expect(change.personal); #expect(change.domains.isEmpty)
        #expect(change.revisions.ownPreferencesRevision == "2")
        #expect((await probe.tags())[1] == "\"compound\"")
    }
}
