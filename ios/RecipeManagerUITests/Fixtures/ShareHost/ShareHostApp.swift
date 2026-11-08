import SwiftUI
import UIKit
import ShareHandoff

@main struct ShareHostApp: App {
    var body: some Scene { WindowGroup { ShareHostView() } }
}
private struct SharePayload: Identifiable { let id = UUID(); let items: [Any] }
private struct ShareHostView: View {
    @State private var payload: SharePayload?
    @State private var captures: [HandoffEnvelope] = []
    @State private var error: String?
    @Environment(\.scenePhase) private var phase
    var body: some View {
        NavigationStack {
            Form {
                Text("Pending captures: \(captures.count)").accessibilityIdentifier("fixture-capture-count")
                ForEach(captures, id: \.handoffId) { capture in
                    if let draft = capture.serverDraftId { Text(draft.uuidString).accessibilityIdentifier("fixture-draft-" + capture.handoffId.uuidString) }
                    Text(capture.kind.rawValue + ": " + capture.content).accessibilityIdentifier("fixture-capture-" + capture.handoffId.uuidString)
                }
                if let error { Text(error).accessibilityIdentifier("fixture-error") }
                Button("Share recipe URL") { payload = .init(items: [URL(string: "https://recipes.example.test/shared-recipe")!]) }.accessibilityIdentifier("fixture-share-url")
                Button("Share recipe text") { payload = .init(items: [(ProcessInfo.processInfo.environment["RECIPE_MANAGER_SHARE_TEXT"] ?? "ChatGPT fixture recipe\n1/3 cup rice\nSalt to taste\nCook, then serve.") as NSString]) }.accessibilityIdentifier("fixture-share-text")
                Button("Share mixed content") { payload = .init(items: [URL(string: "https://recipes.example.test/mixed")!, "Mixed recipe text\n2 cups rice" as NSString]) }.accessibilityIdentifier("fixture-share-mixed")
            }.navigationTitle("Recipe Share Fixture")
        }.sheet(item: $payload, onDismiss: { Task { await load() } }) { value in
            ShareSheet(items: value.items, finished: { payload = nil; Task { await load() } })
        }.task { await load() }.onChange(of: phase) { _, phase in if phase == .active { Task { await load() } } }
    }
    private func load() async {
        do { captures = try await HandoffStore(directory: HandoffContainer.directory()).load().entries; error = nil }
        catch { self.error = "Fixture could not access the shared capture queue." }
    }
}
private struct ShareSheet: UIViewControllerRepresentable {
    let items: [Any]
    let finished: @MainActor () -> Void
    func makeUIViewController(context: Context) -> UIActivityViewController {
        let controller = UIActivityViewController(activityItems: items, applicationActivities: nil)
        let finished = finished
        controller.completionWithItemsHandler = { _, _, _, _ in Task { @MainActor in finished() } }
        return controller
    }
    func updateUIViewController(_ controller: UIActivityViewController, context: Context) {}
}
