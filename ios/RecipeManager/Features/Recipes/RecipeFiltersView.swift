import SwiftUI
import Observation
import RecipeManagerAPI

@Observable @MainActor final class RecipeOrganiserOptions {
    var collections: [Components.Schemas.OrganiserRead] = []
    var tags: [Components.Schemas.OrganiserRead] = []
    var busy = false
    var error: String?
    private var creationRetries: [String: UUID] = [:]
    private let requests: ScopedAPI
    init(selection: ServerSelection) { requests = ScopedAPI(selection: selection) }
    func load() async {
        guard !busy else { return }; busy = true; error = nil; defer { busy = false }
        do { collections = try await all("collections"); tags = try await all("tags") }
        catch { self.error = apiErrorMessage(error) }
    }
    func create(_ kind: String, name: String) async -> String? {
        guard !busy, !name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return nil }
        busy = true; error = nil; defer { busy = false }
        do {
            let body = Components.Schemas.OrganiserWrite(name: name.trimmingCharacters(in: .whitespacesAndNewlines))
            let fingerprint = kind + "\0" + body.name
            let key = creationRetries[fingerprint] ?? UUID(); creationRetries[fingerprint] = key
            let response = try await requests.send(path: "/api/v1/" + kind, method: "POST", body: try APIJSON.encoder().encode(body), retryKey: key)
            let item = try APIJSON.decoder().decode(Components.Schemas.OrganiserRead.self, from: response.data)
            creationRetries.removeValue(forKey: fingerprint)
            if kind == "collections" { collections.append(item) } else { tags.append(item) }
            return item.id
        } catch { self.error = apiErrorMessage(error); return nil }
    }
    private func all(_ kind: String) async throws -> [Components.Schemas.OrganiserRead] {
        var result: [Components.Schemas.OrganiserRead] = [], cursor: String?
        repeat {
            let suffix = cursor.map { "&cursor=" + ($0.addingPercentEncoding(withAllowedCharacters: .alphanumerics) ?? "") } ?? ""
            let response = try await requests.send(path: "/api/v1/\(kind)?limit=100" + suffix)
            let page = try APIJSON.decoder().decode(Components.Schemas.OrganiserListPage.self, from: response.data)
            result += page.items
            if let next = page.nextCursor, next == cursor { throw SessionFailure.invalidResponse }; cursor = page.nextCursor
        } while cursor != nil
        return result
    }
}

struct RecipeFiltersView: View {
    @Binding var collectionIds: [String]
    @Binding var tagIds: [String]
    let allowCreate: Bool
    @State private var collectionName = ""
    @State private var tagName = ""
    @State private var model: RecipeOrganiserOptions
    @Environment(\.dismiss) private var dismiss
    init(selection: ServerSelection, collectionIds: Binding<[String]>, tagIds: Binding<[String]>, allowCreate: Bool = false) {
        self.allowCreate = allowCreate
        _collectionIds = collectionIds; _tagIds = tagIds; _model = State(initialValue: RecipeOrganiserOptions(selection: selection))
    }
    var body: some View {
        Form {
            if let error = model.error { Section { Text(error).foregroundStyle(.red) } }
            if model.busy { ProgressView("Loading collections and tags") }
            choices("Collections", values: model.collections, selected: $collectionIds)
            choices("Tags", values: model.tags, selected: $tagIds)
            if allowCreate {
                createSection("collection", kind: "collections", name: $collectionName, selected: $collectionIds)
                createSection("tag", kind: "tags", name: $tagName, selected: $tagIds)
            }
            Section { Button("Reload choices") { Task { await model.load() } }.disabled(model.busy) }
        }.navigationTitle("Collections and tags").navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .confirmationAction) { Button("Done") { dismiss() }.disabled(model.busy) } }
            .interactiveDismissDisabled(model.busy)
            .task { await model.load() }
    }
    private func createSection(_ label: String, kind: String, name: Binding<String>, selected: Binding<[String]>) -> some View {
        Section("New " + label) {
            TextField("New " + label + " name", text: name).disabled(model.busy)
            Button("Create " + label) {
                Task {
                    if let id = await model.create(kind, name: name.wrappedValue) {
                        if !selected.wrappedValue.contains(id) { selected.wrappedValue.append(id) }
                        name.wrappedValue = ""
                    }
                }
            }.disabled(model.busy || name.wrappedValue.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
        }
    }
    private func choices(_ title: String, values: [Components.Schemas.OrganiserRead], selected: Binding<[String]>) -> some View {
        Section(title) {
            ForEach(values, id: \.id) { item in
                Toggle(item.name, isOn: Binding(get: { selected.wrappedValue.contains(item.id) }, set: { checked in
                    if checked { if !selected.wrappedValue.contains(item.id) { selected.wrappedValue.append(item.id) } }
                    else { selected.wrappedValue.removeAll { $0 == item.id } }
                }))
            }
            if !model.busy {
                ForEach(selected.wrappedValue.filter { id in !values.contains { $0.id == id } }, id: \.self) { id in
                    Button("Remove unavailable selection", role: .destructive) { selected.wrappedValue.removeAll { $0 == id } }
                }
                if values.isEmpty { Text("No \(title.lowercased()) yet. Create them in the recipe library.").foregroundStyle(.secondary) }
            }
        }
    }
}
