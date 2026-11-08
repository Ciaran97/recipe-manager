import Foundation

public enum HandoffLimits {
    public static let maximumTextBytes = 100 * 1024
    public static let maximumURLLength = 2048
    public static let maximumPending = 20
    public static let lifetime: TimeInterval = 7 * 86400
}
public enum HandoffFailure: Error, Sendable, Equatable {
    case invalidInput, inputTooLarge, expired, futureCapture, unsupportedSchema, queueFull
    case corruptQueue, persistence, missingCapture, destinationConflict, identityConflict
}
public enum HandoffKind: String, Codable, Sendable { case url, text }
public enum HandoffStatus: String, Codable, Sendable { case pending, submitted, reviewing }

public struct HandoffDestination: Codable, Sendable, Equatable {
    public let origin: String
    public let householdId: UUID
    public let userId: UUID
    public init(origin: String, householdId: UUID, userId: UUID) throws {
        guard var parts = URLComponents(string: origin), parts.scheme?.lowercased() == "https", let host = parts.host, !host.isEmpty,
              parts.user == nil, parts.password == nil, parts.query == nil, parts.fragment == nil, parts.path.isEmpty || parts.path == "/" else { throw HandoffFailure.invalidInput }
        parts.scheme = "https"; parts.host = host.lowercased(); parts.path = ""; if parts.port == 443 { parts.port = nil }
        guard let normalized = parts.url?.absoluteString else { throw HandoffFailure.invalidInput }
        self.origin = normalized; self.householdId = householdId; self.userId = userId
    }
}

/// Credential-free capture only. All extraction, ingredient matching and recipe commits belong to the main app/API.
public struct HandoffEnvelope: Codable, Sendable, Equatable {
    public let schemaVersion: Int
    public let handoffId: UUID
    public let kind: HandoffKind
    public let content: String
    public let createdAt: Date
    public let idempotencyKey: UUID
    public internal(set) var binding: HandoffDestination?
    public internal(set) var status: HandoffStatus
    public internal(set) var serverDraftId: UUID?
    public var expiresAt: Date { createdAt.addingTimeInterval(HandoffLimits.lifetime) }
    public init(kind: HandoffKind, content: String, createdAt: Date = Date(), handoffId: UUID = UUID(), idempotencyKey: UUID = UUID()) throws {
        self.schemaVersion = 1; self.handoffId = handoffId; self.kind = kind; self.content = content
        self.createdAt = createdAt; self.idempotencyKey = idempotencyKey
        self.binding = nil; self.status = .pending; self.serverDraftId = nil
        try validateContent()
    }
    public func validate(now: Date = Date()) throws {
        guard schemaVersion == 1 else { throw HandoffFailure.unsupportedSchema }
        try validateContent()
        guard createdAt <= now.addingTimeInterval(300) else { throw HandoffFailure.futureCapture }
        guard expiresAt > now else { throw HandoffFailure.expired }
        if let binding {
            _ = try HandoffDestination(origin: binding.origin, householdId: binding.householdId, userId: binding.userId)
        } else if status != .pending || serverDraftId != nil { throw HandoffFailure.corruptQueue }
        if status == .reviewing && serverDraftId == nil { throw HandoffFailure.corruptQueue }
    }
    private func validateContent() throws {
        guard !content.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { throw HandoffFailure.invalidInput }
        if kind == .text {
            guard content.utf8.count <= HandoffLimits.maximumTextBytes else { throw HandoffFailure.inputTooLarge }
        } else {
            guard content.utf16.count <= HandoffLimits.maximumURLLength else { throw HandoffFailure.inputTooLarge }
            guard let parts = URLComponents(string: content), ["http", "https"].contains(parts.scheme?.lowercased() ?? ""),
                  let host = parts.host, !host.isEmpty, parts.user == nil, parts.password == nil, parts.url != nil else { throw HandoffFailure.invalidInput }
        }
    }
    public static func == (lhs: Self, rhs: Self) -> Bool {
        lhs.sameCapture(as: rhs) && lhs.binding == rhs.binding && lhs.status == rhs.status && lhs.serverDraftId == rhs.serverDraftId
    }
    internal func sameCapture(as other: Self) -> Bool {
        schemaVersion == other.schemaVersion && handoffId == other.handoffId && kind == other.kind && content.utf8.elementsEqual(other.content.utf8)
            && createdAt == other.createdAt && idempotencyKey == other.idempotencyKey
    }
}

public struct HandoffListing: Sendable {
    public let entries: [HandoffEnvelope]
    public let expired: [UUID]
}
