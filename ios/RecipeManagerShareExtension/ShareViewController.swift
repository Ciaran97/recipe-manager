import UIKit
import SwiftUI
import Observation
import ShareHandoff

@MainActor @Observable private final class ShareCaptureModel {
    var candidates: [ShareCandidate] = []
    var selected: UUID?
    var error: String?
    var loading = true
    var saving = false
    private let context: NSExtensionContext
    private let capturedAt = Date()
    private var loaded = false
    private var capture: HandoffEnvelope?
    init(context: NSExtensionContext) { self.context = context }
    var chosen: ShareCandidate? { candidates.first { $0.id == selected } }
    func prepare() async {
        guard !loaded else { return }; loaded = true
        defer { loading = false }
        do {
            let providers = context.inputItems.compactMap { $0 as? NSExtensionItem }.flatMap { $0.attachments ?? [] }
            candidates = try await ShareProviderReader.candidates(from: providers)
            if !ShareProviderReader.requiresChoice(candidates) { selected = candidates.first?.id }
        } catch is CancellationError { return }
        catch { self.error = message(error) }
    }
    func save() async {
        guard !saving, let chosen else { return }
        saving = true; error = nil
        defer { saving = false }
        do {
            if capture?.kind != chosen.kind || capture?.content.utf8.elementsEqual(chosen.content.utf8) != true {
                capture = try HandoffEnvelope(kind: chosen.kind, content: chosen.content, createdAt: capturedAt)
            }
            guard let capture else { return }
            let store = HandoffStore(directory: try HandoffContainer.directory())
            _ = try await store.append(capture)
            context.completeRequest(returningItems: [], completionHandler: nil)
        } catch { self.error = message(error) }
    }
    func cancel() {
        guard !saving else { return }
        context.cancelRequest(withError: NSError(domain: NSCocoaErrorDomain, code: NSUserCancelledError))
    }
    private func message(_ error: Error) -> String {
        switch error as? HandoffFailure {
        case .queueFull: "Your capture queue is full. Open Recipe Manager to review or discard captures, then try again."
        case .inputTooLarge: "This content is too large. Share a recipe URL or shorten the text."
        case .invalidInput: "Share a website recipe URL or plain recipe text. This content could not be read."
        case .corruptQueue, .unsupportedSchema: "Saved captures could not be read. Open Recipe Manager to resolve the problem."
        case .expired: "This capture has expired. Cancel and share the content again."
        default: "Could not save this capture. Try again or cancel."
        }
    }
}

private struct ShareCaptureView: View {
    @Bindable var model: ShareCaptureModel
    var body: some View {
        NavigationStack {
            Form {
                Section {
                    Text("Save shared content for import. Open Recipe Manager to review and edit the recipe before saving it.")
                    Text("Captures expire after seven days. Nothing is sent to a recipe website or AI provider here.").font(.caption).foregroundStyle(.secondary)
                }
                if model.loading { ProgressView("Reading shared content") }
                if let error = model.error { Section { Text(error).foregroundStyle(.red).accessibilityIdentifier("share-error") } }
                if ShareProviderReader.requiresChoice(model.candidates) {
                    Section("Choose shared content") {
                        Picker("URL or text", selection: $model.selected) {
                            Text("Choose a URL or text").tag(Optional<UUID>.none)
                            ForEach(model.candidates) { candidate in
                                Text(candidate.kind == .url ? "Recipe URL: \(candidate.content)" : "Recipe text: \(candidate.content.prefix(70))")
                                    .tag(Optional(candidate.id))
                            }
                        }.accessibilityIdentifier("share-candidate")
                    }
                }
                if let chosen = model.chosen {
                    Section(chosen.kind == .url ? "Recipe URL" : "Recipe text") {
                        Text(chosen.content).lineLimit(14).textSelection(.enabled).accessibilityIdentifier("share-content")
                    }
                }
                Section {
                    Button(model.saving ? "Saving capture…" : "Save capture") { Task { await model.save() } }
                        .disabled(model.loading || model.saving || model.chosen == nil).accessibilityIdentifier("share-save")
                }
            }
            .navigationTitle("Recipe Manager")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Cancel") { model.cancel() }.disabled(model.saving).accessibilityIdentifier("share-cancel") } }
            .task { await model.prepare() }
        }
    }
}

final class ShareViewController: UIViewController {
    override func viewDidLoad() {
        super.viewDidLoad()
        guard let context = extensionContext else { return }
        let host = UIHostingController(rootView: ShareCaptureView(model: ShareCaptureModel(context: context)))
        addChild(host); host.view.translatesAutoresizingMaskIntoConstraints = false; view.addSubview(host.view)
        NSLayoutConstraint.activate([host.view.leadingAnchor.constraint(equalTo: view.leadingAnchor), host.view.trailingAnchor.constraint(equalTo: view.trailingAnchor), host.view.topAnchor.constraint(equalTo: view.topAnchor), host.view.bottomAnchor.constraint(equalTo: view.bottomAnchor)])
        host.didMove(toParent: self)
    }
}
