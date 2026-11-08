import SwiftUI
import ShareHandoff

/// The normal import coordinator consumes the confirmed intent; this inbox never extracts or
/// commits a recipe itself and cannot submit content under an unconfirmed account/server.
struct PendingImportInbox: View {
    @Bindable var selection: ServerSelection
    let continueImport: @MainActor (PendingImportIntent) -> Void
    @State private var model: PendingCaptureInboxModel
    @State private var discard: HandoffEnvelope?
    @State private var confirmedIntent: PendingImportIntent?
    @Environment(\.scenePhase) private var phase
    init(selection: ServerSelection, model: PendingCaptureInboxModel? = nil, continueImport: @escaping @MainActor (PendingImportIntent) -> Void) {
        self.selection = selection; self.continueImport = continueImport
        _model = State(initialValue: model ?? PendingCaptureInboxModel(selection: selection))
    }
    var body: some View {
        List {
            Section {
                Text("Shared content waits here until you review and save a recipe. Captures expire after seven days.")
                if selection.user == nil { Text("Choose your server and sign in before reviewing a capture.").foregroundStyle(.secondary) }
                if model.expiredCount > 0 { Text("\(model.expiredCount) expired captures were removed. Saved recipes are unaffected.").accessibilityIdentifier("capture-expiry-notice") }
                if let error = model.error { Text(error).foregroundStyle(.red).accessibilityIdentifier("capture-inbox-error") }
            }
            ForEach(model.entries, id: \.handoffId) { capture in
                Section(capture.kind == .url ? "Shared recipe URL" : "Shared recipe text") {
                    if model.canRead(capture) {
                        Text(capture.content).lineLimit(8).textSelection(.enabled)
                        Text("Captured \(capture.createdAt.formatted()) · Expires \(capture.expiresAt.formatted())").font(.caption)
                        Button(capture.serverDraftId == nil ? "Review import destination" : "Resume import review") { Task { await model.prepare(capture.handoffId) } }
                            .disabled(selection.user == nil).accessibilityIdentifier("capture-review-" + capture.handoffId.uuidString)
                    } else {
                        Text("This capture is bound to another account or server. Sign in there to review it.").foregroundStyle(.secondary)
                    }
                    Button("Discard capture", role: .destructive) { discard = capture }
                }
            }
            if model.entries.isEmpty { Section { Text("No pending captures. Share a recipe URL or text to Recipe Manager from another application.") } }
        }
        .navigationTitle("Shared recipe captures")
        .disabled(model.busy)
        .refreshable { await model.load() }
        .task { await model.load() }
        .onChange(of: phase) { _, value in if value == .active { Task { await model.load() } } }
        .onChange(of: selection.sessionIdentity) { _, _ in model.accountChanged(); Task { await model.load() } }
        .sheet(item: $model.destination, onDismiss: {
            if let intent = confirmedIntent { confirmedIntent = nil; continueImport(intent) }
        }) { destination in
            NavigationStack {
                Form {
                    Section("Confirm import destination") {
                        LabeledContent("Server", value: destination.binding.origin)
                        LabeledContent("Household", value: destination.householdName)
                        LabeledContent("Account", value: destination.accountName)
                        Text("Continue to choose an import method, then review the recipe before saving.")
                        Button("Confirm destination and continue") {
                            Task { if let intent = await model.confirm() { confirmedIntent = intent } }
                        }.disabled(model.busy).accessibilityIdentifier("capture-confirm-destination")
                    }
                    if let error = model.error { Section { Text(error).foregroundStyle(.red) } }
                }.navigationTitle("Import destination").navigationBarTitleDisplayMode(.inline)
                    .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Cancel") { model.cancelDestination() }.disabled(model.busy) } }
            }.interactiveDismissDisabled(model.busy)
        }
        .confirmationDialog("Discard this capture from this device?", isPresented: Binding(get: { discard != nil }, set: { if !$0 { discard = nil } })) {
            Button("Discard capture", role: .destructive) { if let capture = discard { Task { await model.discard(capture.handoffId); discard = nil } } }
            Button("Cancel", role: .cancel) { discard = nil }
        } message: { Text("The shared content will be removed. A recipe already saved on your server is unaffected.") }
    }
}
