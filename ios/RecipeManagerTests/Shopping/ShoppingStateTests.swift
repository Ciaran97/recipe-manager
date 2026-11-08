import Foundation
import Testing
import RecipeManagerAPI
@testable import RecipeManager

struct ShoppingStateTests {
    @Test func informalMeasuresAndCombinedSourcesRoundTrip() throws {
        var quantity = ShoppingQuantityDraft(); quantity.kind = .numeric; quantity.unit = .handful; quantity.text = "1/2"
        #expect(quantity.write().unit == .handful); #expect(quantity.write().quantityText == "1/2")
        let source = Components.Schemas.ShoppingSelection(kind: .combined, entries: [], ranges: [.init(from: "2026-11-02", to: "2026-11-08")])
        let decoded = try APIJSON.decoder().decode(Components.Schemas.ShoppingSelection.self, from: APIJSON.encoder().encode(source))
        #expect(decoded.kind == .combined); #expect(decoded.ranges?.first?.from == "2026-11-02")
    }
    @Test func selectedRecipeServingsRemainUnroundedAndGenerationUsesPlans() throws {
        var draft = ShoppingSourceDraft()
        draft.kind = "recipes"; draft.entries = [.init(recipeId: UUID().uuidString, title: "Rice", servings: "2.125")]
        let selection = draft.selection()
        #expect(selection.entries?.first?.servings == "2.125")
        #expect(draft.generationBasis == .planned)
        draft.basis = .actual
        #expect(draft.generationBasis == .planned)
    }
    @Test func unknownAmountsRemainTextWithoutAnInventedQuantity() throws {
        var draft = ShoppingQuantityDraft(); draft.text = "to taste"; draft.kind = .unknown
        let json = try #require(JSONSerialization.jsonObject(with: APIJSON.encoder().encode(draft.write())) as? [String: Any])
        #expect(json["quantityText"] as? String == "to taste")
        #expect(json["kind"] as? String == "unknown"); #expect(json["exact"] == nil)
    }
    @Test func directQuantityInputIsPreserved() {
        var draft = ShoppingQuantityDraft(); draft.kind = .numeric; draft.text = "1/3"; draft.unit = .cup; draft.standard = .US
        #expect(draft.write().quantityText == "1/3"); #expect(draft.write().measurementStandard == .US)
    }
}

@MainActor struct ShoppingPresentationTests {
    @Test func staleNotificationKeepsManualDraftAndDoesNotMarkAnythingPurchased() {
        let model = ShoppingListModel(selection: ServerSelection(credentials: MemoryCredentials(), persist: { _ in }))
        model.editor = .manual; model.label = "Bread"; model.quantity.text = "1.25"; model.quantity.kind = .numeric
        model.remoteInvalidation()
        #expect(model.label == "Bread"); #expect(model.quantity.text == "1.25")
        #expect(model.outdated); #expect(model.dirty)
    }
}

@Suite(.serialized) @MainActor struct ShoppingRequestTests {
    @Test func staleManualSaveRetainsDraftAndUsesOpaqueListValidator() async throws {
        let configuration = URLSessionConfiguration.ephemeral; configuration.protocolClasses = [StubURLProtocol.self]
        let selection = ServerSelection(credentials: MemoryCredentials(), persist: { _ in }, makeClient: { APIClient(session: $0, configuration: configuration) })
        await selection.requestChange("https://shopping.example.test")
        await selection.signedIn(ClientUser(id: UUID(), householdId: UUID(), role: .member))
        let id = UUID().uuidString
        let value = """
        {"id":"\(id)","revision":"1","name":"Shopping","selection":null,"basis":"planned","policyVersion":1,
        "householdSourceRevision":"2","sourceRevisions":[],"items":[],
        "summary":{"knownItemCount":0,"unresolvedCount":0,"purchasedCount":0,"removedCount":0,"knownTotals":[]}}
        """
        StubURLProtocol.set(host: "shopping.example.test") { request in
            var status = 200, body = value
            if request.url?.path == "/api/v1/auth/csrf" { body = "{\"token\":\"csrf-fixture\"}" }
            else if request.httpMethod == "POST" {
                #expect(request.value(forHTTPHeaderField: "If-Match") == "\"opaque-shopping-version\"")
                #expect(request.value(forHTTPHeaderField: "Idempotency-Key") != nil)
                status = 412; body = "{\"code\":\"stale_revision\"}"
            }
            return (HTTPURLResponse(url: request.url!, statusCode: status, httpVersion: nil, headerFields: ["ETag": "\"opaque-shopping-version\""])!, Data(body.utf8))
        }
        let model = ShoppingListModel(selection: selection)
        await model.open(id); model.begin(.manual); model.label = "Bread"; model.quantity.text = "2.125"; model.quantity.kind = .numeric
        await model.saveManual()
        #expect(model.outdated); #expect(model.dirty); #expect(model.label == "Bread"); #expect(model.quantity.text == "2.125")
        #expect(model.etag == "\"opaque-shopping-version\""); #expect(model.editor == .manual)
    }
}

@Suite(.serialized) @MainActor struct ShoppingSyncTests {
    @Test func remoteReadKeepsDirtyInputOriginalValidatorAndAcceptedNotice() async throws {
        let configuration = URLSessionConfiguration.ephemeral; configuration.protocolClasses = [StubURLProtocol.self]
        let selection = ServerSelection(credentials: MemoryCredentials(), persist: { _ in }, makeClient: { APIClient(session: $0, configuration: configuration) })
        await selection.requestChange("https://shopping-sync.example.test")
        await selection.signedIn(ClientUser(id: UUID(), householdId: UUID(), role: .member))
        let id = UUID().uuidString
        StubURLProtocol.set(host: "shopping-sync.example.test") { request in
            var body: String, tag = "\"original-list\""
            switch request.url?.path {
            case "/api/v1/household/settings":
                body = """
                {"id":"00000000-0000-0000-0000-000000000001","name":"Home","timezone":"UTC","revision":"1","ai":{"enabled":false,"provider":"OpenAICompatible","model":"","baseUrl":"","hasApiKey":false}}
                """
            case "/api/v1/ingredient-categories", "/api/v1/shopping-lists": body = "{\"items\":[],\"nextCursor\":null}"
            default:
                let conditional = request.value(forHTTPHeaderField: "If-None-Match") != nil
                if conditional { #expect(request.value(forHTTPHeaderField: "If-None-Match") == "\"original-list\""); tag = "\"new-list\"" }
                body = """
                {"id":"\(id)","revision":"\(conditional ? "2" : "1")","name":"Shopping","selection":null,"basis":"planned","policyVersion":1,"householdSourceRevision":"2","sourceRevisions":[],"items":[],"summary":{"knownItemCount":0,"unresolvedCount":0,"purchasedCount":0,"removedCount":0,"knownTotals":[]}}
                """
            }
            return (HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: nil, headerFields: ["ETag": tag])!, Data(body.utf8))
        }
        let model = ShoppingListModel(selection: selection)
        await model.initialize(); await model.open(id); model.begin(.manual)
        model.label = "Keep bread"; model.quantity.text = "1.25"; model.notice = "Previous action accepted."
        await model.synchronize()
        #expect(model.error == nil); #expect(model.outdated); #expect(model.dirty)
        #expect(model.label == "Keep bread"); #expect(model.quantity.text == "1.25")
        #expect(model.current?.revision == "1"); #expect(model.etag == "\"original-list\"")
        #expect(model.notice == "Previous action accepted.")
    }
}
