import SwiftUI
import Observation
import RecipeManagerAPI

@Observable @MainActor final class RecipeLibraryModel {
    var query = ""
    var archived = "exclude"
    var favouriteOnly = false
    var collectionIds: [String] = []
    var tagIds: [String] = []
    var items: [Components.Schemas.RecipeRead] = []
    var cursor: String?
    var error: String?
    var loading = false
    private let requests: ScopedAPI
    private var generation = 0
    private var pending: Task<APIResponse, Error>?
    private var acceptedSelection = ""
    init(selection: ServerSelection) { requests = ScopedAPI(selection: selection) }
    func load(append: Bool = false) async {
        pending?.cancel(); generation += 1; let current = generation
        loading = true; error = nil
        defer { if current == generation { loading = false } }
        do {
            let escaped = query.addingPercentEncoding(withAllowedCharacters: .alphanumerics) ?? ""
            let selection = ([query, archived, String(favouriteOnly)] + collectionIds.sorted() + ["tags"] + tagIds.sorted()).joined(separator: "\0")
            let useAppend = append && selection == acceptedSelection
            let next = useAppend ? cursor : nil
            let organisers = collectionIds.sorted().map { "&collectionIds=" + ($0.addingPercentEncoding(withAllowedCharacters: .alphanumerics) ?? "") }.joined() + tagIds.sorted().map { "&tagIds=" + ($0.addingPercentEncoding(withAllowedCharacters: .alphanumerics) ?? "") }.joined()
            let path = "/api/v1/recipes?limit=25&archived=\(archived)&favouriteOnly=\(favouriteOnly)&q=\(escaped)" + organisers + (next.map { "&cursor=" + ($0.addingPercentEncoding(withAllowedCharacters: .alphanumerics) ?? "") } ?? "")
            let task = Task { try await self.requests.send(path: path) }; pending = task
            let response = try await task.value
            let page = try APIJSON.decoder().decode(Components.Schemas.RecipeListPage.self, from: response.data)
            guard current == generation else { return }
            if useAppend { let ids = Set(items.map(\.id)); items += page.items.filter { !ids.contains($0.id) } } else { items = page.items }
            cursor = page.nextCursor; acceptedSelection = selection
        } catch { if current == generation { self.error = apiErrorMessage(error) } }
    }
    func cancel() { generation += 1; pending?.cancel(); pending = nil; loading = false }
}

@Observable @MainActor final class RecipeDetailModel {
    var recipe: Components.Schemas.RecipeRead?
    var preview: Components.Schemas.RecipeServingPreview?
    var servings = ""
    var error: String?
    var busy = false
    var deleted = false
    var duplicateId: String?
    private(set) var etag: String?
    private let requests: ScopedAPI
    private let id: String
    private var retry: UUID?
    init(selection: ServerSelection, id: String) { requests = ScopedAPI(selection: selection); self.id = id }
    func load(preserveServings: Bool = false) async {
        await perform {
            let response = try await self.requests.send(path: "/api/v1/recipes/" + self.id)
            try self.accept(response); if !preserveServings { self.servings = self.recipe?.defaultServings ?? "" }; self.preview = nil
        }
    }
    func favourite() async { guard let recipe else { return }; await update("favourite", body: Components.Schemas.FavouriteRecipeWrite(favourite: !recipe.favourite)) }
    func archive() async { guard let recipe else { return }; await update("archive", body: Components.Schemas.ArchiveRecipeWrite(archived: !recipe.archived)) }
    private func update<T: Encodable>(_ action: String, body: T) async {
        await perform { try self.accept(try await self.requests.send(path: "/api/v1/recipes/\(self.id)/\(action)", method: "PUT", body: try APIJSON.encoder().encode(body), etag: self.etag)) }
    }
    func duplicate() async {
        await perform {
            if self.retry == nil { self.retry = UUID() }
            let response = try await self.requests.send(path: "/api/v1/recipes/\(self.id)/duplicate", method: "POST", body: Data("{}".utf8), etag: self.etag, retryKey: self.retry)
            self.duplicateId = try APIJSON.decoder().decode(Components.Schemas.RecipeRead.self, from: response.data).id; self.retry = nil
        }
    }
    func remove() async {
        await perform { _ = try await self.requests.send(path: "/api/v1/recipes/" + self.id, method: "DELETE", etag: self.etag); self.deleted = true }
    }
    func scale() async {
        await perform {
            let response = try await self.requests.send(path: "/api/v1/recipes/\(self.id)/serving-preview", method: "POST", body: try APIJSON.encoder().encode(Components.Schemas.ServingPreviewWrite(targetServings: self.servings)))
            self.preview = try APIJSON.decoder().decode(Components.Schemas.RecipeServingPreview.self, from: response.data)
        }
    }
    func quantity(_ line: Components.Schemas.RecipeIngredientRead) -> String {
        if let scaled = preview?.ingredients.first(where: { $0.id == line.id }) { return scaled.displayText.isEmpty ? "Quantity not specified" : scaled.displayText }
        if line.kind == .unknown { return line.quantityText.isEmpty ? "Quantity not specified" : line.quantityText }
        return line.quantityText + " " + line.unit.displayName
    }
    private func accept(_ response: APIResponse) throws { recipe = try APIJSON.decoder().decode(Components.Schemas.RecipeRead.self, from: response.data); etag = response.etag }
    private func perform(_ action: () async throws -> Void) async {
        guard !busy else { return }; busy = true; error = nil; defer { busy = false }
        do { try await action() } catch { self.error = apiErrorMessage(error) }
    }
}

struct RecipeLibraryView: View {
    let selection: ServerSelection
    @State private var model: RecipeLibraryModel
    @State private var creating = false
    @State private var activeImport: RecipeImportRoute?
    @State private var showingCaptures = false
    @State private var showingFilters = false
    @State private var inbox: PendingCaptureInboxModel
    @Environment(\.scenePhase) private var phase
    init(selection: ServerSelection) { self.selection = selection; _model = State(initialValue: RecipeLibraryModel(selection: selection)); _inbox = State(initialValue: PendingCaptureInboxModel(selection: selection)) }
    var body: some View {
        List {
            if !inbox.entries.isEmpty { Section { Button("Shared recipe captures (\(inbox.entries.count))", systemImage: "tray") { showingCaptures = true }.accessibilityIdentifier("recipe-pending-captures") } }
            if let error = model.error { Text(error).foregroundStyle(.red).accessibilityIdentifier("recipe-library-error") }
            Section {
                NavigationLink("Collections and tags") { OrganisersView(selection: selection) }
                Button("Filter by collections and tags") { showingFilters = true }
                Picker("Archived recipes", selection: $model.archived) { Text("Active").tag("exclude"); Text("All").tag("include"); Text("Archived only").tag("only") }
                Toggle("Favourites only", isOn: $model.favouriteOnly)
                Button { Task { await model.load() } } label: { Text("Apply filters").font(.body).fixedSize(horizontal: false, vertical: true) }.disabled(model.loading)
            } header: { Text("Filter recipes").foregroundStyle(Color(uiColor: .label)) }
            ForEach(model.items, id: \.id) { recipe in
                NavigationLink { RecipeDetailView(selection: selection, id: recipe.id) } label: {
                    HStack {
                        if let image = recipe.imageAssetId { PrivateRecipeImage(selection: selection, id: image, title: recipe.title).frame(width: 64, height: 64).clipped() }
                        VStack(alignment: .leading) {
                            Text(recipe.title).font(.headline).fixedSize(horizontal: false, vertical: true)
                            if let description = recipe.description { Text(description).lineLimit(2).foregroundStyle(Color(uiColor: .label).opacity(0.8)).fixedSize(horizontal: false, vertical: true) }
                            if recipe.archived { Text("Archived").font(.caption) }
                            if recipe.favourite { Label("Favourite", systemImage: "heart.fill").font(.caption) }
                        }
                    }
                }.accessibilityIdentifier("recipe-library-" + recipe.id)
            }
            if model.items.isEmpty && !model.loading { ContentUnavailableView("No recipes found", systemImage: "book", description: Text("Create a recipe or change your search.")) }
            if model.cursor != nil { Button("Load more recipes") { Task { await model.load(append: true) } }.disabled(model.loading) }
            if model.loading { ProgressView("Loading recipes") }
        }
        .navigationTitle("Recipes").searchable(text: $model.query, prompt: "Recipe or ingredient")
        .onSubmit(of: .search) { Task { await model.load() } }
        .refreshable { await model.load(); await inbox.load() }.task { await model.load(); await inbox.load() }
        .onHouseholdChange(domains: [.recipes], personal: true) { _ in Task { await model.load() } }
        .toolbar {
            ToolbarItem(placement: .primaryAction) { Button("New recipe", systemImage: "plus") { creating = true } }
            ToolbarItem(placement: .topBarLeading) {
                Menu("Import recipes", systemImage: "square.and.arrow.down") {
                    Button("Import URL or text") { activeImport = RecipeImportRoute() }.accessibilityIdentifier("recipe-import-new")
                    Button("Shared recipe captures") { showingCaptures = true }.accessibilityIdentifier("recipe-import-captures")
                }.accessibilityIdentifier("recipe-import-menu")
            }
        }
        .onDisappear { model.cancel() }
        .sheet(isPresented: $showingFilters, onDismiss: { Task { await model.load() } }) {
            NavigationStack { RecipeFiltersView(selection: selection, collectionIds: $model.collectionIds, tagIds: $model.tagIds) }
        }
        .onChange(of: phase) { _, value in if value == .active { Task { await inbox.load() } } }
        .navigationDestination(isPresented: $showingCaptures) {
            PendingImportInbox(selection: selection, model: inbox) { intent in activeImport = RecipeImportRoute(intent: intent) }
        }
        .sheet(item: $activeImport, onDismiss: { Task { await model.load(); await inbox.load() } }) { route in
            NavigationStack { ImportReviewView(selection: selection, intent: route.intent) { _ in activeImport = nil } }
        }
        .sheet(isPresented: $creating, onDismiss: { Task { await model.load() } }) { NavigationStack { RecipeEditor(selection: selection) { _ in creating = false } } }
    }
}

struct RecipeDetailView: View {
    let selection: ServerSelection
    @State private var model: RecipeDetailModel
    @State private var editing = false
    @State private var deleting = false
    @AccessibilityFocusState private var errorFocused: Bool
    @Environment(\.dismiss) private var dismiss
    init(selection: ServerSelection, id: String) { self.selection = selection; _model = State(initialValue: RecipeDetailModel(selection: selection, id: id)) }
    var body: some View {
        ScrollViewReader { proxy in
            List {
                if let error = model.error { Text(error).foregroundStyle(.red).accessibilityIdentifier("recipe-detail-error").id("recipe-detail-error").accessibilityFocused($errorFocused) }
                if let recipe = model.recipe {
                    Section {
                        if let image = recipe.imageAssetId { PrivateRecipeImage(selection: selection, id: image, title: recipe.title).frame(maxHeight: 280) }
                        if let description = recipe.description { Text(description) }
                        Text("Default servings: " + recipe.defaultServings)
                        Text(recipe.measurementStandard == .AU ? "Australian measures" : "US measures")
                        if let minutes = recipe.prepMinutes { Text("Preparation: \(minutes) minutes") }
                        if let minutes = recipe.cookMinutes { Text("Cooking: \(minutes) minutes") }
                        if recipe.archived { Text("Archived") }
                    }
                    Section {
                        TextField("Servings", text: $model.servings).keyboardType(.decimalPad).accessibilityLabel("Preview servings")
                        Button("Scale ingredients") { Task { await model.scale() } }.disabled(model.busy)
                        if model.preview != nil { Text("Preview only. Original recipe unchanged.").font(.caption) }
                    } header: { Text("Serving preview").foregroundStyle(Color(uiColor: .label)) }
                    ingredientSection("Ingredients", lines: recipe.ingredients.filter { $0.groupId == nil })
                    ForEach(recipe.groups, id: \.id) { group in ingredientSection(group.title, lines: recipe.ingredients.filter { $0.groupId == group.id }) }
                    Section { ForEach(Array(recipe.instructions.enumerated()), id: \.element.id) { index, step in Text("\(index + 1). " + step.text) } } header: { Text("Instructions").foregroundStyle(Color(uiColor: .label)) }
                    if let notes = recipe.notes { Section { Text(notes) } header: { Text("Notes").foregroundStyle(Color(uiColor: .label)) } }
                    if let source = recipe.sourceUrl, let url = URL(string: source) { Section { Link("Original recipe", destination: url) } }
                    Section {
                        Button(recipe.favourite ? "Remove favourite" : "Favourite recipe", systemImage: recipe.favourite ? "heart.fill" : "heart") { Task { await model.favourite() } }
                        NavigationLink("Add ingredients to shopping list") {
                            ShoppingListView(selection: selection, incoming: ShoppingSourceDraft(kind: "recipes", entries: [.init(recipeId: recipe.id, title: recipe.title, servings: model.preview?.targetServings ?? recipe.defaultServings)]))
                        }
                        Button("Edit recipe", systemImage: "pencil") { editing = true }
                        Button("Duplicate recipe", systemImage: "doc.on.doc") { Task { await model.duplicate() } }
                        Button(recipe.archived ? "Restore recipe" : "Archive recipe", systemImage: "archivebox") { Task { await model.archive() } }
                        Button("Delete recipe", role: .destructive) { deleting = true }
                    } header: { Text("Recipe actions").foregroundStyle(Color(uiColor: .label)) }.disabled(model.busy)
                } else if model.busy { ProgressView("Loading recipe") }
            }
            .onChange(of: model.error) { _, error in
                guard error != nil else { return }
                proxy.scrollTo("recipe-detail-error", anchor: .top)
                errorFocused = true
            }
            .navigationTitle(model.recipe?.title ?? "Recipe").task { await model.load() }
            .onHouseholdChange(domains: [.recipes], personal: true) { _ in Task { await model.load(preserveServings: true) } }
            .refreshable { await model.load() }
            .onChange(of: model.deleted) { _, deleted in if deleted { dismiss() } }
            .navigationDestination(item: $model.duplicateId) { RecipeDetailView(selection: selection, id: $0) }
            .sheet(isPresented: $editing) { if let recipe = model.recipe { NavigationStack { RecipeEditor(selection: selection, recipeId: recipe.id) { _ in editing = false; Task { await model.load() } } } } }
            .confirmationDialog("Delete this recipe permanently?", isPresented: $deleting) { Button("Delete recipe", role: .destructive) { Task { await model.remove() } } } message: { Text("Referenced recipes can be archived instead.") }
        }
    }
    private func ingredientSection(_ title: String, lines: [Components.Schemas.RecipeIngredientRead]) -> some View {
        Section(title) { ForEach(lines, id: \.id) { line in
            VStack(alignment: .leading) {
                Text(model.quantity(line) + " " + line.ingredientName)
                if let scaled = model.preview?.ingredients.first(where: { $0.id == line.id }) {
                    if scaled.isApproximate { Text("Approximately; rounded for display").font(.caption).foregroundStyle(Color(uiColor: .label).opacity(0.8)) }
                    if let exact = scaled.exact {
                        DisclosureGroup("Exact quantity for " + line.ingredientName) {
                            Text(exact.numerator + "/" + exact.denominator + " " + line.unit.displayName).textSelection(.enabled)
                        }
                    }
                }
                if line.kind == .unknown || line.uncertain { Text("Quantity needs review").font(.caption).foregroundStyle(Color(uiColor: .label).opacity(0.8)) }
                if let preparation = line.preparation { Text(preparation).font(.caption) }
            }
        } }
    }
}

struct PrivateRecipeImage: View {
    let selection: ServerSelection
    let id: String
    let title: String
    @State private var image: UIImage?
    @State private var failed = false
    var body: some View {
        ZStack {
            if let image { Image(uiImage: image).resizable().scaledToFit().accessibilityLabel(title).accessibilityIdentifier("recipe-image-" + id) }
            else if failed { Label("Photo unavailable", systemImage: "photo").font(.caption) }
            else { ProgressView("Loading photo") }
        }.task(id: id) {
            let requests = ScopedAPI(selection: selection)
            image = nil; failed = false
            do {
                let response = try await requests.send(path: "/api/v1/images/" + id)
                try Task.checkCancellation()
                guard let decoded = UIImage(data: response.data) else { failed = true; return }
                image = decoded
            } catch is CancellationError { } catch { failed = true }
        }
    }
}
