import Foundation
import Darwin

/// One small queue document in the App Group. Coordination serializes both processes; staged
/// protected writes plus atomic rename preserve the previous document if capture is interrupted.
public final class HandoffStore: Sendable {
    private struct QueueFile: Codable { let schemaVersion: Int; var envelopes: [HandoffEnvelope] }
    private let directory: URL
    private let now: @Sendable () -> Date
    private let io = DispatchQueue(label: "local.recipemanager.handoff-io", qos: .utility)
    public init(directory: URL, now: @escaping @Sendable () -> Date = { Date() }) { self.directory = directory; self.now = now }

    public func append(_ envelope: HandoffEnvelope) async throws -> HandoffEnvelope {
        try envelope.validate(now: now())
        return try await transaction { entries, _, instant in
            try envelope.validate(now: instant)
            if let existing = entries.first(where: { $0.handoffId == envelope.handoffId }) {
                guard existing.sameCapture(as: envelope) else { throw HandoffFailure.identityConflict }
                return existing
            }
            guard entries.count < HandoffLimits.maximumPending else { throw HandoffFailure.queueFull }
            entries.append(envelope); return envelope
        }
    }
    public func load() async throws -> HandoffListing {
        try await transaction { entries, expired, _ in .init(entries: entries, expired: expired) }
    }
    public func bind(_ id: UUID, to destination: HandoffDestination) async throws -> HandoffEnvelope {
        try await transaction { entries, _, _ in
            guard let index = entries.firstIndex(where: { $0.handoffId == id }) else { throw HandoffFailure.missingCapture }
            if let previous = entries[index].binding, previous != destination { throw HandoffFailure.destinationConflict }
            entries[index].binding = destination; return entries[index]
        }
    }
    public func markSubmitted(_ id: UUID, destination: HandoffDestination, draftId: UUID? = nil) async throws -> HandoffEnvelope {
        try await transaction { entries, _, _ in
            guard let index = entries.firstIndex(where: { $0.handoffId == id }) else { throw HandoffFailure.missingCapture }
            guard entries[index].binding == destination else { throw HandoffFailure.destinationConflict }
            entries[index].status = draftId == nil ? .submitted : .reviewing
            entries[index].serverDraftId = draftId; return entries[index]
        }
    }
    public func discard(_ id: UUID) async throws {
        try await transaction { entries, _, _ in entries.removeAll { $0.handoffId == id } }
    }

    private func transaction<T: Sendable>(_ operation: @escaping @Sendable (inout [HandoffEnvelope], [UUID], Date) throws -> T) async throws -> T {
        try await withCheckedThrowingContinuation { continuation in
            io.async {
                do { continuation.resume(returning: try self.coordinated(operation)) }
                catch let failure as HandoffFailure { continuation.resume(throwing: failure) }
                catch { continuation.resume(throwing: HandoffFailure.persistence) }
            }
        }
    }
    private func coordinated<T>(_ operation: (inout [HandoffEnvelope], [UUID], Date) throws -> T) throws -> T {
        guard directory.isFileURL else { throw HandoffFailure.persistence }
        var attributes: [FileAttributeKey: Any] = [.posixPermissions: 0o700]
        #if os(iOS)
        attributes[.protectionKey] = FileProtectionType.completeUntilFirstUserAuthentication
        #endif
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true, attributes: attributes)
        let url = directory.appendingPathComponent("queue.json")
        let coordinator = NSFileCoordinator(filePresenter: nil)
        var coordinationError: NSError?, result: Result<T, Error>?
        // This updates the same queue document, even though bytes are atomically renamed.
        coordinator.coordinate(writingItemAt: url, options: [], error: &coordinationError) { coordinatedURL in
            result = Result {
                let current = try self.read(coordinatedURL), instant = self.now()
                guard current.schemaVersion == 1 else { throw HandoffFailure.unsupportedSchema }
                guard current.envelopes.count <= HandoffLimits.maximumPending,
                      Set(current.envelopes.map(\.handoffId)).count == current.envelopes.count else { throw HandoffFailure.corruptQueue }
                let expired = current.envelopes.filter { $0.expiresAt <= instant }.map(\.handoffId)
                var entries = current.envelopes.filter { $0.expiresAt > instant }
                for item in entries { try item.validate(now: instant) }
                let value = try operation(&entries, expired, instant)
                if entries != current.envelopes || !FileManager.default.fileExists(atPath: coordinatedURL.path) {
                    try self.write(.init(schemaVersion: 1, envelopes: entries), to: coordinatedURL)
                }
                return value
            }
        }
        if let coordinationError { throw coordinationError }
        guard let result else { throw HandoffFailure.persistence }
        return try result.get()
    }
    private func read(_ url: URL) throws -> QueueFile {
        guard FileManager.default.fileExists(atPath: url.path) else { return .init(schemaVersion: 1, envelopes: []) }
        let attributes = try FileManager.default.attributesOfItem(atPath: url.path)
        // JSON escaping can expand a bounded text capture by six times.
        let maximum = HandoffLimits.maximumPending * HandoffLimits.maximumTextBytes * 6 + 1_048_576
        guard let size = attributes[.size] as? NSNumber, size.intValue <= maximum else { throw HandoffFailure.corruptQueue }
        do { return try JSONDecoder().decode(QueueFile.self, from: Data(contentsOf: url)) }
        catch { throw HandoffFailure.corruptQueue }
    }
    private func write(_ file: QueueFile, to url: URL) throws {
        let temporary = directory.appendingPathComponent("recipe-capture-" + UUID().uuidString + ".tmp")
        defer { try? FileManager.default.removeItem(at: temporary) }
        var attributes: [FileAttributeKey: Any] = [.posixPermissions: 0o600]
        #if os(iOS)
        attributes[.protectionKey] = FileProtectionType.completeUntilFirstUserAuthentication
        #endif
        guard FileManager.default.createFile(atPath: temporary.path, contents: nil, attributes: attributes) else { throw HandoffFailure.persistence }
        let handle = try FileHandle(forWritingTo: temporary)
        do { try handle.write(contentsOf: JSONEncoder().encode(file)); try handle.synchronize(); try handle.close() }
        catch { try? handle.close(); throw error }
        guard Darwin.rename(temporary.path, url.path) == 0 else { throw HandoffFailure.persistence }
        let descriptor = Darwin.open(directory.path, O_RDONLY)
        guard descriptor >= 0 else { throw HandoffFailure.persistence }
        defer { _ = Darwin.close(descriptor) }
        guard Darwin.fsync(descriptor) == 0 else { throw HandoffFailure.persistence }
    }
}
