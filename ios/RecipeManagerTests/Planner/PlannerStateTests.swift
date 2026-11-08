import Foundation
import Testing
import RecipeManagerAPI
@testable import RecipeManager

struct PlannerStateTests {
    @Test func householdDateUsesItsTimezoneAndWeeksStartOnMonday() throws {
        let instant = try #require(ISO8601DateFormatter().date(from: "2026-11-01T17:00:00Z"))
        #expect(PlannerDates.today(timezone: "Australia/Perth", now: instant) == "2026-11-02")
        #expect(PlannerDates.monday("2026-11-08") == "2026-11-02")
        #expect(PlannerDates.add("2024-02-28", days: 1) == "2024-02-29")
        #expect(PlannerDates.add("2026-12-31", days: 1) == "2027-01-01")
    }
    @Test func schedulingKeepsEnteredPortionsSeparateAndDoesNotConfirmActuals() throws {
        var draft = MealDraft(date: "2026-11-02", slotId: UUID().uuidString)
        draft.recipeId = UUID().uuidString; draft.prepared = "4"; draft.eaten = "1.5"
        let body = try APIJSON.encoder().encode(draft.cookingWrite())
        let object = try #require(JSONSerialization.jsonObject(with: body) as? [String: Any])
        #expect(object["plannedPrepared"] as? String == "4")
        #expect(object["plannedEaten"] as? String == "1.5")
        #expect(object["actualPrepared"] == nil)
        #expect(object["allocations"] == nil)
    }
    @Test func directDecimalInputIsNotRoundedByPresentation() {
        var draft = MealDraft(date: "2026-11-02", slotId: UUID().uuidString)
        draft.prepared = "2.125"; draft.eaten = "0.1"
        #expect(draft.cookingWrite().plannedPrepared == "2.125")
        #expect(draft.cookingWrite().plannedEaten == "0.1")
    }
    private func allocation() -> Components.Schemas.AllocationReservationRead {
        .init(mealId: UUID().uuidString, mealRevision: "7", date: "2026-11-03", slotId: UUID().uuidString,
              allocation: .init(id: UUID().uuidString, sourceCookingEventId: UUID().uuidString, sourceRevision: "8", recipeTitle: "Rice", sourceDate: "2026-11-02", plannedPortions: "2", consumedPortions: "1.5", reservedRemainder: "0.5", commitment: "2"))
    }
    @Test func releasingReservationDoesNotResubmitHistoricalPlanOrConsumedPortions() throws {
        var row = ReservationResolution(allocation()); row.action = "release"
        let write = try #require(row.write())
        #expect(write.reservedRemainder == "0")
        #expect(write.plannedPortions == nil); #expect(write.consumedPortions == nil)
        #expect(write.clearConsumption != true); #expect(write.mealRevision == "7")
    }
    @Test func correctingReservationKeepsConsumptionUnlessExplicitlyChanged() throws {
        var row = ReservationResolution(allocation()); row.action = "edit"; row.reserved = "0.25"
        let write = try #require(row.write())
        #expect(write.plannedPortions == "2"); #expect(write.reservedRemainder == "0.25")
        #expect(write.consumedPortions == nil); #expect(write.clearConsumption != true)
        row.consumption = "clear"
        #expect(row.write()?.clearConsumption == true)
        #expect(row.write()?.reservedRemainder == "0.25")
    }
}

@MainActor struct PlannerPresentationTests {
    @Test func positionOnlyActionsDoNotDiscardUnsavedContentChanges() async {
        let model = MealEditorModel(selection: ServerSelection(credentials: MemoryCredentials(), persist: { _ in }), meal: nil, type: .other, date: "2026-11-02", slotId: UUID().uuidString)
        model.draft.label = "Edited meal description"
        await model.submit("duplicate")
        #expect(model.error?.contains("Save meal content changes") == true)
        #expect(!model.saved); #expect(model.draft.label == "Edited meal description")
    }
    @Test func anUnsavedSourceChangeCannotRecordConsumption() async {
        let selection = ServerSelection(credentials: MemoryCredentials(), persist: { _ in })
        let model = LeftoversModel(selection: selection, meal: nil, date: "2026-11-03", slotId: UUID().uuidString)
        model.sourceId = UUID().uuidString
        await model.consumption()
        #expect(model.error?.contains("Save or discard") == true)
        #expect(!model.saved)
    }
    @Test func directConsumptionInputIsProtectedFromSheetDismissal() {
        let model = LeftoversModel(selection: ServerSelection(credentials: MemoryCredentials(), persist: { _ in }), meal: nil, date: "2026-11-03", slotId: UUID().uuidString)
        #expect(!model.dirty)
        model.consumed = "1.5"
        #expect(model.dirty)
    }
}

@Suite(.serialized) @MainActor struct PlannerRequestTests {
    @Test func mealSlotWritesUseServerValidatorsRatherThanConstructingThem() async throws {
        let configuration = URLSessionConfiguration.ephemeral; configuration.protocolClasses = [StubURLProtocol.self]
        let selection = ServerSelection(credentials: MemoryCredentials(), persist: { _ in }, makeClient: { APIClient(session: $0, configuration: configuration) })
        await selection.requestChange("https://planner.example.test")
        await selection.signedIn(ClientUser(id: UUID(), householdId: UUID(), role: .member))
        let slot = Components.Schemas.MealSlotRead(id: UUID().uuidString, name: "Snack", position: 3, isDefault: false, archived: false, revision: "5")
        let data = try APIJSON.encoder().encode(slot)
        StubURLProtocol.set(host: "planner.example.test") { request in
            let body: Data
            if request.url?.path == "/api/v1/auth/csrf" { body = Data("{\"token\":\"csrf-fixture\"}".utf8) }
            else if request.url?.path == "/api/v1/meal-slots" { body = Data("{\"items\":[],\"revision\":\"6\"}".utf8) }
            else {
                if request.httpMethod == "PUT" { #expect(request.value(forHTTPHeaderField: "If-Match") == "\"opaque-slot-validator\"") }
                body = data
            }
            return (HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: nil, headerFields: ["ETag": "\"opaque-slot-validator\""])!, body)
        }
        let model = MealSlotsModel(selection: selection); model.edit(slot); model.name = "Afternoon snack"
        await model.save()
        #expect(model.error == nil); #expect(model.editing == nil)
    }
    @Test func staleMealSaveRetainsEnteredFieldsAndOpaqueValidator() async throws {
        let configuration = URLSessionConfiguration.ephemeral; configuration.protocolClasses = [StubURLProtocol.self]
        let selection = ServerSelection(credentials: MemoryCredentials(), persist: { _ in }, makeClient: { APIClient(session: $0, configuration: configuration) })
        await selection.requestChange("https://planner.example.test")
        await selection.signedIn(ClientUser(id: UUID(), householdId: UUID(), role: .member))
        let id = UUID().uuidString, slot = UUID().uuidString
        let json = "{\"id\":\"\(id)\",\"revision\":\"4\",\"date\":\"2026-11-02\",\"slotId\":\"\(slot)\",\"type\":\"other\",\"label\":\"Takeaway\"}"
        let meal = try APIJSON.decoder().decode(Components.Schemas.ScheduledMealRead.self, from: Data(json.utf8))
        StubURLProtocol.set(host: "planner.example.test") { request in
            var status = 200, body = json, headers = ["ETag": "\"opaque-meal-version\""]
            if request.url?.path == "/api/v1/auth/csrf" { body = "{\"token\":\"csrf-fixture\"}" }
            else if request.httpMethod == "PUT" {
                #expect(request.value(forHTTPHeaderField: "If-Match") == "\"opaque-meal-version\"")
                status = 412; body = "{\"code\":\"stale_revision\"}"; headers = [:]
            }
            return (HTTPURLResponse(url: request.url!, statusCode: status, httpVersion: nil, headerFields: headers)!, Data(body.utf8))
        }
        let model = MealEditorModel(selection: selection, meal: meal, type: .other, date: meal.date, slotId: slot)
        await model.load(); model.draft.label = "Changed takeaway"; await model.submit()
        #expect(!model.saved); #expect(model.draft.label == "Changed takeaway")
        #expect(model.error != nil); #expect(model.dirty)
    }
}
