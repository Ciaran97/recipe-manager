import SwiftUI
import Observation
import PhotosUI
import ImageIO
import UIKit
import RecipeManagerAPI

struct RecipeIngredientDraft: Identifiable, Equatable {
    var id = UUID().uuidString
    var groupId = ""
    var ingredientId = ""
    var ingredientName = ""
    var quantityText = ""
    var kind: Components.Schemas.QuantityKind = .numeric
    var unit: Components.Schemas.IngredientUnit? = .g
    var preparation = ""
    var uncertain = false
}
struct RecipeGroupDraft: Identifiable, Equatable { var id = UUID().uuidString; var title = "" }
struct RecipeInstructionDraft: Identifiable, Equatable { var id = UUID().uuidString; var text = "" }
struct RecipeDraft: Equatable {
    var title = ""
    var description = ""
    var defaultServings = "2"
    var prepMinutes = ""
    var cookMinutes = ""
    var notes = ""
    var sourceUrl = ""
    var imageAssetId = ""
    var measurementStandard: Components.Schemas.MeasurementStandard? = .AU
    var acknowledgeUncertain = false
    var ingredients = [RecipeIngredientDraft()]
    var groups: [RecipeGroupDraft] = []
    var instructions = [RecipeInstructionDraft()]
    var collectionIds: [String] = []
    var tagIds: [String] = []
}
enum RecipeEditorFailure: Error { case invalid(String) }

@Observable @MainActor
final class RecipeEditorViewModel {
    var draft = RecipeDraft()
    var savedRecipe: Components.Schemas.RecipeRead?
    var suggestions: [Components.Schemas.IngredientRead] = []
    var activeIngredient: String?
    var error: String?
    var fieldErrors: [String: String] = [:]
    var originalLines: [String: String] = [:]
    var importedSave: (@MainActor (Components.Schemas.RecipeWrite) async throws -> Components.Schemas.RecipeRead)?
    var submissionUncertain = false
    var loading = false
    var busy = false
    let recipeId: String?
    private let selection: ServerSelection
    private let requests: ScopedAPI
    private var original = RecipeDraft()
    private var etag: String?
    private var retryKey: UUID?
    private var photoRetry: UUID?
    private var ingredientRetries: [String: UUID] = [:]
    private var searchTask: Task<Void, Never>?
    var dirty: Bool { draft != original }
    var requiresReview: Bool { draft.ingredients.contains { $0.kind == .unknown || $0.uncertain } }
    init(selection: ServerSelection, recipeId: String? = nil) {
        self.selection = selection; self.requests = ScopedAPI(selection: selection); self.recipeId = recipeId; original = draft
    }
    func applyImported(_ recipe: Components.Schemas.ImportedRecipe) {
        var value = RecipeDraft()
        value.title = recipe.title ?? ""; value.description = recipe.description ?? ""; value.defaultServings = recipe.defaultServings ?? ""
        value.prepMinutes = recipe.prepMinutes.map(String.init) ?? ""; value.cookMinutes = recipe.cookMinutes.map(String.init) ?? ""
        value.notes = recipe.notes ?? ""; value.sourceUrl = recipe.sourceUrl ?? ""
        value.measurementStandard = recipe.measurementStandard.flatMap { Components.Schemas.MeasurementStandard(rawValue: $0.rawValue) }
        var groups: [String: String] = [:]; originalLines = [:]
        value.ingredients = recipe.ingredients.map { source in
            if let title = source.group, groups[title] == nil {
                let group = RecipeGroupDraft(title: title); groups[title] = group.id; value.groups.append(group)
            }
            var row = RecipeIngredientDraft()
            row.groupId = source.group.flatMap { groups[$0] } ?? ""; row.ingredientId = source.matchedIngredientId ?? ""
            row.ingredientName = source.matchedIngredientName ?? source.ingredientName; row.quantityText = source.quantityText; row.kind = source.kind
            row.unit = source.unit.flatMap { Components.Schemas.IngredientUnit(rawValue: $0.rawValue) }
            row.preparation = source.preparation ?? ""; row.uncertain = source.uncertain || row.unit == nil
            originalLines[row.id] = source.originalText
            return row
        }
        value.instructions = recipe.instructions.map { source in RecipeInstructionDraft(text: source.group.map { $0 + ": " + source.text } ?? source.text) }
        draft = value; original = value; error = nil; fieldErrors = [:]; cancelSearch()
    }
    func applyReviewed(_ recipe: Components.Schemas.RecipeWrite, ingredientNames: [String: String]) {
        var value = RecipeDraft()
        value.title = recipe.title; value.description = recipe.description ?? ""; value.defaultServings = recipe.defaultServings
        value.prepMinutes = recipe.prepMinutes.map(String.init) ?? ""; value.cookMinutes = recipe.cookMinutes.map(String.init) ?? ""
        value.notes = recipe.notes ?? ""; value.sourceUrl = recipe.sourceUrl ?? ""; value.imageAssetId = recipe.imageAssetId ?? ""
        value.measurementStandard = recipe.measurementStandard; value.collectionIds = recipe.collectionIds; value.tagIds = recipe.tagIds
        value.acknowledgeUncertain = recipe.acknowledgeUncertainIngredients ?? false
        value.groups = recipe.groups.map { RecipeGroupDraft(id: $0.id ?? UUID().uuidString, title: $0.title) }
        value.ingredients = recipe.ingredients.map { source in
            let name = ingredientNames[source.ingredientId] ?? ""
            return RecipeIngredientDraft(id: source.id ?? UUID().uuidString, groupId: source.groupId ?? "", ingredientId: name.isEmpty ? "" : source.ingredientId,
                ingredientName: name, quantityText: source.quantityText, kind: source.kind, unit: source.unit, preparation: source.preparation ?? "", uncertain: source.uncertain ?? false)
        }
        value.instructions = recipe.instructions.map { RecipeInstructionDraft(id: $0.id ?? UUID().uuidString, text: $0.text) }
        draft = value; original = value; originalLines = [:]; error = nil; fieldErrors = [:]; cancelSearch()
    }
    func acceptImported(_ recipe: Components.Schemas.RecipeRead) { savedRecipe = recipe; original = draft; submissionUncertain = false }
    func load() async {
        guard let recipeId else { return }
        loading = true; defer { loading = false }
        await perform {
            let response = try await self.requests.send(path: "/api/v1/recipes/" + recipeId)
            let recipe = try APIJSON.decoder().decode(Components.Schemas.RecipeRead.self, from: response.data)
            var draft = RecipeDraft(); draft.title = recipe.title; draft.description = recipe.description ?? ""; draft.defaultServings = recipe.defaultServings
            draft.prepMinutes = recipe.prepMinutes.map { String($0) } ?? ""; draft.cookMinutes = recipe.cookMinutes.map { String($0) } ?? ""
            draft.notes = recipe.notes ?? ""; draft.sourceUrl = recipe.sourceUrl ?? ""; draft.imageAssetId = recipe.imageAssetId ?? ""; draft.measurementStandard = recipe.measurementStandard
            draft.collectionIds = recipe.collectionIds; draft.tagIds = recipe.tagIds
            draft.groups = recipe.groups.map { RecipeGroupDraft(id: $0.id, title: $0.title) }
            draft.ingredients = recipe.ingredients.map { RecipeIngredientDraft(id: $0.id, groupId: $0.groupId ?? "", ingredientId: $0.ingredientId, ingredientName: $0.ingredientName, quantityText: $0.quantityText, kind: $0.kind, unit: $0.unit, preparation: $0.preparation ?? "", uncertain: $0.uncertain) }
            draft.instructions = recipe.instructions.map { RecipeInstructionDraft(id: $0.id, text: $0.text) }
            self.draft = draft; self.original = draft; self.etag = response.etag
        }
    }
    func addIngredient() { if draft.ingredients.count < 1000 { draft.ingredients.append(RecipeIngredientDraft()) } }
    func removeIngredient(_ id: String) { draft.ingredients.removeAll { $0.id == id }; if activeIngredient == id { cancelSearch() } }
    func duplicateIngredient(_ id: String) {
        guard draft.ingredients.count < 1000, let index = draft.ingredients.firstIndex(where: { $0.id == id }) else { return }
        var copy = draft.ingredients[index]; copy.id = UUID().uuidString; draft.ingredients.insert(copy, at: index + 1)
    }
    func moveIngredient(_ id: String, by offset: Int) {
        guard let index = draft.ingredients.firstIndex(where: { $0.id == id }), draft.ingredients.indices.contains(index + offset) else { return }
        draft.ingredients.swapAt(index, index + offset)
    }
    func addGroup() { if draft.groups.count < 1000 { draft.groups.append(RecipeGroupDraft()) } }
    func removeGroup(_ id: String) { draft.groups.removeAll { $0.id == id }; for index in draft.ingredients.indices where draft.ingredients[index].groupId == id { draft.ingredients[index].groupId = "" } }
    func moveGroup(_ id: String, by offset: Int) { if let index = draft.groups.firstIndex(where: { $0.id == id }), draft.groups.indices.contains(index + offset) { draft.groups.swapAt(index, index + offset) } }
    func addInstruction() { if draft.instructions.count < 1000 { draft.instructions.append(RecipeInstructionDraft()) } }
    func removeInstruction(_ id: String) { draft.instructions.removeAll { $0.id == id } }
    func duplicateInstruction(_ id: String) { if draft.instructions.count < 1000, let index = draft.instructions.firstIndex(where: { $0.id == id }) { var copy = draft.instructions[index]; copy.id = UUID().uuidString; draft.instructions.insert(copy, at: index + 1) } }
    func moveInstruction(_ id: String, by offset: Int) { if let index = draft.instructions.firstIndex(where: { $0.id == id }), draft.instructions.indices.contains(index + offset) { draft.instructions.swapAt(index, index + offset) } }
    func setIngredientName(_ id: String, _ value: String) {
        guard let index = draft.ingredients.firstIndex(where: { $0.id == id }) else { return }
        guard draft.ingredients[index].ingredientName != value else { return }
        draft.ingredients[index].ingredientName = value; draft.ingredients[index].ingredientId = ""; beginSearch(id)
    }
    func beginSearch(_ id: String) {
        guard let row = draft.ingredients.first(where: { $0.id == id }), row.ingredientId.isEmpty else { return }
        searchTask?.cancel(); activeIngredient = id; suggestions = []
        let query = row.ingredientName
        searchTask = Task { [weak self] in
            guard let self else { return }
            do {
                try await Task.sleep(for: .milliseconds(150))
                let encoded = query.addingPercentEncoding(withAllowedCharacters: .alphanumerics) ?? ""
                let response = try await self.requests.send(path: "/api/v1/ingredients?limit=8&q=" + encoded)
                let page = try APIJSON.decoder().decode(Components.Schemas.IngredientPage.self, from: response.data)
                try Task.checkCancellation()
                if self.activeIngredient == id, self.draft.ingredients.contains(where: { $0.id == id && $0.ingredientName == query && $0.ingredientId.isEmpty }) { self.suggestions = page.items }
            } catch is CancellationError { } catch { if !Task.isCancelled { self.error = apiErrorMessage(error) } }
        }
    }
    func cancelSearch() { searchTask?.cancel(); searchTask = nil; activeIngredient = nil; suggestions = [] }
    func selectIngredient(_ id: String, _ ingredient: Components.Schemas.IngredientRead) {
        guard let index = draft.ingredients.firstIndex(where: { $0.id == id }) else { return }
        draft.ingredients[index].ingredientId = ingredient.id; draft.ingredients[index].ingredientName = ingredient.canonicalName; cancelSearch()
    }
    func createIngredient(_ id: String) async {
        guard let row = draft.ingredients.first(where: { $0.id == id }), !row.ingredientName.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return }
        await perform {
            if self.ingredientRetries[id] == nil { self.ingredientRetries[id] = UUID() }
            let body = try APIJSON.encoder().encode(Components.Schemas.CreateIngredientWrite(canonicalName: row.ingredientName))
            let response = try await self.requests.send(path: "/api/v1/ingredients", method: "POST", body: body, retryKey: self.ingredientRetries[id])
            self.selectIngredient(id, try APIJSON.decoder().decode(Components.Schemas.IngredientRead.self, from: response.data)); self.ingredientRetries[id] = nil
        }
    }
    func makeWrite() throws -> Components.Schemas.RecipeWrite {
        guard !draft.title.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { throw RecipeEditorFailure.invalid("Enter a recipe title.") }
        guard let standard = draft.measurementStandard else { throw RecipeEditorFailure.invalid("Choose the recipe's source cup and spoon measures.") }
        guard draft.ingredients.allSatisfy({ $0.kind == .unknown || $0.unit != nil }) else { throw RecipeEditorFailure.invalid("Choose a source unit for each numeric quantity.") }
        guard !draft.defaultServings.isEmpty else { throw RecipeEditorFailure.invalid("Enter the default serving count.") }
        guard draft.ingredients.allSatisfy({ !$0.ingredientId.isEmpty && ($0.kind == .unknown || !$0.quantityText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty) }) else { throw RecipeEditorFailure.invalid("Choose or create each ingredient and enter its quantity, or mark it unspecified.") }
        guard !requiresReview || draft.acknowledgeUncertain else { throw RecipeEditorFailure.invalid("Review and acknowledge unresolved or uncertain ingredient quantities before saving.") }
        guard draft.instructions.allSatisfy({ !$0.text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }), draft.groups.allSatisfy({ !$0.title.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }) else { throw RecipeEditorFailure.invalid("Give each group a heading and each instruction some text, or remove unused rows.") }
        return Components.Schemas.RecipeWrite(title: draft.title, defaultServings: draft.defaultServings,
            groups: draft.groups.enumerated().map { Components.Schemas.RecipeGroupWrite(title: $0.element.title, position: Int32($0.offset), id: $0.element.id) },
            ingredients: draft.ingredients.enumerated().map { entry in
                // Unknown quantities never enter unit arithmetic. The existing wire format uses
                // item as its neutral storage value when no source unit was supplied.
                let unit = entry.element.unit ?? .item
                return Components.Schemas.RecipeIngredientWrite(position: Int32(entry.offset), ingredientId: entry.element.ingredientId, quantityText: entry.element.quantityText, kind: entry.element.kind, unit: unit, id: entry.element.id, groupId: optional(entry.element.groupId), preparation: optional(entry.element.preparation), uncertain: entry.element.uncertain) },
            instructions: draft.instructions.enumerated().map { Components.Schemas.RecipeInstructionWrite(position: Int32($0.offset), text: $0.element.text, id: $0.element.id) },
            collectionIds: draft.collectionIds, tagIds: draft.tagIds, description: optional(draft.description), imageAssetId: optional(draft.imageAssetId),
            prepMinutes: try minutes(draft.prepMinutes), cookMinutes: try minutes(draft.cookMinutes), notes: optional(draft.notes), sourceUrl: optional(draft.sourceUrl), measurementStandard: standard, acknowledgeUncertainIngredients: draft.acknowledgeUncertain)
    }
    private func optional(_ value: String) -> String? { value.isEmpty ? nil : value }
    private func minutes(_ value: String) throws -> Int32? { if value.isEmpty { return nil }; guard let integer = Int32(value), integer >= 0 else { throw RecipeEditorFailure.invalid("Use nonnegative whole minutes for preparation and cooking times.") }; return integer }
    func save() async {
        guard !busy, !submissionUncertain else { return }
        do {
            let request = try makeWrite()
            await perform {
                if let imported = self.importedSave { self.acceptImported(try await imported(request)); return }
                if self.recipeId == nil && self.retryKey == nil { self.retryKey = UUID() }
                let response = try await self.requests.send(path: self.recipeId.map { "/api/v1/recipes/" + $0 } ?? "/api/v1/recipes", method: self.recipeId == nil ? "POST" : "PUT", body: try APIJSON.encoder().encode(request), etag: self.recipeId == nil ? nil : self.etag, retryKey: self.recipeId == nil ? self.retryKey : nil)
                self.savedRecipe = try APIJSON.decoder().decode(Components.Schemas.RecipeRead.self, from: response.data); self.original = self.draft; self.retryKey = nil
            }
        } catch { if case RecipeEditorFailure.invalid(let message) = error { self.error = message } else { self.error = apiErrorMessage(error) } }
    }
    func uploadPhoto(_ source: Data) async {
        await perform {
            let jpeg = try await Task.detached(priority: .userInitiated) { try RecipePhoto.prepare(source) }.value
            try Task.checkCancellation()
            if self.photoRetry == nil { self.photoRetry = UUID() }
            let boundary = "RecipeManager-" + UUID().uuidString
            var body = Data("--\(boundary)\r\nContent-Disposition: form-data; name=\"file\"; filename=\"recipe.jpg\"\r\nContent-Type: image/jpeg\r\n\r\n".utf8)
            body.append(jpeg); body.append(Data("\r\n--\(boundary)--\r\n".utf8))
            let response = try await self.requests.send(path: "/api/v1/images", method: "POST", body: body, retryKey: self.photoRetry, contentType: "multipart/form-data; boundary=" + boundary)
            self.draft.imageAssetId = try APIJSON.decoder().decode(Components.Schemas.RecipeImageRead.self, from: response.data).id; self.photoRetry = nil
        }
    }
    private func perform(_ action: () async throws -> Void) async {
        guard !busy else { return }; busy = true; error = nil; fieldErrors = [:]; defer { busy = false }
        do { try await action() }
        catch {
            if case RecipeEditorFailure.invalid(let message) = error { self.error = message } else { self.error = apiErrorMessage(error) }
            if let rejected = error as? APIRejection { for field in rejected.problem?.errors ?? [] { if let path = field.path { fieldErrors[path] = field.message } } }
        }
    }
}

private enum RecipePhoto {
    static func prepare(_ source: Data) throws -> Data {
        guard source.count <= 50 * 1024 * 1024, let imageSource = CGImageSourceCreateWithData(source as CFData, nil),
            let image = CGImageSourceCreateThumbnailAtIndex(imageSource, 0, [kCGImageSourceCreateThumbnailFromImageAlways: true, kCGImageSourceThumbnailMaxPixelSize: 2000, kCGImageSourceCreateThumbnailWithTransform: true] as CFDictionary),
            let data = UIImage(cgImage: image).jpegData(compressionQuality: 0.85), data.count <= 10 * 1024 * 1024 else {
            throw RecipeEditorFailure.invalid("Choose a smaller supported photo. The prepared image must fit within 10 MiB.")
        }
        return data
    }
}

struct RecipeEditor: View {
    @Bindable var selection: ServerSelection
    @State private var model: RecipeEditorViewModel
    @State private var photo: PhotosPickerItem?
    @State private var discard = false
    @State private var showingOrganisers = false
    @Environment(\.dismiss) private var dismiss
    var onSaved: @MainActor (Components.Schemas.RecipeRead) -> Void
    init(selection: ServerSelection, recipeId: String? = nil, model: RecipeEditorViewModel? = nil, onSaved: @escaping @MainActor (Components.Schemas.RecipeRead) -> Void) {
        self.selection = selection; self.onSaved = onSaved; _model = State(initialValue: model ?? RecipeEditorViewModel(selection: selection, recipeId: recipeId))
    }
    var body: some View {
        VStack(spacing: 0) {
            if let error = model.error { Text(error).foregroundStyle(.red).padding().accessibilityIdentifier("recipe-editor-error") }
            if model.loading { ProgressView("Loading recipe") }
            Form {
                detailsSection
                groupsSection
                ingredientsSection
                instructionsSection
                Section("Organisation") { Button("Choose collections and tags") { showingOrganisers = true } }
                Section("Notes and source") {
                    TextField("Notes", text: $model.draft.notes, axis: .vertical).lineLimit(3...8)
                    TextField("Source URL", text: $model.draft.sourceUrl).keyboardType(.URL).textInputAutocapitalization(.never).autocorrectionDisabled()
                }
            }.scrollDismissesKeyboard(.interactively)
        }
        .disabled(model.busy || model.loading || model.submissionUncertain)
        .navigationTitle(model.importedSave != nil ? "Review imported recipe" : model.recipeId == nil ? "New recipe" : "Edit recipe")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            ToolbarItem(placement: .cancellationAction) { Button("Cancel") { if model.dirty { discard = true } else { dismiss() } }.disabled(model.busy) }
            ToolbarItem(placement: .confirmationAction) { Button(model.busy ? "Working…" : "Save recipe") { Task { await model.save() } }.disabled(model.busy || model.loading || model.submissionUncertain) }
        }
        .interactiveDismissDisabled(model.dirty || model.busy)
        .confirmationDialog("Discard unsaved recipe changes?", isPresented: $discard) { Button("Discard changes", role: .destructive) { dismiss() }; Button("Keep editing", role: .cancel) { } }
        .task { await model.load() }
        .task(id: photo) {
            guard let photo else { return }
            do { if let data = try await photo.loadTransferable(type: Data.self) { await model.uploadPhoto(data) } }
            catch { model.error = "Could not load that photo. Choose another image." }
        }
        .sheet(isPresented: $showingOrganisers) { NavigationStack {
            RecipeFiltersView(selection: selection, collectionIds: $model.draft.collectionIds, tagIds: $model.draft.tagIds)
        } }
        .onDisappear { model.cancelSearch() }
        .onChange(of: model.savedRecipe?.id) { _, _ in if let recipe = model.savedRecipe { onSaved(recipe) } }
    }
    private var detailsSection: some View {
        Section("Recipe details") {
            TextField("Title", text: $model.draft.title).accessibilityIdentifier("recipe-title")
            TextField("Description", text: $model.draft.description, axis: .vertical).lineLimit(2...4)
            TextField("Default servings", text: $model.draft.defaultServings).keyboardType(.decimalPad).accessibilityIdentifier("recipe-default-servings")
            TextField("Preparation minutes", text: $model.draft.prepMinutes).keyboardType(.numberPad)
            TextField("Cooking minutes", text: $model.draft.cookMinutes).keyboardType(.numberPad)
            Picker("Cup and spoon measures", selection: $model.draft.measurementStandard) {
                Text("Choose source measures").tag(Optional<Components.Schemas.MeasurementStandard>.none)
                Text("Australian").tag(Optional(Components.Schemas.MeasurementStandard.AU)); Text("US").tag(Optional(Components.Schemas.MeasurementStandard.US))
            }.accessibilityIdentifier("recipe-measurement-standard")
            PhotosPicker("Choose recipe photo", selection: $photo, matching: .images)
            if !model.draft.imageAssetId.isEmpty {
                PrivateRecipeImage(selection: selection, id: model.draft.imageAssetId, title: model.draft.title)
                Button("Remove photo from recipe") { model.draft.imageAssetId = "" }
            }
        }
    }
    private var groupsSection: some View {
        Section {
            DisclosureGroup("Ingredient groups") {
                ForEach(model.draft.groups) { item in
                    let group = bindingForRecipeRow(item, in: $model.draft.groups)
                    VStack(alignment: .leading) {
                        TextField("Group heading", text: group.title)
                        HStack {
                            Button("Move group up", systemImage: "arrow.up") { model.moveGroup(item.id, by: -1) }.disabled(model.draft.groups.first?.id == item.id)
                            Button("Move group down", systemImage: "arrow.down") { model.moveGroup(item.id, by: 1) }.disabled(model.draft.groups.last?.id == item.id)
                            Button("Remove group", systemImage: "trash", role: .destructive) { model.removeGroup(item.id) }
                        }.buttonStyle(.borderless)
                    }
                }
                Button("Add ingredient group") { model.addGroup() }.disabled(model.draft.groups.count >= 1000)
            }
        }
    }
    private var ingredientsSection: some View {
        Section("Structured ingredients") {
            ForEach(model.draft.ingredients) { row in
                RecipeIngredientRow(row: bindingForRecipeRow(row, in: $model.draft.ingredients), model: model, index: model.draft.ingredients.firstIndex(where: { $0.id == row.id }) ?? 0)
            }
            Button("Add ingredient", systemImage: "plus") { model.addIngredient() }.disabled(model.draft.ingredients.count >= 1000)
            if model.requiresReview {
                Toggle("I have reviewed unresolved or uncertain ingredient quantities", isOn: $model.draft.acknowledgeUncertain)
                Text("Unspecified quantities remain visible. No quantity is supplied automatically.").font(.footnote).foregroundStyle(.secondary)
            }
        }
    }
    private var instructionsSection: some View {
        Section("Instructions") {
            ForEach(model.draft.instructions) { item in
                let step = bindingForRecipeRow(item, in: $model.draft.instructions)
                VStack(alignment: .leading) {
                    TextField("Instruction \((model.draft.instructions.firstIndex(where: { $0.id == item.id }) ?? 0) + 1)", text: step.text, axis: .vertical).lineLimit(3...8).accessibilityIdentifier("recipe-instruction-" + item.id)
                    RecipeRowActions(label: "instruction", first: model.draft.instructions.first?.id == item.id, last: model.draft.instructions.last?.id == item.id,
                        up: { model.moveInstruction(item.id, by: -1) }, down: { model.moveInstruction(item.id, by: 1) }, duplicate: { model.duplicateInstruction(item.id) }, remove: { model.removeInstruction(item.id) })
                }
            }
            Button("Add instruction", systemImage: "plus") { model.addInstruction() }.disabled(model.draft.instructions.count >= 1000)
        }
    }
}

private struct RecipeIngredientRow: View {
    @Binding var row: RecipeIngredientDraft
    @Bindable var model: RecipeEditorViewModel
    let index: Int
    private enum Field: Hashable { case name, quantity }
    @FocusState private var focused: Field?
    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            TextField("Ingredient name \(index + 1)", text: Binding(get: { row.ingredientName }, set: { model.setIngredientName(row.id, $0) }))
                .textInputAutocapitalization(.never).autocorrectionDisabled().focused($focused, equals: .name)
                .onSubmit { if let ingredient = model.suggestions.first, model.activeIngredient == row.id { model.selectIngredient(row.id, ingredient); focused = .quantity } }
            if model.activeIngredient == row.id {
                ForEach(model.suggestions, id: \.id) { ingredient in
                    Button("Use ingredient \(ingredient.canonicalName)") { model.selectIngredient(row.id, ingredient); focused = .quantity }.buttonStyle(.borderless)
                }
                if !row.ingredientName.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                    Button("Create “\(row.ingredientName)” ingredient") { Task { await model.createIngredient(row.id); if !row.ingredientId.isEmpty { focused = .quantity } } }.buttonStyle(.borderless)
                }
            }
            if row.ingredientId.isEmpty && !row.ingredientName.isEmpty { Text("Choose a household match or create this ingredient.").font(.footnote).foregroundStyle(.secondary) }
            if let source = model.originalLines[row.id] { Text("Imported: " + source).font(.footnote).foregroundStyle(.secondary) }
            TextField(row.kind == .unknown ? "Quantity note \(index + 1)" : "Quantity \(index + 1)", text: $row.quantityText).keyboardType(.numbersAndPunctuation).textInputAutocapitalization(.never).autocorrectionDisabled().focused($focused, equals: .quantity)
            if let error = model.fieldErrors["ingredients[\(index)].quantityText"] { Text(error).foregroundStyle(.red).font(.footnote) }
            Picker("Unit \(index + 1)", selection: $row.unit) {
                Text(row.kind == .unknown ? "Not needed for unspecified quantity" : "Choose source unit").tag(Optional<Components.Schemas.IngredientUnit>.none)
                ForEach(Components.Schemas.IngredientUnit.allCases, id: \.self) { Text($0.displayName).tag(Optional($0)) }
            }.pickerStyle(.menu).buttonStyle(.borderless).accessibilityIdentifier("recipe-unit-" + row.id)
            Toggle("Quantity unspecified \(index + 1)", isOn: Binding(get: { row.kind == .unknown }, set: { row.kind = $0 ? .unknown : .numeric }))
            DisclosureGroup("Preparation notes and group") {
                TextField("Preparation notes", text: $row.preparation, axis: .vertical).lineLimit(2...4)
                if !model.draft.groups.isEmpty { Picker("Group", selection: $row.groupId) { Text("Ungrouped").tag(""); ForEach(model.draft.groups) { Text($0.title.isEmpty ? "Untitled group" : $0.title).tag($0.id) } }.pickerStyle(.menu).buttonStyle(.borderless).accessibilityIdentifier("recipe-group-" + row.id) }
                Toggle("Quantity uncertain", isOn: $row.uncertain)
            }
            RecipeRowActions(label: "ingredient \(index + 1)", first: index == 0, last: index == model.draft.ingredients.count - 1,
                up: { model.moveIngredient(row.id, by: -1) }, down: { model.moveIngredient(row.id, by: 1) }, duplicate: { model.duplicateIngredient(row.id) }, remove: { model.removeIngredient(row.id) })
        }
        .onChange(of: focused) { _, value in if value == .name { model.beginSearch(row.id) } }
    }
}

private struct RecipeRowActions: View {
    let label: String
    let first: Bool
    let last: Bool
    var up: () -> Void
    var down: () -> Void
    var duplicate: () -> Void
    var remove: () -> Void
    var body: some View {
        HStack {
            Button(action: up) { Label("Move \(label) up", systemImage: "arrow.up").frame(minWidth: 44, minHeight: 44) }.disabled(first)
            Button(action: down) { Label("Move \(label) down", systemImage: "arrow.down").frame(minWidth: 44, minHeight: 44) }.disabled(last)
            Button(action: duplicate) { Label("Duplicate \(label)", systemImage: "plus.square.on.square").frame(minWidth: 44, minHeight: 44) }
            Button(role: .destructive, action: remove) { Label("Remove \(label)", systemImage: "trash").frame(minWidth: 44, minHeight: 44) }
        }.labelStyle(.iconOnly).buttonStyle(.borderless)
    }
}

extension Components.Schemas.IngredientUnit {
    var displayName: String {
        switch self { case .g: "g"; case .kg: "kg"; case .ml: "ml"; case .l: "L"; case .tsp: "Teaspoon"; case .tbsp: "Tablespoon"; case .cup: "Cup"; case .item: "Whole item" }
    }
}


// SwiftUI can read a disappearing control after deletion. Resolve by identity instead of a stale
// array index, retain its rendering snapshot, and ignore late writes to a row that no longer exists.
@MainActor func bindingForRecipeRow<Row: Identifiable>(_ row: Row, in rows: Binding<[Row]>) -> Binding<Row> {
    Binding(get: { rows.wrappedValue.first { $0.id == row.id } ?? row }, set: { value in
        guard let index = rows.wrappedValue.firstIndex(where: { $0.id == row.id }) else { return }
        rows.wrappedValue[index] = value
    })
}
