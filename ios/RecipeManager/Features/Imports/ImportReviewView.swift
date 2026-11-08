import SwiftUI
import RecipeManagerAPI

struct RecipeImportRoute: Identifiable {
    let id = UUID()
    var intent: PendingImportIntent?
}
struct ImportReviewView: View {
    let selection: ServerSelection
    let onSaved: @MainActor (Components.Schemas.RecipeRead) -> Void
    @State private var model: ImportReviewViewModel
    @State private var reviewing = false
    @FocusState private var editingInput: Bool
    @State private var discard = false
    @State private var replace: Replacement?
    @Environment(\.dismiss) private var dismiss
    private enum Replacement { case candidate(Int), reload }
    init(selection: ServerSelection, intent: PendingImportIntent? = nil, onSaved: @escaping @MainActor (Components.Schemas.RecipeRead) -> Void) {
        self.selection = selection; self.onSaved = onSaved
        _model = State(initialValue: ImportReviewViewModel(selection: selection, intent: intent))
    }
    var body: some View {
        Form {
            if let error = model.error { Section { Text(error).foregroundStyle(.red).accessibilityIdentifier("import-error") } }
            if let notice = model.notice { Section { Text(notice).foregroundStyle(.secondary) } }
            if let draft = model.draft {
                Section("Review ready") {
                    Text("Review and correct every field before saving this recipe.")
                    LabeledContent("Draft expires", value: draft.expiresAt.formatted(date: .abbreviated, time: .shortened))
                    Button("Continue recipe review") { reviewing = true }.accessibilityIdentifier("import-review-continue")
                    originalInput
                }
            } else {
                Section("Import recipe") {
                    if model.isCapture { LabeledContent("Import method", value: model.method == .url ? "Website URL" : "Pasted text with AI") }
                    else { Picker("Import method", selection: $model.method) { Text("Website URL").tag(RecipeImportMethod.url); Text("Pasted text with AI").tag(RecipeImportMethod.text) }.accessibilityIdentifier("import-method") }
                    if model.isCapture { originalInput }
                    else if model.method == .url {
                        TextField("Recipe URL", text: $model.input).keyboardType(.URL).textInputAutocapitalization(.never).autocorrectionDisabled().accessibilityIdentifier("import-url").focused($editingInput)
                    } else {
                        TextEditor(text: $model.input).frame(minHeight: 180).textInputAutocapitalization(.never).autocorrectionDisabled().accessibilityLabel("Recipe text").accessibilityIdentifier("import-text").focused($editingInput)
                        if !model.input.isEmpty { Button("Clear recipe text", role: .destructive) { model.input = "" }.accessibilityIdentifier("import-clear-text") }
                    }
                    if model.method == .text {
                        Text("Your text will be sent to the household's configured AI provider. Review every ingredient and quantity before saving.").font(.footnote)
                        if model.settingsLoaded && !model.aiEnabled { Text("AI importing is not configured. Ask an administrator in Settings; URL and manual recipe entry remain available. Shared content stays on this device until you explicitly import or discard it.").foregroundStyle(.secondary) }
                    } else { Text("Public pages with structured recipe data are supported. Images are not downloaded automatically.").font(.footnote) }
                    Button(model.busy ? "Importing…" : "Import for review") { Task { await model.start() } }
                        .disabled(model.busy || model.method == .text && model.settingsLoaded && !model.aiEnabled && !model.isCapture)
                        .accessibilityIdentifier("import-start")
                }.disabled(model.busy)
            }
            if model.busy { Section { ProgressView("Processing recipe"); Button("Cancel extraction") { model.cancelExtraction() } } }
            if model.uncertainSave { retrySection }
        }
        .scrollDismissesKeyboard(.interactively)
        .toolbar { ToolbarItemGroup(placement: .keyboard) { Spacer(); Button("Done") { editingInput = false } } }
        .navigationTitle("Import recipe").navigationBarTitleDisplayMode(.inline)
        .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Cancel") { if model.dirty { discard = true } else { dismiss() } }.disabled(model.busy || model.editor.busy) } }
        .interactiveDismissDisabled(model.dirty || model.busy || model.editor.busy)
        .confirmationDialog(model.uncertainSave ? "The previous save may already have succeeded. Leave without checking?" : "Discard this import review?", isPresented: $discard) {
            Button("Leave import", role: .destructive) { Task { if await model.discardDraft() { dismiss() } } }
            Button("Keep reviewing", role: .cancel) { }
        } message: { Text(model.isCapture ? "The capture stays bound to its original account and server so it can be resumed." : "No new recipe is saved by cancelling this review.") }
        .task { await model.loadSettings(); await model.resumeCapture() }
        .onHouseholdChange(domains: [.settings]) { _ in Task { await model.loadSettings() } }
        .onChange(of: model.draft?.id) { _, id in if id != nil { reviewing = true } }
        .onChange(of: model.savedRecipe?.id) { _, _ in if let recipe = model.savedRecipe { onSaved(recipe) } }
        .onDisappear { if !reviewing { model.cancelExtraction() } }
        .navigationDestination(isPresented: $reviewing) {
            RecipeEditor(selection: selection, model: model.editor) { _ in }
                .safeAreaInset(edge: .top) {
                    if !model.candidates.isEmpty {
                        VStack(alignment: .leading, spacing: 8) {
                            if model.candidates.count > 1 {
                                Menu("Choose imported recipe") {
                                    ForEach(Array(model.candidates.enumerated()), id: \.offset) { index, recipe in
                                        Button(recipe.title ?? "Untitled recipe") { request(.candidate(index)) }
                                    }
                                }.accessibilityIdentifier("import-candidate-menu")
                            }
                            if let error = model.error { Text(error).foregroundStyle(.red).font(.footnote) }
                            Button("Reload saved review") { request(.reload) }.disabled(model.busy || model.editor.busy || model.uncertainSave)
                        }.padding().frame(maxWidth: .infinity, alignment: .leading).background(.bar)
                    }
                }
                .safeAreaInset(edge: .bottom) { if model.uncertainSave { retrySection.padding().background(.bar) } }
                .confirmationDialog("Replace your unsaved review?", isPresented: Binding(get: { replace != nil }, set: { if !$0 { replace = nil } })) {
                    Button("Replace review", role: .destructive) { if let action = replace { perform(action) }; replace = nil }
                    Button("Keep editing", role: .cancel) { replace = nil }
                }
        }
    }
    private var originalInput: some View {
        DisclosureGroup("Original recipe input") { Text(model.input).font(.footnote).textSelection(.enabled).accessibilityIdentifier("import-original-input") }
    }
    private var retrySection: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("The previous save was not confirmed. Check its result before editing or starting another import.")
            if let error = model.error { Text(error).foregroundStyle(.red) }
            Button("Check previous save") { Task { await model.retrySave() } }.disabled(model.busy || model.editor.busy).accessibilityIdentifier("import-retry-save")
        }
    }
    private func request(_ action: Replacement) {
        guard !model.busy, !model.editor.busy, !model.uncertainSave else { return }
        if model.editor.dirty { replace = action } else { perform(action) }
    }
    private func perform(_ action: Replacement) {
        switch action { case .candidate(let index): model.selectCandidate(index); case .reload: Task { await model.reload() } }
    }
}
