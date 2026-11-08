import SwiftUI
import Observation
import RecipeManagerAPI

@Observable @MainActor final class MealEditorModel {
    var draft: MealDraft
    var original: MealDraft
    var meal: Components.Schemas.ScheduledMealRead?
    var recipes: [Components.Schemas.RecipeRead] = []
    var query = ""
    var cursor: String?
    var error: String?
    var busy = false
    var saved = false
    var conflict = false
    var action = "save"
    var confirmConsumedRemoval = false
    let type: Components.Schemas.MealType
    private let requests: ScopedAPI
    private var etag: String?
    private var retry = UUID()
    var dirty: Bool { draft != original }
    var contentChanged: Bool {
        draft.recipeId != original.recipeId || draft.prepared != original.prepared || draft.eaten != original.eaten || draft.label != original.label
    }
    init(selection: ServerSelection, meal: Components.Schemas.ScheduledMealRead?, type: Components.Schemas.MealType, date: String, slotId: String) {
        requests = ScopedAPI(selection: selection); self.meal = meal; self.type = type
        let value = MealDraft(date: date, slotId: slotId, recipeId: meal?.cookingEvent?.sourceRecipeId ?? "", recipeRevision: nil,
                              prepared: meal?.cookingEvent?.plannedPrepared ?? "2", eaten: meal?.cookingEvent?.plannedEaten ?? "2", label: meal?.label ?? "")
        draft = value; original = value
    }
    func load() async {
        await perform {
            if let id = self.meal?.id {
                let response = try await self.requests.send(path: "/api/v1/meals/" + id)
                self.meal = try APIJSON.decoder().decode(Components.Schemas.ScheduledMealRead.self, from: response.data); self.etag = response.etag
                if !self.dirty, let meal = self.meal {
                    self.draft = MealDraft(date: meal.date, slotId: meal.slotId, recipeId: meal.cookingEvent?.sourceRecipeId ?? "", recipeRevision: nil,
                                           prepared: meal.cookingEvent?.plannedPrepared ?? "2", eaten: meal.cookingEvent?.plannedEaten ?? "2", label: meal.label ?? "")
                    self.original = self.draft
                }
            }
        }
        if type == .cook { await search() }
    }
    func search(append: Bool = false) async {
        await perform {
            let q = self.query.addingPercentEncoding(withAllowedCharacters: .alphanumerics) ?? ""
            let next = append ? self.cursor : nil
            let response = try await self.requests.send(path: "/api/v1/recipes?limit=25&q=\(q)" + (next.map { "&cursor=" + ($0.addingPercentEncoding(withAllowedCharacters: .alphanumerics) ?? "") } ?? ""))
            let page = try APIJSON.decoder().decode(Components.Schemas.RecipeListPage.self, from: response.data)
            if append { let ids = Set(self.recipes.map(\.id)); self.recipes += page.items.filter { !ids.contains($0.id) } } else { self.recipes = page.items }
            self.cursor = page.nextCursor
        }
    }
    func choose(_ recipe: Components.Schemas.RecipeRead) {
        draft.recipeId = recipe.id; draft.recipeRevision = recipe.revision
        if meal == nil && draft.prepared == original.prepared && draft.eaten == original.eaten { draft.prepared = recipe.defaultServings; draft.eaten = recipe.defaultServings }
    }
    func submit(_ action: String = "save") async {
        guard !["move", "duplicate"].contains(action) || !contentChanged else {
            error = "Save meal content changes before moving or duplicating to another date and slot."; return
        }
        self.action = action; conflict = false
        await perform {
            let response: APIResponse
            if let meal = self.meal {
                let path = "/api/v1/meals/" + meal.id
                let source = meal.leftovers?.sourceRevision
                switch action {
                case "duplicate": response = try await self.requests.send(path: path + "/duplicate", method: "POST", body: try APIJSON.encoder().encode(Components.Schemas.DuplicateMealWrite(date: self.draft.date, slotId: self.draft.slotId, sourceRevision: source)), etag: self.etag, retryKey: self.retry)
                case "move": response = try await self.requests.send(path: path + "/position", method: "PUT", body: try APIJSON.encoder().encode(Components.Schemas.MealPositionWrite(date: self.draft.date, slotId: self.draft.slotId, sourceRevision: source)), etag: self.etag)
                case "delete": response = try await self.requests.send(path: path, method: "DELETE", body: try APIJSON.encoder().encode(Components.Schemas.DeleteMealWrite(sourceRevision: source, confirmConsumedRemoval: self.confirmConsumedRemoval)), etag: self.etag)
                default:
                    let body = try self.writeBody()
                    response = try await self.requests.send(path: path + (self.type == .cook ? "/cooking" : "/other"), method: "PUT", body: body, etag: self.etag)
                }
            } else {
                response = try await self.requests.send(path: "/api/v1/meals/" + (self.type == .cook ? "cooking" : "other"), method: "POST", body: try self.writeBody(), retryKey: self.retry)
            }
            _ = response; self.saved = true
        }
    }
    private func writeBody() throws -> Data {
        if type == .cook { return try APIJSON.encoder().encode(draft.cookingWrite()) }
        return try APIJSON.encoder().encode(Components.Schemas.OtherMealWrite(date: draft.date, slotId: draft.slotId, label: draft.label))
    }
    private func perform(_ action: () async throws -> Void) async {
        guard !busy else { return }; busy = true; error = nil; defer { busy = false }
        do { try await action() } catch {
            self.error = apiErrorMessage(error)
            if let rejection = error as? APIRejection, rejection.status == 409, meal?.cookingEvent != nil,
               ["save", "move", "delete"].contains(self.action) { conflict = true }
        }
    }
}

struct MealEditorView: View {
    let selection: ServerSelection
    let slots: [Components.Schemas.MealSlotRead]
    @Environment(\.dismiss) private var dismiss
    @State private var model: MealEditorModel
    @State private var discard = false
    @State private var deleting = false
    @State private var source = false
    init(selection: ServerSelection, meal: Components.Schemas.ScheduledMealRead?, type: Components.Schemas.MealType, date: String, slotId: String, slots: [Components.Schemas.MealSlotRead]) {
        self.selection = selection; self.slots = slots
        _model = State(initialValue: MealEditorModel(selection: selection, meal: meal, type: type, date: date, slotId: slotId))
    }
    var body: some View {
        Form {
            if let error = model.error { Text(error).foregroundStyle(.red).accessibilityIdentifier("meal-editor-error") }
            Section("When") { MealPositionFields(date: $model.draft.date, slotId: $model.draft.slotId, slots: slots) }
            if model.type == .cook {
                Section("Recipe") {
                    TextField("Find recipe", text: $model.query).submitLabel(.search).onSubmit { Task { await model.search() } }
                    Button("Search recipes") { Task { await model.search() } }
                    if let cook = model.meal?.cookingEvent { Text("Scheduled recipe: \(cook.recipeSnapshot.title)").font(.caption) }
                    ForEach(model.recipes, id: \.id) { recipe in
                        Button { model.choose(recipe) } label: { HStack { Text(recipe.title); if model.draft.recipeId == recipe.id { Image(systemName: "checkmark") } } }
                            .accessibilityIdentifier("meal-recipe-\(recipe.id)")
                    }
                    if model.cursor != nil { Button("More recipes") { Task { await model.search(append: true) } } }
                }
                Section("Planned portions") {
                    PortionField(title: "Portions to prepare", text: $model.draft.prepared)
                    PortionField(title: "Portions to eat", text: $model.draft.eaten)
                    Text("Leftovers are available for explicit scheduling. Saving does not confirm cooking or consumption.").font(.caption)
                }
                if let cook = model.meal?.cookingEvent {
                    Section("Cooking and recipe snapshot") {
                        Text("\(cook.availablePortions) available (\(cook.availabilityBasis))")
                        Button("Actual quantities, leftovers and recipe refresh") { source = true }
                        Text("Recipe edits keep this event's saved recipe. Refresh it explicitly to use the latest version.").font(.caption)
                    }
                }
            } else {
                Section("Other meal") { TextField("Meal description", text: $model.draft.label) }
            }
            Section {
                Button("Save meal") { Task { await model.submit() } }.accessibilityIdentifier("meal-save")
                if model.meal != nil {
                    Button("Move meal to selected date and slot") { Task { await model.submit("move") } }.disabled(model.contentChanged)
                    Button("Duplicate meal to selected date and slot") { Task { await model.submit("duplicate") } }.disabled(model.contentChanged)
                    if model.contentChanged { Text("Save meal content changes before moving or duplicating.").font(.caption) }
                    Button("Delete meal", role: .destructive) { deleting = true }
                }
                if model.conflict { Button("Resolve dependent leftovers") { source = true } }
                if model.error != nil { Button("Reload current version; keep entered values") { Task { await model.load() } } }
            }
        }
        .disabled(model.busy).navigationTitle(model.type == .cook ? "Cook recipe" : "Other meal")
        .interactiveDismissDisabled(model.dirty || model.busy)
        .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Cancel") { if model.dirty { discard = true } else { dismiss() } }.disabled(model.busy) } }
        .confirmationDialog("Discard meal changes?", isPresented: $discard) { Button("Discard changes", role: .destructive) { dismiss() } }
        .confirmationDialog("Delete meal?", isPresented: $deleting) { Button("Delete meal", role: .destructive) { Task { await model.submit("delete") } } }
        .task { await model.load() }
        .onChange(of: model.saved) { _, saved in if saved { dismiss() } }
        .sheet(isPresented: $source, onDismiss: { Task { if !model.conflict { await model.load() } } }) {
            if let meal = model.meal, let cook = meal.cookingEvent {
                NavigationStack {
                    CookingSourceView(selection: selection, sourceId: cook.id, slots: slots,
                                      proposed: model.conflict ? SourceProposal(action: model.action, draft: model.draft) : nil,
                                      completed: { if model.conflict { model.saved = true } })
                }
            }
        }
    }
}
