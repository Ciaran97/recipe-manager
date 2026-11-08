import Foundation
import Testing
@testable import RecipeManager

@Suite(.serialized) @MainActor
struct CatalogueStateTests {
    private nonisolated static let id = "10000000-0000-0000-0000-000000000001"
    private nonisolated static let ingredient = "{\"id\":\"10000000-0000-0000-0000-000000000001\",\"canonicalName\":\"Red capsicum\",\"categoryId\":null,\"categoryName\":null,\"revision\":\"1\",\"aliases\":[{\"id\":\"20000000-0000-0000-0000-000000000001\",\"name\":\"Red pepper\"}]}"
    private func selection() async -> ServerSelection {
        let configuration = URLSessionConfiguration.ephemeral; configuration.protocolClasses = [StubURLProtocol.self]
        let selection = ServerSelection(credentials: MemoryCredentials(), persist: { _ in }, makeClient: { APIClient(session: $0, configuration: configuration) })
        await selection.requestChange("https://catalogue.example.test")
        await selection.signedIn(ClientUser(id: UUID(), householdId: UUID(), role: .member))
        return selection
    }
    private nonisolated static func response(_ request: URLRequest, _ json: String, status: Int = 200, etag: String? = nil) -> (HTTPURLResponse, Data) {
        (HTTPURLResponse(url: request.url!, statusCode: status, httpVersion: nil, headerFields: etag.map { ["ETag": $0] })!, Data(json.utf8))
    }
    @Test func catalogueLoadsCanonicalNamesAndAliasesWithoutClientBusinessRules() async {
        let selection = await selection()
        StubURLProtocol.set(host: "catalogue.example.test") { request in
            if request.url!.path == "/api/v1/ingredient-categories" { return Self.response(request, "{\"items\":[],\"nextCursor\":null}") }
            return Self.response(request, "{\"items\":[\(Self.ingredient)],\"nextCursor\":null}")
        }
        let model = CatalogueViewModel(selection: selection); await model.load()
        #expect(model.items.count == 1); #expect(model.items.first?.canonicalName == "Red capsicum")
        #expect(model.items.first?.aliases.first?.name == "Red pepper")
    }
    @Test func conflictPreservesCorrectionAndUsesOpaqueParentValidator() async {
        let selection = await selection()
        StubURLProtocol.set(host: "catalogue.example.test") { request in
            if request.url!.path == "/api/v1/auth/csrf" { return Self.response(request, "{\"token\":\"csrf\"}") }
            if request.httpMethod == "PUT" {
                #expect(request.value(forHTTPHeaderField: "If-Match") == "\"opaque-parent\"")
                return Self.response(request, "{\"code\":\"name_conflict\"}", status: 409)
            }
            return Self.response(request, Self.ingredient, etag: "\"opaque-parent\"")
        }
        let model = CatalogueViewModel(selection: selection); await model.choose(Self.id); model.name = "Beef mince"; await model.save()
        #expect(model.name == "Beef mince"); #expect(model.selected?.id == Self.id); #expect(model.error != nil)
    }
    @Test func aliasConflictAndReferencedDeletionRetainTheSelectedIngredient() async {
        let selection = await selection()
        StubURLProtocol.set(host: "catalogue.example.test") { request in
            if request.url!.path == "/api/v1/auth/csrf" { return Self.response(request, "{\"token\":\"csrf\"}") }
            if request.httpMethod == "PUT" {
                #expect(request.value(forHTTPHeaderField: "If-Match") == "\"opaque-parent\"")
                return Self.response(request, "{\"code\":\"name_conflict\"}", status: 409)
            }
            if request.httpMethod == "DELETE" { return Self.response(request, "{\"code\":\"ingredient_referenced\"}", status: 409) }
            return Self.response(request, Self.ingredient, etag: "\"opaque-parent\"")
        }
        let model = CatalogueViewModel(selection: selection); await model.choose(Self.id)
        if let alias = model.selected?.aliases.first { model.editAlias(alias) }
        model.aliasName = "Beef mince"; await model.saveAlias()
        #expect(model.aliasName == "Beef mince"); #expect(model.selected?.aliases.first?.name == "Red pepper")
        await model.delete(); #expect(model.selected?.id == Self.id); #expect(model.error?.contains("used") == true)
    }

}
