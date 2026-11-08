import Foundation
import Testing
import ShareHandoff

struct HandoffEnvelopeTests {
    let now = Date(timeIntervalSince1970: 1_800_000_000)
    @Test func urlAndTextPreserveOriginalContentAndStableIdentity() throws {
        let source = "1/3 cup flour\nSalt to taste\n"
        let capture = try HandoffEnvelope(kind: .text, content: source, createdAt: now)
        let roundTrip = try JSONDecoder().decode(HandoffEnvelope.self, from: JSONEncoder().encode(capture))
        #expect(roundTrip == capture); #expect(roundTrip.content == source)
        #expect(roundTrip.schemaVersion == 1); #expect(roundTrip.binding == nil); #expect(roundTrip.status == .pending)
        let url = "https://recipes.example.test/recipe?q=one%20cup"
        #expect(try HandoffEnvelope(kind: .url, content: url, createdAt: now).content == url)
    }
    @Test func unsupportedEmptyAndOverLimitInputsAreRejected() throws {
        #expect(throws: HandoffFailure.self) { try HandoffEnvelope(kind: .text, content: " \n\t", createdAt: now) }
        #expect(throws: HandoffFailure.self) { try HandoffEnvelope(kind: .url, content: "file:///private/example", createdAt: now) }
        #expect(throws: HandoffFailure.self) { try HandoffEnvelope(kind: .url, content: "https://user:secret@example.test/recipe", createdAt: now) }
        #expect(throws: HandoffFailure.self) { try HandoffEnvelope(kind: .text, content: String(repeating: "é", count: HandoffLimits.maximumTextBytes), createdAt: now) }
        #expect(throws: HandoffFailure.self) { try HandoffEnvelope(kind: .url, content: "https://example.test/" + String(repeating: "a", count: HandoffLimits.maximumURLLength), createdAt: now) }
    }
    @Test func envelopeContainsNoSessionOrProviderCredentialFields() throws {
        let value = try HandoffEnvelope(kind: .text, content: "Cookie crumb recipe", createdAt: now)
        let json = try #require(JSONSerialization.jsonObject(with: JSONEncoder().encode(value)) as? [String: Any])
        #expect(Set(json.keys).isDisjoint(with: ["cookie", "password", "apiKey", "accessToken", "session"]))
        #expect(json["content"] as? String == "Cookie crumb recipe")
    }
}

struct HandoffStoreTests {
    private func directory() throws -> URL {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("recipe-handoff-test-" + UUID().uuidString, isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700]); return root
    }
    @Test func captureSurvivesNewStoreInstancesAndDuplicateDelivery() async throws {
        let directory = try directory(); defer { try? FileManager.default.removeItem(at: directory) }
        let first = HandoffStore(directory: directory), second = HandoffStore(directory: directory)
        let capture = try HandoffEnvelope(kind: .text, content: "A recipe with unknown salt")
        let saved = try await first.append(capture)
        #expect(try await second.append(capture) == saved)
        #expect(try await second.load().entries == [saved])
        let attributes = try FileManager.default.attributesOfItem(atPath: directory.appendingPathComponent("queue.json").path)
        #expect((attributes[.posixPermissions] as? NSNumber)?.intValue == 0o600)
    }
    @Test func concurrentWritersCannotExceedTwentyCapturesOrLoseAcceptedEntries() async throws {
        let directory = try directory(); defer { try? FileManager.default.removeItem(at: directory) }
        let first = HandoffStore(directory: directory), second = HandoffStore(directory: directory)
        let captures = try (0..<21).map { try HandoffEnvelope(kind: .text, content: "Recipe \($0)") }
        let accepted = await withTaskGroup(of: Bool.self, returning: Int.self) { group in
            for (offset, capture) in captures.enumerated() { group.addTask { do { _ = try await (offset.isMultiple(of: 2) ? first : second).append(capture); return true } catch HandoffFailure.queueFull { return false } catch { Issue.record("Unexpected append failure"); return false } } }
            var count = 0; for await success in group { if success { count += 1 } }; return count
        }
        #expect(accepted == 20); #expect(try await first.load().entries.count == 20)
    }
    @Test func sevenDayExpiryRemovesContentAndReportsExpiredIdentity() async throws {
        let directory = try directory(); defer { try? FileManager.default.removeItem(at: directory) }
        let now = Date(timeIntervalSince1970: 1_800_000_000)
        let capture = try HandoffEnvelope(kind: .text, content: "Expired recipe", createdAt: now)
        _ = try await HandoffStore(directory: directory, now: { now }).append(capture)
        let later = HandoffStore(directory: directory, now: { now.addingTimeInterval(7 * 86400) })
        let result = try await later.load()
        #expect(result.entries.isEmpty); #expect(result.expired == [capture.handoffId])
        #expect(throws: HandoffFailure.self) { try capture.validate(now: now.addingTimeInterval(7 * 86400)) }
    }
    @Test func bindingAndDraftAssociationCannotSilentlyCrossAccountsOrServers() async throws {
        let directory = try directory(); defer { try? FileManager.default.removeItem(at: directory) }
        let store = HandoffStore(directory: directory)
        let capture = try HandoffEnvelope(kind: .url, content: "https://recipes.example.test/one")
        _ = try await store.append(capture)
        let destination = try HandoffDestination(origin: "https://home.example.test", householdId: UUID(), userId: UUID())
        let bound = try await store.bind(capture.handoffId, to: destination)
        let draft = UUID()
        let submitted = try await store.markSubmitted(capture.handoffId, destination: destination, draftId: draft)
        #expect(submitted.serverDraftId == draft); #expect(submitted.idempotencyKey == bound.idempotencyKey)
        let different = try HandoffDestination(origin: "https://another.example.test", householdId: UUID(), userId: UUID())
        await #expect(throws: HandoffFailure.self) { try await store.bind(capture.handoffId, to: different) }
        #expect(try await store.load().entries.first?.binding == destination)
    }
    @Test func partialTemporaryFileCannotReplaceThePreviousDurableQueue() async throws {
        let directory = try directory(); defer { try? FileManager.default.removeItem(at: directory) }
        let store = HandoffStore(directory: directory), capture = try HandoffEnvelope(kind: .text, content: "Keep this capture")
        _ = try await store.append(capture)
        try Data("{incomplete".utf8).write(to: directory.appendingPathComponent("recipe-capture-interrupted.tmp"))
        #expect(try await store.load().entries == [capture])
    }
    @Test func corruptedOrUnsupportedQueueFailsWithoutErasingOriginalBytes() async throws {
        let directory = try directory(); defer { try? FileManager.default.removeItem(at: directory) }
        let path = directory.appendingPathComponent("queue.json"), bytes = Data("{not-valid-json".utf8)
        try bytes.write(to: path)
        await #expect(throws: HandoffFailure.self) { try await HandoffStore(directory: directory).load() }
        #expect(try Data(contentsOf: path) == bytes)
    }
    @Test func explicitDiscardRemovesOnlyTheSelectedCapture() async throws {
        let directory = try directory(); defer { try? FileManager.default.removeItem(at: directory) }
        let store = HandoffStore(directory: directory), first = try HandoffEnvelope(kind: .text, content: "First"), second = try HandoffEnvelope(kind: .text, content: "Second")
        _ = try await store.append(first); _ = try await store.append(second)
        try await store.discard(first.handoffId)
        #expect(try await store.load().entries == [second])
    }
    @Test func unknownSchemaCannotBeOverwrittenByAnOlderApplication() async throws {
        let directory = try directory(); defer { try? FileManager.default.removeItem(at: directory) }
        let path = directory.appendingPathComponent("queue.json"), bytes = Data("{\"schemaVersion\":99,\"envelopes\":[]}".utf8)
        try bytes.write(to: path)
        await #expect(throws: HandoffFailure.self) { try await HandoffStore(directory: directory).load() }
        #expect(try Data(contentsOf: path) == bytes)
    }
    @Test func duplicateIdentityWithDifferentInputCannotOverwriteTheCapture() async throws {
        let directory = try directory(); defer { try? FileManager.default.removeItem(at: directory) }
        let store = HandoffStore(directory: directory), first = try HandoffEnvelope(kind: .text, content: "Original recipe")
        _ = try await store.append(first)
        let changed = try HandoffEnvelope(kind: .text, content: "Different input", createdAt: first.createdAt, handoffId: first.handoffId, idempotencyKey: first.idempotencyKey)
        await #expect(throws: HandoffFailure.self) { try await store.append(changed) }
        #expect(try await store.load().entries == [first])
    }

    @Test func canonicallyEquivalentUnicodeStillHasDistinctOriginalInputIdentity() async throws {
        let directory = try directory(); defer { try? FileManager.default.removeItem(at: directory) }
        let store = HandoffStore(directory: directory), first = try HandoffEnvelope(kind: .text, content: "Caf\u{00E9}")
        let changed = try HandoffEnvelope(kind: .text, content: "Cafe\u{0301}", createdAt: first.createdAt, handoffId: first.handoffId, idempotencyKey: first.idempotencyKey)
        #expect(first.content == changed.content)
        #expect(Array(first.content.utf8) != Array(changed.content.utf8))
        _ = try await store.append(first)
        await #expect(throws: HandoffFailure.self) { try await store.append(changed) }
    }

}
