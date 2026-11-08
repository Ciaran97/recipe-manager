import Foundation
import Observation
import RecipeManagerAPI

@Observable @MainActor final class ShoppingListModel {
    enum Editor: String, Identifiable { case create, manual, item, rename, reconcile; var id: String { rawValue } }
    var lists: [Components.Schemas.ShoppingListBrief] = []
    var current: Components.Schemas.ShoppingListRead?
    var cursor: String?
    var editor: Editor?
    var source = ShoppingSourceDraft()
    private var addition: ShoppingSourceDraft?
    var name = ""
    var label = ""
    var categoryId = ""
    var quantity = ShoppingQuantityDraft()
    var recipeQuery = ""
    var recipes: [Components.Schemas.RecipeRead] = []
    var recipeCursor: String?
    var categories: [Components.Schemas.CategoryRead] = []
    var item: Components.Schemas.ShoppingItemRead?
    var preview: Components.Schemas.ShoppingPreview?
    var choices: [String: Components.Schemas.ShoppingReconciliationChoice] = [:]
    var busy = false
    var error: String?
    var notice: String?
    var outdated = false
    var sourcesChanged = false
    var deletedRemotely = false
    var showRemoved = false
    var needsReview = false
    var reviewed = false
    private(set) var etag: String?
    private let requests: ScopedAPI
    private var initial = ""
    private var initialQuantity = ShoppingQuantityDraft()
    private var retry: UUID?
    private var sourceGeneration = 0
    private var initialized = false
    private var pendingRemote = false
    private var observedSequence: UInt64 = 0
    init(selection: ServerSelection) { requests = ScopedAPI(selection: selection) }
    var dirty: Bool { editor != nil && (fingerprint != initial || preview != nil) }
    private var fingerprint: String { "\(name)|\(label)|\(categoryId)|\(quantity)|\(source)" }
    var groups: [(String, [Components.Schemas.ShoppingItemRead])] {
        let visible = (current?.items ?? []).filter { showRemoved || !$0.removed }
        let grouped = Dictionary(grouping: visible, by: { $0.categoryName ?? "Other" })
        return grouped.keys.sorted().map { ($0, grouped[$0] ?? []) }
    }
    func initialize() async {
        guard !initialized else { return }
        await perform {
            let settings = try await self.read(Components.Schemas.HouseholdSettingsRead.self, "/api/v1/household/settings")
            self.source.from = PlannerDates.monday(PlannerDates.today(timezone: settings.timezone))
            self.source.to = PlannerDates.add(self.source.from, days: 6)
            try await self.index()
            self.categories = []
            var cursor: String?
            repeat {
                let page = try await self.read(Components.Schemas.CategoryPage.self, "/api/v1/ingredient-categories?limit=100" + (cursor.map { "&cursor=\(self.escape($0))" } ?? ""))
                self.categories += page.items; cursor = page.nextCursor
            } while cursor != nil
            self.initialized = true
        }
    }
    func loadIndex(append: Bool = false) async { await perform { try await self.index(append: append) } }
    private func index(append: Bool = false) async throws {
        let page = try await read(Components.Schemas.ShoppingListPageRead.self, "/api/v1/shopping-lists?limit=25" + (append ? cursor.map { "&cursor=\(escape($0))" } ?? "" : ""))
        if append { let ids = Set(lists.map(\.id)); lists += page.items.filter { !ids.contains($0.id) } } else { lists = page.items }
        cursor = page.nextCursor
    }
    func open(_ id: String) async {
        guard !dirty else { return }
        await perform { try self.accept(try await self.requests.send(path: "/api/v1/shopping-lists/" + id)); self.sourcesChanged = false }
    }
    func reloadReview() async {
        guard let current else { return }
        await perform {
            try self.accept(try await self.requests.send(path: "/api/v1/shopping-lists/" + current.id))
            self.preview = nil; self.needsReview = true; self.reviewed = false
        }
    }
    func begin(_ next: Editor, item: Components.Schemas.ShoppingItemRead? = nil) {
        guard !busy else { return }
        cancel(); name = current?.name ?? ""; label = ""; categoryId = ""; quantity = ShoppingQuantityDraft(); self.item = item
        switch next {
        case .create: name = ""; source.kind = "mealRange"; source.basis = .planned; source.entries = []
        case .item:
            if let item { label = item.label; quantity = ShoppingQuantityDraft(item) }
        case .reconcile:
            if let selection = current?.selection {
                source.kind = selection.kind.rawValue; source.from = selection.from ?? source.from; source.to = selection.to ?? source.to
                source.basis = current?.basis ?? .planned
                let contributions = current?.items.flatMap(\.contributions) ?? []
                source.entries = (selection.entries ?? []).map { entry in .init(recipeId: entry.recipeId, title: contributions.first(where: { $0.sourceRecipeId == entry.recipeId })?.sourceLabel ?? "Selected recipe", servings: entry.servings) }
            }
        default: break
        }
        editor = next; initialQuantity = quantity; initial = fingerprint
    }
    func cancel() { addition = nil; editor = nil; item = nil; preview = nil; choices = [:]; needsReview = false; reviewed = false; retry = nil; error = nil; notice = nil }
    func sourceEdited() { preview = nil; reviewed = false }
    func receive(_ change: SyncDelta) {
        guard change.sequence > observedSequence else { return }; observedSequence = change.sequence
        if !change.domains.isDisjoint(with: [.recipes, .planner, .settings]) { requirementsChanged() }
        if change.domains.contains(.shopping) { Task { await self.synchronize() } }
    }
    func synchronize() async {
        guard initialized else { return }
        if busy { pendingRemote = true; return }
        await perform(background: true) {
            try await self.index()
            guard let current = self.current else { return }
            do {
                let response = try await self.requests.send(path: "/api/v1/shopping-lists/" + current.id, etag: self.etag)
                if response.status == 304 { return }
                if self.editor != nil { self.remoteInvalidation(); return }
                try self.accept(response)
            } catch let rejected as APIRejection where rejected.status == 404 {
                self.deletedRemotely = true; self.outdated = true
            }
        }
    }
    func remoteInvalidation() { outdated = true; if editor == .reconcile { needsReview = true; reviewed = false } }
    func requirementsChanged() { sourceGeneration += 1; sourcesChanged = current?.selection != nil; if preview != nil { needsReview = true; reviewed = false } }
    func searchRecipes(append: Bool = false) async {
        await perform {
            let page = try await self.read(Components.Schemas.RecipeListPage.self, "/api/v1/recipes?limit=25&q=\(self.escape(self.recipeQuery))" + (append ? self.recipeCursor.map { "&cursor=\(self.escape($0))" } ?? "" : ""))
            if append { let ids = Set(self.recipes.map(\.id)); self.recipes += page.items.filter { !ids.contains($0.id) } } else { self.recipes = page.items }
            self.recipeCursor = page.nextCursor
        }
    }
    func addRecipe(_ recipe: Components.Schemas.RecipeRead) {
        guard !source.entries.contains(where: { $0.recipeId == recipe.id }) else { return }
        source.entries.append(.init(recipeId: recipe.id, title: recipe.title, servings: recipe.defaultServings)); sourceEdited()
    }
    func create() async {
        await perform {
            if self.retry == nil { self.retry = UUID() }
            let response: APIResponse
            if self.source.kind == "manual" { response = try await self.write("/api/v1/shopping-lists", .init(name: self.name) as Components.Schemas.ShoppingListWrite, retry: self.retry) }
            else { response = try await self.write("/api/v1/shopping-lists/generate", Components.Schemas.ShoppingGenerateWrite(name: self.name, selection: self.source.selection(), basis: .planned), retry: self.retry) }
            try self.accept(response); self.cancel(); self.notice = "Shopping list created."
        }
    }
    func saveManual() async {
        guard let current else { return }
        await perform {
            if self.retry == nil { self.retry = UUID() }
            let body = Components.Schemas.ShoppingManualItemWrite(label: self.label, quantityText: self.quantity.text, kind: self.quantity.kind, unit: self.quantity.unit, measurementStandard: self.quantity.standard, categoryId: self.categoryId.isEmpty ? nil : self.categoryId)
            try self.accept(try await self.write("/api/v1/shopping-lists/\(current.id)/items", body, etag: self.etag, retry: self.retry))
            self.cancel(); self.notice = "Manual item added."
        }
    }
    func saveItem() async {
        guard let item else { return }
        await perform {
            let override: Components.Schemas.ShoppingItemWrite.quantityOverridePayload? = self.quantity == self.initialQuantity ? nil : try self.copy(self.quantity.write())
            try await self.change(item, .init(quantityOverride: override, clearOverride: false, label: item.manual && self.label != item.label ? self.label : nil))
            self.cancel(); self.notice = "Shopping item saved."
        }
    }
    func purchase(_ item: Components.Schemas.ShoppingItemRead) async { guard !dirty else { return }; await perform { try await self.change(item, .init(clearOverride: false, purchased: !item.purchased)) } }
    func restore(_ item: Components.Schemas.ShoppingItemRead) async { await perform { try await self.change(item, .init(clearOverride: false, removed: false)) } }
    func clearOverride() async {
        guard let item else { return }
        await perform { try await self.change(item, .init(clearOverride: true)); self.cancel() }
    }
    func remove(_ item: Components.Schemas.ShoppingItemRead) async {
        guard let current else { return }
        await perform { try self.accept(try await self.requests.send(path: "/api/v1/shopping-lists/\(current.id)/items/\(item.id)", method: "DELETE", etag: self.etag)) }
    }
    func rename() async {
        guard let current else { return }
        await perform { try self.accept(try await self.write("/api/v1/shopping-lists/\(current.id)", Components.Schemas.ShoppingListWrite(name: self.name), method: "PUT", etag: self.etag)); self.cancel() }
    }
    func deleteList() async {
        guard let current else { return }
        await perform {
            _ = try await self.requests.send(path: "/api/v1/shopping-lists/" + current.id, method: "DELETE", etag: self.etag)
            self.lists.removeAll { $0.id == current.id }; self.current = nil; self.cancel()
        }
    }
    func addSources(_ incoming: ShoppingSourceDraft, to id: String) async -> Bool {
        guard !dirty else { error = "Finish or cancel your current edit before adding ingredients."; return false }
        await open(id)
        guard current?.id == id else { return false }
        guard current?.basis == .planned else { error = "Add to a list using planned quantities, or create a new list."; return false }
        begin(.reconcile); addition = incoming; source.kind = "combined"; source.basis = .planned
        await previewChanges()
        return true
    }
    func previewChanges() async {
        guard let current else { return }
        let generation = sourceGeneration
        await perform {
            let selected: Components.Schemas.ShoppingPreviewWrite.selectionPayload?
            if let addition = self.addition { selected = try self.copy(addition.selection()) }
            else if self.source.kind == "combined" { selected = try current.selection.map { try self.copy($0) } }
            else { selected = try self.copy(self.source.selection()) }
            let body = Components.Schemas.ShoppingPreviewWrite(selection: selected, basis: self.addition == nil ? .init(rawValue: self.source.generationBasis.rawValue) : .planned)
            let endpoint = self.addition == nil ? "reconciliation" : "sources"
            let response = try await self.write("/api/v1/shopping-lists/\(current.id)/\(endpoint)/preview", body, etag: self.etag)
            let value = try APIJSON.decoder().decode(Components.Schemas.ShoppingPreview.self, from: response.data)
            var choices: [String: Components.Schemas.ShoppingReconciliationChoice] = [:]
            for change in value.changes {
                if let old = self.choices[change.key], change.allowedChoices.contains(old.choice) { choices[change.key] = old }
                else { choices[change.key] = .init(key: change.key, choice: change.defaultChoice) }
            }
            self.choices = choices; self.preview = value; self.sourcesChanged = generation != self.sourceGeneration
            self.needsReview = self.needsReview || value.changes.contains(where: \.conflict); self.reviewed = false
        }
    }
    func applyChanges() async {
        guard let current, let preview, !sourcesChanged, !needsReview || reviewed else { return }
        guard !preview.changes.contains(where: { $0.purchaseDecisionRequired && choices[$0.key]?.choice == .acceptNew && choices[$0.key]?.purchased == nil }) else { error = "Choose the purchase state for each increased requirement."; return }
        await perform {
            let body = Components.Schemas.ShoppingApplyWrite(listRevision: preview.listRevision, householdSourceRevision: preview.householdSourceRevision, sourceRevisions: preview.sourceRevisions, policyVersion: preview.policyVersion, selection: try preview.selection.map { try self.copy($0) }, basis: preview.basis, choices: preview.changes.compactMap { self.choices[$0.key] })
            try self.accept(try await self.write("/api/v1/shopping-lists/\(current.id)/reconciliation/apply", body, etag: self.etag))
            self.cancel(); self.notice = "Reviewed requirements applied."
        }
    }
    private func change(_ item: Components.Schemas.ShoppingItemRead, _ body: Components.Schemas.ShoppingItemWrite) async throws {
        guard let current else { return }
        try accept(try await write("/api/v1/shopping-lists/\(current.id)/items/\(item.id)", body, method: "PUT", etag: etag))
    }
    private func accept(_ response: APIResponse) throws {
        guard let tag = response.etag else { throw SessionFailure.invalidResponse }
        let value = try APIJSON.decoder().decode(Components.Schemas.ShoppingListRead.self, from: response.data)
        current = value; etag = tag; outdated = false; deletedRemotely = false
        lists.removeAll { $0.id == value.id }; lists.insert(.init(id: value.id, revision: value.revision, name: value.name, basis: value.basis, summary: value.summary), at: 0)
    }
    private func read<T: Decodable>(_ type: T.Type, _ path: String) async throws -> T { try APIJSON.decoder().decode(type, from: await requests.send(path: path).data) }
    private func write<T: Encodable>(_ path: String, _ body: T, method: String = "POST", etag: String? = nil, retry: UUID? = nil) async throws -> APIResponse {
        try await requests.send(path: path, method: method, body: APIJSON.encoder().encode(body), etag: etag, retryKey: retry)
    }
    // The generator inlines nullable references; these structurally identical DTO wrappers share the contract, not business logic.
    private func copy<T: Encodable, U: Decodable>(_ value: T) throws -> U { try APIJSON.decoder().decode(U.self, from: APIJSON.encoder().encode(value)) }
    private func escape(_ value: String) -> String { value.addingPercentEncoding(withAllowedCharacters: .alphanumerics) ?? "" }
    private func perform(background: Bool = false, _ action: () async throws -> Void) async {
        guard !busy else { return }; busy = true
        if !background { error = nil; notice = nil }
        defer {
            busy = false
            if pendingRemote { pendingRemote = false; Task { await self.synchronize() } }
        }
        do { try await action() }
        catch {
            self.error = apiErrorMessage(error)
            if let rejected = error as? APIRejection, rejected.status == 409 || rejected.status == 412 { outdated = true; needsReview = true; reviewed = false }
        }
    }
}
