import Foundation

/// Reads only the providers handed to the user's explicit Paste action, never the ambient clipboard.
@MainActor enum InvitationPaste {
    static func load(_ providers: [NSItemProvider], completion: @escaping @MainActor @Sendable (String?) -> Void) {
        guard let provider = providers.first else { completion(nil); return }
        if provider.canLoadObject(ofClass: NSURL.self) {
            _ = provider.loadObject(ofClass: NSURL.self) { value, _ in
                let text = (value as? NSURL)?.absoluteString
                Task { @MainActor in completion(text) }
            }
        } else if provider.canLoadObject(ofClass: NSString.self) {
            _ = provider.loadObject(ofClass: NSString.self) { value, _ in
                let text = value as? String
                Task { @MainActor in completion(text) }
            }
        } else { completion(nil) }
    }
}
