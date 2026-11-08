import Foundation
import Testing
import RecipeManagerAPI
@testable import RecipeManager

actor SyncProbe {
    var requests: [String?] = []
    var events: [SyncEvent] = []
    var responses: [APIResponse]
    var failure: SessionFailure?
    init(_ responses: [APIResponse], failure: SessionFailure? = nil) { self.responses = responses; self.failure = failure }
    func fetch(_ tag: String?) throws -> APIResponse {
        requests.append(tag)
        if let failure { throw failure }
        if responses.count > 1 { return responses.removeFirst() }
        return responses.first ?? APIResponse(data: Data(), status: 304, etag: tag)
    }
    func receive(_ event: SyncEvent) { events.append(event) }
    func count() -> Int { requests.count }
    func recorded() -> [SyncEvent] { events }
    func tags() -> [String?] { requests }
}
func syncResponse(personal: String = "1", shopping: String = "1", tag: String = "\"compound\"") -> APIResponse {
    let value = """
    {"contentRevision":"\(shopping)","recipesRevision":"1","plannerRevision":"1","shoppingRevision":"\(shopping)","settingsRevision":"1","membershipRevision":"1","ownPreferencesRevision":"\(personal)"}
    """
    return APIResponse(data: Data(value.utf8), status: 200, etag: tag)
}
func eventually(_ condition: @escaping @Sendable () async -> Bool) async -> Bool {
    for _ in 0..<200 { if await condition() { return true }; try? await Task.sleep(for: .milliseconds(5)) }
    return false
}

struct SyncCoordinatorTests {
    @Test func repeatedForegroundStartHasOnePollingTaskAndUsesConditionalValidator() async {
        let probe = SyncProbe([syncResponse(), APIResponse(data: Data(), status: 304, etag: "\"compound\"")])
        let sync = SyncCoordinator(fetch: { try await probe.fetch($0) }, deliver: { await probe.receive($0) }, pause: { _ in try await Task.sleep(for: .milliseconds(30)) }, jitter: { 0 })
        await sync.start(); await sync.start(); await sync.start()
        #expect(await eventually { await probe.count() >= 2 })
        await sync.stop()
        let tags = await probe.tags(); #expect(tags.first! == nil); #expect(tags.dropFirst().allSatisfy { $0 == "\"compound\"" })
        #expect(await probe.recorded().count == 1)
        let count = await probe.count(); try? await Task.sleep(for: .milliseconds(50)); #expect(await probe.count() == count)
    }
    @Test func backgroundStopsAndForegroundImmediatelyReadsChanges() async {
        let probe = SyncProbe([syncResponse(), syncResponse(shopping: "2", tag: "\"next\"")])
        let sync = SyncCoordinator(fetch: { try await probe.fetch($0) }, deliver: { await probe.receive($0) })
        await sync.start(); #expect(await eventually { await probe.recorded().count == 1 }); await sync.stop()
        await sync.start(); #expect(await eventually { await probe.recorded().count == 2 }); await sync.stop()
        guard case .changed(let change) = await probe.recorded().last else { Issue.record("Missing foreground change"); return }
        #expect(change.domains.contains(.shopping)); #expect(change.personal); #expect(!change.initial)
    }
    @Test func unauthorizedStopsInsteadOfPollingAnExpiredSession() async {
        let probe = SyncProbe([], failure: .authenticationRequired)
        let sync = SyncCoordinator(fetch: { try await probe.fetch($0) }, deliver: { await probe.receive($0) })
        await sync.start(); #expect(await eventually { await probe.recorded().count == 1 }); await sync.stop()
        guard case .authenticationRequired = await probe.recorded().first else { Issue.record("Missing unauthorized event"); return }
        #expect(await probe.count() == 1)
    }
    @Test func networkFailuresUseBoundedBackoff() {
        #expect(SyncCoordinator.delay(failures: 0, jitter: 0) == .seconds(1))
        #expect(SyncCoordinator.delay(failures: 1, jitter: 0) == .seconds(2))
        #expect(SyncCoordinator.delay(failures: 2, jitter: 0) == .seconds(4))
        #expect(SyncCoordinator.delay(failures: 100, jitter: 1) == .seconds(30))
    }
}

private actor SuspendedSyncFetch {
    private var continuation: CheckedContinuation<APIResponse, Never>?
    private(set) var started = false
    private(set) var cancelled = false
    func fetch() async -> APIResponse {
        await withTaskCancellationHandler {
            await withCheckedContinuation { continuation in self.continuation = continuation; self.started = true }
        } onCancel: { Task { await self.markCancelled() } }
    }
    private func markCancelled() { cancelled = true }
    func release() { continuation?.resume(returning: syncResponse()); continuation = nil }
}

struct SyncCancellationTests {
    @Test func aLateResponseAfterBackgroundCancellationCannotPublishOldState() async {
        let fetch = SuspendedSyncFetch(); let probe = SyncProbe([])
        let sync = SyncCoordinator(fetch: { _ in await fetch.fetch() }, deliver: { await probe.receive($0) })
        await sync.start(); #expect(await eventually { await fetch.started })
        let stopped = Task { await sync.stop() }
        #expect(await eventually { await fetch.cancelled })
        await fetch.release(); await stopped.value
        #expect(await probe.recorded().isEmpty)
    }
}

private actor SyncDelays {
    private(set) var values: [Duration] = []
    func pause(_ duration: Duration) throws {
        values.append(duration)
        if values.count >= 3 { throw CancellationError() }
    }
}
struct SyncRetryTests {
    @Test func failedReadsActuallyBackOffWithoutPublishingOrCreatingAnotherLoop() async {
        let delays = SyncDelays(), probe = SyncProbe([], failure: .invalidResponse)
        let sync = SyncCoordinator(fetch: { try await probe.fetch($0) }, deliver: { await probe.receive($0) }, pause: { try await delays.pause($0) }, jitter: { 0 })
        await sync.start()
        #expect(await eventually { await delays.values.count == 3 })
        await sync.stop()
        #expect(await delays.values == [.seconds(2), .seconds(4), .seconds(8)])
        #expect(await probe.count() == 3); #expect(await probe.recorded().isEmpty)
    }
}

struct SyncForegroundRevalidationTests {
    @Test func anUnchangedForegroundValidatorStillRefreshesExistingVisibleState() async {
        let probe = SyncProbe([syncResponse(), APIResponse(data: Data(), status: 304, etag: "\"compound\"")])
        let sync = SyncCoordinator(fetch: { try await probe.fetch($0) }, deliver: { await probe.receive($0) })
        await sync.start(); #expect(await eventually { await probe.recorded().count == 1 }); await sync.stop()
        await sync.start(); #expect(await eventually { await probe.recorded().count == 2 }); await sync.stop()
        guard case .changed(let change) = await probe.recorded().last else { Issue.record("Missing foreground revalidation"); return }
        #expect(!change.initial); #expect(change.domains.contains(.shopping)); #expect(change.personal)
    }
}
