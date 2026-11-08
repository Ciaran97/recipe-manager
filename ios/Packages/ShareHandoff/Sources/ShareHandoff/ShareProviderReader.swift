import Foundation
import UniformTypeIdentifiers

public struct ShareCandidate: Sendable, Identifiable, Equatable {
    public let id: UUID
    public let kind: HandoffKind
    public let content: String
    public init(kind: HandoffKind, content: String) throws {
        _ = try HandoffEnvelope(kind: kind, content: content)
        self.id = UUID(); self.kind = kind; self.content = content
    }
}

/// Host-provided representations remain separate choices. No extraction, network request,
/// ingredient calculation or access to application credentials occurs in the extension.
@MainActor public enum ShareProviderReader {
    public static func requiresChoice(_ candidates: [ShareCandidate]) -> Bool { candidates.count > 1 }
    public static func candidates(from providers: [NSItemProvider]) async throws -> [ShareCandidate] {
        try Task.checkCancellation()
        var values: [ShareCandidate] = []
        var failure: Error?
        for provider in providers {
            try Task.checkCancellation()
            if provider.canLoadObject(ofClass: NSURL.self) {
                do {
                    let content = try await url(provider)
                    try Task.checkCancellation()
                    let candidate = try ShareCandidate(kind: .url, content: content)
                    if !values.contains(where: { $0.kind == candidate.kind && $0.content.utf8.elementsEqual(candidate.content.utf8) }) { values.append(candidate) }
                } catch is CancellationError { throw CancellationError() }
                catch { failure = error }
            }
            if provider.canLoadObject(ofClass: NSString.self) || provider.hasItemConformingToTypeIdentifier(UTType.text.identifier) {
                do {
                    let content = try await text(provider)
                    try Task.checkCancellation()
                    let candidate = try ShareCandidate(kind: .text, content: content)
                    if !values.contains(where: { $0.kind == candidate.kind && $0.content.utf8.elementsEqual(candidate.content.utf8) }) { values.append(candidate) }
                } catch is CancellationError { throw CancellationError() }
                catch { failure = error }
            }
        }
        try Task.checkCancellation()
        guard !values.isEmpty else { throw failure ?? HandoffFailure.invalidInput }
        return values
    }
    private static func url(_ provider: NSItemProvider) async throws -> String {
        try await withCheckedThrowingContinuation { continuation in
            provider.loadObject(ofClass: NSURL.self) { object, error in
                if let error { continuation.resume(throwing: error) }
                else if let url = object as? URL { continuation.resume(returning: url.absoluteString) }
                else { continuation.resume(throwing: HandoffFailure.invalidInput) }
            }
        }
    }
    private static func text(_ provider: NSItemProvider) async throws -> String {
        if provider.canLoadObject(ofClass: NSString.self) {
            do { return try await objectText(provider) }
            catch is CancellationError { throw CancellationError() }
            catch { /* Cross-process hosts may advertise text without object coercion support. */ }
        }
        var identifiers = provider.registeredTypeIdentifiers.filter { UTType($0)?.conforms(to: .text) == true }
        for fallback in [UTType.plainText.identifier, UTType.text.identifier] where !identifiers.contains(fallback) { identifiers.append(fallback) }
        var failure: Error = HandoffFailure.invalidInput
        for identifier in identifiers {
            try Task.checkCancellation()
            do { return try await itemText(provider, identifier: identifier) }
            catch is CancellationError { throw CancellationError() }
            catch { failure = error }
        }
        throw failure
    }
    private static func objectText(_ provider: NSItemProvider) async throws -> String {
        try await withCheckedThrowingContinuation { continuation in
            provider.loadObject(ofClass: NSString.self) { object, error in
                if let error { continuation.resume(throwing: error) }
                else if let text = object as? String { continuation.resume(returning: text) }
                else { continuation.resume(throwing: HandoffFailure.invalidInput) }
            }
        }
    }
    private static func itemText(_ provider: NSItemProvider, identifier: String) async throws -> String {
        try await withCheckedThrowingContinuation { continuation in
            provider.loadItem(forTypeIdentifier: identifier, options: nil) { item, error in
                if let error { continuation.resume(throwing: error); return }
                if let text = item as? String { continuation.resume(returning: text); return }
                if let data = item as? Data {
                    guard data.count <= HandoffLimits.maximumTextBytes else { continuation.resume(throwing: HandoffFailure.inputTooLarge); return }
                    if let text = String(data: data, encoding: .utf8) { continuation.resume(returning: text); return }
                }
                continuation.resume(throwing: HandoffFailure.invalidInput)
            }
        }
    }
}
