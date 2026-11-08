import SwiftUI
import Observation
import RecipeManagerAPI

enum RecipeOrganiserKind: String, CaseIterable { case collections, tags }
@Observable @MainActor final class OrganisersModel {
    var kind: RecipeOrganiserKind = .collections
    var items: [Components.Schemas.OrganiserRead] = []
    var name = ""
    var editingId: String?
    var busy = false
    var error: String?
    var notice: String?
    var remoteChanged = false
    var cursor: String?
    var dirty: Bool { name != originalName }
    private var originalName = ""
    private var etag: String?
    private var retry = UUID()
    private var retryBody = ""
    private let requests: ScopedAPI
    init(selection: ServerSelection) { requests = ScopedAPI(selection: selection) }
    func reset() { name = ""; originalName = ""; editingId = nil; etag = nil; retryBody = "" }
    func changeKind(_ value: RecipeOrganiserKind) async { reset(); kind = value; items = []; cursor = nil; await load() }
    func load(append: Bool = false) async {
        await perform {
            self.remoteChanged = false
            let suffix = (append ? self.cursor : nil).map { "&cursor=" + ($0.addingPercentEncoding(withAllowedCharacters: .alphanumerics) ?? "") } ?? ""
            let result = try await self.requests.send(path: "/api/v1/\(self.kind.rawValue)?limit=100" + suffix)
            let page = try APIJSON.decoder().decode(Components.Schemas.OrganiserListPage.self, from: result.data)
            if append { let ids = Set(self.items.map(\.id)); self.items += page.items.filter { !ids.contains($0.id) } } else { self.items = page.items }
            self.cursor = page.nextCursor
        }
    }
    func edit(_ item: Components.Schemas.OrganiserRead) async {
        await perform {
            let result = try await self.requests.send(path: "/api/v1/\(self.kind.rawValue)/\(item.id)")
            let current = try APIJSON.decoder().decode(Components.Schemas.OrganiserRead.self, from: result.data)
            self.name = current.name; self.originalName = current.name; self.editingId = current.id; self.etag = result.etag
        }
    }
    func save() async {
        guard !busy else { return }; var saved = false
        await perform {
            let value = self.name.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !value.isEmpty, value.count <= 200 else { throw RecipeEditorFailure.invalid("Enter a name up to 200 characters.") }
            let body = Components.Schemas.OrganiserWrite(name: value)
            let fingerprint = self.kind.rawValue + "\0" + value
            if fingerprint != self.retryBody { self.retryBody = fingerprint; self.retry = UUID() }
            let path = "/api/v1/" + self.kind.rawValue + (self.editingId.map { "/" + $0 } ?? "")
            _ = try await self.requests.send(path: path, method: self.editingId == nil ? "POST" : "PUT", body: try APIJSON.encoder().encode(body), etag: self.etag, retryKey: self.editingId == nil ? self.retry : nil)
            self.reset(); self.notice = "Saved."; saved = true
        }
        if saved { await load() }
    }
    func remove(_ item: Components.Schemas.OrganiserRead) async {
        guard !busy else { return }; var removed = false
        await perform {
            let path = "/api/v1/\(self.kind.rawValue)/\(item.id)"
            let response = try await self.requests.send(path: path)
            let current = try APIJSON.decoder().decode(Components.Schemas.OrganiserRead.self, from: response.data)
            guard current.revision == item.revision, current.name == item.name else { throw RecipeEditorFailure.invalid("This organiser changed. Reload before deleting it.") }
            _ = try await self.requests.send(path: path, method: "DELETE", etag: response.etag)
            if self.editingId == item.id { self.reset() }; self.notice = "Deleted. Recipes were kept."; removed = true
        }
        if removed { await load() }
    }
    private func perform(_ action: () async throws -> Void) async {
        guard !busy else { return }; busy = true; error = nil; defer { busy = false }
        do { try await action() }
        catch RecipeEditorFailure.invalid(let message) { error = message }
        catch { self.error = apiErrorMessage(error) }
    }
}

struct OrganisersView: View {
    @State private var model: OrganisersModel
    @State private var deletion: Components.Schemas.OrganiserRead?
    @State private var pendingKind: RecipeOrganiserKind?
    @State private var pendingEdit: Components.Schemas.OrganiserRead?
    @State private var discard = false
    @State private var leaving = false
    @Environment(\.dismiss) private var dismiss
    init(selection: ServerSelection) { _model = State(initialValue: OrganisersModel(selection: selection)) }
    var body: some View {
        Form {
            Section {
                Picker("Organiser type", selection: Binding(get: { model.kind }, set: { value in
                    if model.dirty { pendingKind = value; discard = true } else { Task { await model.changeKind(value) } }
                })) { Text("Collections").tag(RecipeOrganiserKind.collections); Text("Tags").tag(RecipeOrganiserKind.tags) }.pickerStyle(.segmented)
                Text("Shared with your household. Removing one keeps its recipes.").font(.footnote)
                if let error = model.error { Text(error).foregroundStyle(.red).accessibilityIdentifier("organiser-error") }
                if model.remoteChanged { Text("Collections or tags changed on another device. Reload the list; your unsaved name is retained.").foregroundStyle(.secondary) }
                if let notice = model.notice { Text(notice).accessibilityIdentifier("organiser-notice") }
            }
            Section(model.editingId == nil ? "Create" : "Rename") {
                TextField("Name", text: $model.name).accessibilityIdentifier("organiser-name")
                Button(model.editingId == nil ? "Create" : "Save name") { Task { await model.save() } }.disabled(model.name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty).accessibilityIdentifier("organiser-save")
                Button("Cancel edit") { if model.dirty { discard = true } else { model.reset() } }
            }
            Section(model.kind == .collections ? "Collections" : "Tags") {
                ForEach(model.items, id: \.id) { item in
                    HStack {
                        Text(item.name)
                        Spacer()
                        Button("Rename") { if model.dirty { pendingEdit = item; discard = true } else { Task { await model.edit(item) } } }.accessibilityLabel("Rename " + item.name)
                        Button("Delete", role: .destructive) { deletion = item }.accessibilityLabel("Delete " + item.name)
                    }.buttonStyle(.borderless)
                }
                if model.cursor != nil { Button("Load more") { Task { await model.load(append: true) } } }
                Button("Reload list") { Task { await model.load() } }
                if model.busy { ProgressView("Working") }
            }
        }.disabled(model.busy).navigationTitle("Collections and tags").navigationBarTitleDisplayMode(.inline)
            .navigationBarBackButtonHidden(model.dirty)
            .toolbar { if model.dirty { ToolbarItem(placement: .topBarLeading) { Button("Back") { leaving = true; discard = true } } } }
            .onHouseholdChange(domains: [.recipes]) { _ in model.remoteChanged = true }
            .task { await model.load() }.refreshable { await model.load() }
            .confirmationDialog("Delete \(deletion?.name ?? "organiser")? Recipes will be kept.", isPresented: Binding(get: { deletion != nil }, set: { if !$0 { deletion = nil } })) {
                Button("Delete organiser", role: .destructive) { if let item = deletion { Task { await model.remove(item); deletion = nil } } }
                Button("Cancel", role: .cancel) { deletion = nil }
            }
            .confirmationDialog("Discard unsaved organiser name?", isPresented: $discard) {
                Button("Discard changes", role: .destructive) {
                    let kind = pendingKind, item = pendingEdit; pendingKind = nil; pendingEdit = nil; model.reset()
                    Task { if leaving { leaving = false; dismiss() } else if let kind { await model.changeKind(kind) } else if let item { await model.edit(item) } }
                }
                Button("Keep editing", role: .cancel) { pendingKind = nil; pendingEdit = nil; leaving = false }
            }
    }
}
