import SwiftUI
import Observation
import RecipeManagerAPI

@Observable @MainActor
final class CatalogueViewModel {
    var items: [Components.Schemas.IngredientRead] = []
    var categories: [Components.Schemas.CategoryRead] = []
    var selected: Components.Schemas.IngredientRead?
    var selectedCategory: Components.Schemas.CategoryRead?
    var editingAlias: Components.Schemas.IngredientAliasRead?
    var query = ""
    var name = ""
    var categoryId = ""
    var keepPreviousName = false
    var aliasName = ""
    var categoryName = ""
    var categoryOrder: Int32 = 0
    var nextCursor: String?
    var loading = true
    var busy = false
    var error: String?
    var notice: String?
    private var ingredientETag: String?
    private var categoryETag: String?
    private var ingredientRetry: UUID?
    private var aliasRetry: UUID?
    private var categoryRetry: UUID?
    private let selection: ServerSelection
    private let requests: ScopedAPI
    var sortedCategories: [Components.Schemas.CategoryRead] { categories.sorted { $0.sortOrder == $1.sortOrder ? $0.name.localizedCompare($1.name) == .orderedAscending : $0.sortOrder < $1.sortOrder } }
    var ingredientDirty: Bool { name != (selected?.canonicalName ?? "") || categoryId != (selected?.categoryId ?? "") || keepPreviousName }
    var dirty: Bool { ingredientDirty || aliasName != (editingAlias?.name ?? "") }
    var categoryDirty: Bool { categoryName != (selectedCategory?.name ?? "") || categoryOrder != (selectedCategory?.sortOrder ?? 0) }
    init(selection: ServerSelection) { self.selection = selection; requests = ScopedAPI(selection: selection) }
    func load(background: Bool = false) async {
        if !background { loading = true }
        await perform(background: background) { try await self.loadIngredients(append: false); try await self.loadCategories() }
        if !background { loading = false }
    }
    func search(append: Bool = false) async {
        guard query.count <= 200 else { error = "Use up to 200 characters to search."; return }
        await perform { try await self.loadIngredients(append: append) }
    }
    private func escaped(_ value: String) -> String { value.addingPercentEncoding(withAllowedCharacters: .alphanumerics) ?? "" }
    private func loadIngredients(append: Bool) async throws {
        let cursor = append ? nextCursor : nil
        let path = "/api/v1/ingredients?limit=25" + (query.isEmpty ? "" : "&q=" + escaped(query)) + (cursor.map { "&cursor=" + escaped($0) } ?? "")
        let response = try await requests.send(path: path)
        let page = try APIJSON.decoder().decode(Components.Schemas.IngredientPage.self, from: response.data)
        if append {
            for item in page.items { if let index = items.firstIndex(where: { $0.id == item.id }) { items[index] = item } else { items.append(item) } }
        } else { items = page.items }
        nextCursor = page.nextCursor
    }
    private func loadCategories() async throws {
        var cursor: String?; var result: [Components.Schemas.CategoryRead] = []
        repeat {
            let response = try await requests.send(path: "/api/v1/ingredient-categories?limit=100" + (cursor.map { "&cursor=" + escaped($0) } ?? ""))
            let page = try APIJSON.decoder().decode(Components.Schemas.CategoryPage.self, from: response.data)
            result.append(contentsOf: page.items); cursor = page.nextCursor
        } while cursor != nil
        categories = result
    }
    func startNew() { selected = nil; ingredientETag = nil; ingredientRetry = nil; revert() }
    func revert() { name = selected?.canonicalName ?? ""; categoryId = selected?.categoryId ?? ""; keepPreviousName = false; clearAlias() }
    func choose(_ id: String) async {
        await perform { try self.accept(try await self.requests.send(path: "/api/v1/ingredients/" + id)) }
    }
    func save() async {
        guard !name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty, name.count <= 200 else { error = "Enter a name of up to 200 characters."; return }
        await perform {
            let body: Data; let method: String; let path: String
            if let selected = self.selected {
                body = try APIJSON.encoder().encode(Components.Schemas.UpdateIngredientWrite(canonicalName: self.name, categoryId: self.categoryId.isEmpty ? nil : self.categoryId, retainOldNameAsAlias: self.keepPreviousName))
                method = "PUT"; path = "/api/v1/ingredients/" + selected.id
            } else {
                body = try APIJSON.encoder().encode(Components.Schemas.CreateIngredientWrite(canonicalName: self.name, categoryId: self.categoryId.isEmpty ? nil : self.categoryId))
                if self.ingredientRetry == nil { self.ingredientRetry = UUID() }; method = "POST"; path = "/api/v1/ingredients"
            }
            try self.accept(try await self.requests.send(path: path, method: method, body: body, etag: self.selected == nil ? nil : self.ingredientETag, retryKey: self.selected == nil ? self.ingredientRetry : nil))
            self.ingredientRetry = nil; self.notice = "Ingredient saved."
        }
    }
    func delete() async {
        guard let selected else { return }
        await perform {
            _ = try await self.requests.send(path: "/api/v1/ingredients/" + selected.id, method: "DELETE", etag: self.ingredientETag)
            self.items.removeAll { $0.id == selected.id }; self.startNew(); self.notice = "Ingredient deleted."
        }
    }
    func editAlias(_ alias: Components.Schemas.IngredientAliasRead) { editingAlias = alias; aliasName = alias.name; aliasRetry = nil }
    func clearAlias() { editingAlias = nil; aliasName = ""; aliasRetry = nil }
    func saveAlias() async {
        guard let selected, !ingredientDirty else { error = "Save or discard ingredient details before changing aliases."; return }
        guard !aliasName.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty, aliasName.count <= 200 else { error = "Enter an alias of up to 200 characters."; return }
        await perform {
            let body = try APIJSON.encoder().encode(Components.Schemas.AliasWrite(name: self.aliasName))
            if self.editingAlias == nil && self.aliasRetry == nil { self.aliasRetry = UUID() }
            let path = "/api/v1/ingredients/" + selected.id + "/aliases" + (self.editingAlias.map { "/" + $0.id } ?? "")
            try self.accept(try await self.requests.send(path: path, method: self.editingAlias == nil ? "POST" : "PUT", body: body, etag: self.ingredientETag, retryKey: self.editingAlias == nil ? self.aliasRetry : nil))
            self.clearAlias(); self.notice = "Alias saved."
        }
    }
    func removeAlias(_ alias: Components.Schemas.IngredientAliasRead) async {
        guard let selected, !ingredientDirty else { error = "Save or discard ingredient details before changing aliases."; return }
        await perform {
            try self.accept(try await self.requests.send(path: "/api/v1/ingredients/" + selected.id + "/aliases/" + alias.id, method: "DELETE", etag: self.ingredientETag))
            self.clearAlias(); self.notice = "Alias removed."
        }
    }
    func startCategory() { selectedCategory = nil; categoryName = ""; categoryOrder = 0; categoryRetry = nil; categoryETag = nil }
    func revertCategory() { categoryName = selectedCategory?.name ?? ""; categoryOrder = selectedCategory?.sortOrder ?? 0 }
    func chooseCategory(_ id: String) async {
        await perform {
            let response = try await self.requests.send(path: "/api/v1/ingredient-categories/" + id)
            self.selectedCategory = try APIJSON.decoder().decode(Components.Schemas.CategoryRead.self, from: response.data)
            self.categoryETag = response.etag; self.revertCategory()
        }
    }
    func saveCategory() async {
        guard !categoryName.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty, categoryName.count <= 200, categoryOrder >= 0 else { error = "Enter a category name and nonnegative display order."; return }
        await perform {
            let body = try APIJSON.encoder().encode(Components.Schemas.CategoryWrite(name: self.categoryName, sortOrder: self.categoryOrder))
            let selected = self.selectedCategory
            if selected == nil && self.categoryRetry == nil { self.categoryRetry = UUID() }
            let response = try await self.requests.send(path: "/api/v1/ingredient-categories" + (selected.map { "/" + $0.id } ?? ""), method: selected == nil ? "POST" : "PUT", body: body, etag: selected == nil ? nil : self.categoryETag, retryKey: selected == nil ? self.categoryRetry : nil)
            let category = try APIJSON.decoder().decode(Components.Schemas.CategoryRead.self, from: response.data)
            self.categories.removeAll { $0.id == category.id }; self.categories.append(category)
            self.selectedCategory = category; self.categoryETag = response.etag; self.categoryRetry = nil; self.revertCategory(); self.notice = "Category saved."
        }
    }
    func deleteCategory() async {
        guard let category = selectedCategory else { return }
        await perform {
            _ = try await self.requests.send(path: "/api/v1/ingredient-categories/" + category.id, method: "DELETE", etag: self.categoryETag)
            self.categories.removeAll { $0.id == category.id }; self.startCategory(); self.notice = "Category deleted. Its ingredients are now Uncategorized."
        }
    }
    private func accept(_ response: APIResponse) throws {
        let ingredient = try APIJSON.decoder().decode(Components.Schemas.IngredientRead.self, from: response.data)
        selected = ingredient; ingredientETag = response.etag; revert()
        if let index = items.firstIndex(where: { $0.id == ingredient.id }) { items[index] = ingredient } else { items.insert(ingredient, at: 0) }
    }
    private func perform(background: Bool = false, _ action: () async throws -> Void) async {
        guard !busy else { return }; busy = true; if !background { error = nil; notice = nil }
        defer { busy = false }
        do { try await action() } catch { self.error = apiErrorMessage(error) }
    }
}

struct CatalogueView: View {
    @Bindable var selection: ServerSelection
    @State private var model: CatalogueViewModel
    @State private var showingEditor = false
    init(selection: ServerSelection) { self.selection = selection; _model = State(initialValue: CatalogueViewModel(selection: selection)) }
    var body: some View {
        List {
            if let error = model.error { Text(error).foregroundStyle(.red).accessibilityIdentifier("catalogue-error") }
            if let notice = model.notice { Text(notice).accessibilityIdentifier("catalogue-notice") }
            if model.loading { ProgressView("Loading ingredients") }
            Section {
                Button("New ingredient", systemImage: "plus") { model.startNew(); showingEditor = true }
                NavigationLink("Manage shopping categories") { CategoryManagementView(model: model) }
            }
            Section("Your ingredients") {
                ForEach(model.items, id: \.id) { item in
                    Button { Task { await model.choose(item.id); if model.selected?.id == item.id { showingEditor = true } } } label: {
                        VStack(alignment: .leading) {
                            Text(item.canonicalName).foregroundStyle(.primary)
                            Text(([item.categoryName ?? "Uncategorized"] + item.aliases.map(\.name)).joined(separator: " · ")).font(.caption).foregroundStyle(.secondary)
                        }.frame(maxWidth: .infinity, alignment: .leading)
                    }.accessibilityIdentifier("ingredient-row-" + item.id)
                }
                if model.items.isEmpty && !model.loading { Text("No matching ingredients. Add a canonical name to get started.") }
                if model.nextCursor != nil { Button("Load more ingredients") { Task { await model.search(append: true) } } }
            }
        }
        .navigationTitle("Ingredients")
        .searchable(text: $model.query, prompt: "Names and aliases")
        .onSubmit(of: .search) { Task { await model.search() } }
        .onChange(of: model.query) { _, value in if value.isEmpty { Task { await model.search() } } }
        .task { await model.load() }
        .onHouseholdChange(domains: [.recipes, .settings]) { _ in
            if !showingEditor && !model.dirty && !model.categoryDirty { Task { await model.load(background: true) } }
        }
        .disabled(model.busy)
        .sheet(isPresented: $showingEditor, onDismiss: { Task { await model.load(background: true) } }) {
            NavigationStack { IngredientEditorView(model: model) }
        }
    }
}

private struct IngredientEditorView: View {
    @Bindable var model: CatalogueViewModel
    @Environment(\.dismiss) private var dismiss
    @State private var confirmDiscard = false
    @State private var confirmDelete = false
    @State private var confirmReload = false
    var body: some View {
        VStack(spacing: 0) {
            if let error = model.error { Text(error).foregroundStyle(.red).padding().accessibilityIdentifier("catalogue-error") }
            if let notice = model.notice { Text(notice).padding().accessibilityIdentifier("catalogue-notice") }
            Form {
                Section("Ingredient details") {
                    TextField("Canonical ingredient name", text: $model.name).autocorrectionDisabled().accessibilityIdentifier("ingredient-name")
                    Picker("Shopping category", selection: $model.categoryId) {
                        Text("Uncategorized").tag("")
                        ForEach(model.sortedCategories, id: \.id) { Text($0.name).tag($0.id) }
                    }
                    .accessibilityIdentifier("ingredient-category")
                    if model.selected != nil { Toggle("Keep previous name as alias", isOn: $model.keepPreviousName) }
                    Button("Save ingredient") { Task { await model.save() } }.disabled(model.name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                }
                if let selected = model.selected {
                    Section("Aliases") {
                        Text("Other names for the same ingredient. Keep genuinely different ingredients separate.").font(.footnote).foregroundStyle(.secondary)
                        if model.ingredientDirty { Text("Save or discard ingredient details before changing aliases.").font(.footnote) }
                        ForEach(selected.aliases, id: \.id) { alias in
                            HStack {
                                Text(alias.name); Spacer()
                                Button("Edit", systemImage: "pencil") { model.editAlias(alias) }.labelStyle(.iconOnly).frame(minWidth: 44, minHeight: 44).contentShape(Rectangle()).accessibilityLabel("Edit \(alias.name)").buttonStyle(.borderless)
                                Button("Remove", systemImage: "minus.circle") { Task { await model.removeAlias(alias) } }.labelStyle(.iconOnly).frame(minWidth: 44, minHeight: 44).contentShape(Rectangle()).accessibilityLabel("Remove \(alias.name)").buttonStyle(.borderless)
                            }.disabled(model.ingredientDirty)
                        }
                        TextField(model.editingAlias == nil ? "New alias" : "Edit alias", text: $model.aliasName).autocorrectionDisabled().disabled(model.ingredientDirty).accessibilityIdentifier("ingredient-alias")
                        Button("Save alias") { Task { await model.saveAlias() } }.disabled(model.ingredientDirty || model.aliasName.isEmpty)
                        if model.editingAlias != nil { Button("Cancel alias edit") { model.clearAlias() } }
                    }
                    Section {
                        Button("Reload ingredient and discard edits") { confirmReload = true }
                        Button("Delete ingredient", role: .destructive) { confirmDelete = true }
                    }
                }
            }.scrollDismissesKeyboard(.interactively)
        }
        .disabled(model.busy)
        .navigationTitle(model.selected == nil ? "New ingredient" : "Ingredient")
        .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Done") { if model.dirty { confirmDiscard = true } else { dismiss() } }.disabled(model.busy) } }
        .interactiveDismissDisabled(model.dirty || model.busy)
        .confirmationDialog("Discard unsaved ingredient changes?", isPresented: $confirmDiscard) { Button("Discard changes", role: .destructive) { model.revert(); dismiss() }; Button("Keep editing", role: .cancel) { } }
        .confirmationDialog("Delete this ingredient? Referenced ingredients cannot be deleted.", isPresented: $confirmDelete) { Button("Delete ingredient", role: .destructive) { Task { await model.delete(); if model.selected == nil { dismiss() } } }; Button("Cancel", role: .cancel) { } }
        .confirmationDialog("Reload and discard unsaved edits?", isPresented: $confirmReload) { Button("Reload", role: .destructive) { if let id = model.selected?.id { Task { await model.choose(id) } } }; Button("Cancel", role: .cancel) { } }
    }
}

private struct CategoryManagementView: View {
    @Bindable var model: CatalogueViewModel
    @Environment(\.dismiss) private var dismiss
    @State private var confirmDelete = false
    @State private var confirmDiscard = false
    var body: some View {
        Form {
            if let error = model.error { Text(error).foregroundStyle(.red).accessibilityIdentifier("catalogue-error") }
            if let notice = model.notice { Text(notice).accessibilityIdentifier("catalogue-notice") }
            Section("Shopping categories") {
                ForEach(model.sortedCategories, id: \.id) { category in Button("Edit \(category.name)") { Task { await model.chooseCategory(category.id) } }.disabled(model.categoryDirty) }
                Button("New category") { model.startCategory() }.disabled(model.categoryDirty)
                if model.categoryDirty { Button("Discard category edits") { model.revertCategory() } }
            }
            Section(model.selectedCategory == nil ? "New category" : "Category details") {
                TextField("Category name", text: $model.categoryName)
                TextField("Display order", value: $model.categoryOrder, format: .number).keyboardType(.numberPad)
                Button("Save category") { Task { await model.saveCategory() } }.disabled(model.categoryName.isEmpty || model.categoryOrder < 0)
                if model.selectedCategory != nil { Button("Delete category", role: .destructive) { confirmDelete = true } }
                Text("Deleting a category moves its ingredients to Uncategorized.").font(.footnote).foregroundStyle(.secondary)
            }
        }
        .disabled(model.busy)
        .navigationTitle("Categories")
        .navigationBarBackButtonHidden(model.categoryDirty)
        .toolbar { ToolbarItem(placement: .confirmationAction) { Button("Done") { if model.categoryDirty { confirmDiscard = true } else { dismiss() } }.disabled(model.busy) } }
        .confirmationDialog("Delete this category? Its ingredients are kept.", isPresented: $confirmDelete) { Button("Delete category", role: .destructive) { Task { await model.deleteCategory() } }; Button("Cancel", role: .cancel) { } }
        .confirmationDialog("Discard category edits?", isPresented: $confirmDiscard) { Button("Discard changes", role: .destructive) { model.revertCategory(); dismiss() }; Button("Keep editing", role: .cancel) { } }
    }
}
