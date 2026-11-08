import SwiftUI
import Observation
import RecipeManagerAPI

@Observable @MainActor final class LeftoversModel {
    var meal: Components.Schemas.ScheduledMealRead?
    var source: Components.Schemas.LeftoversAvailability?
    var sources: [Components.Schemas.ScheduledMealRead] = []
    var sourceWeek: String
    var sourceId = ""
    var date: String
    var slotId: String
    var planned = "2"
    var reserved = "2"
    var consumed = "2"
    var remaining = "0"
    var actualPrepared = ""
    var actualEaten = ""
    var confirmSource = false
    var confirmRemoval = false
    var error: String?
    var busy = false
    var saved = false
    private var initial = ""
    private var initialConsumption = ""
    private let requests: ScopedAPI
    private var etag: String?
    private var retry = UUID()
    var dirty: Bool { initial != signature || initialConsumption != consumptionSignature }
    private var signature: String { [sourceId, date, slotId, planned, reserved].joined(separator: "|") }
    private var consumptionSignature: String { [consumed, remaining, actualPrepared, actualEaten, String(confirmSource)].joined(separator: "|") }
    var canConsume: Bool { meal != nil && initial == signature && sourceId == meal?.leftovers?.sourceCookingEventId }
    init(selection: ServerSelection, meal: Components.Schemas.ScheduledMealRead?, date: String, slotId: String) {
        requests = ScopedAPI(selection: selection); self.meal = meal; self.date = date; self.slotId = slotId
        sourceWeek = PlannerDates.monday(date); sourceId = meal?.leftovers?.sourceCookingEventId ?? ""
        initial = signature; initialConsumption = consumptionSignature
    }
    func load() async {
        await perform {
            if let id = self.meal?.id {
                let response = try await self.requests.send(path: "/api/v1/meals/" + id)
                let value = try APIJSON.decoder().decode(Components.Schemas.ScheduledMealRead.self, from: response.data)
                self.meal = value; self.etag = response.etag; self.date = value.date; self.slotId = value.slotId
                if let allocation = value.leftovers {
                    self.sourceId = allocation.sourceCookingEventId; self.planned = allocation.plannedPortions; self.reserved = allocation.reservedRemainder
                    self.consumed = allocation.consumedPortions ?? allocation.plannedPortions
                    self.remaining = allocation.consumedPortions == nil ? "0" : allocation.reservedRemainder
                    self.sourceWeek = PlannerDates.monday(allocation.sourceDate)
                }
            }
            self.initial = self.signature
            try await self.readSource()
            self.initialConsumption = self.consumptionSignature
        }
        await searchSources()
    }
    func searchSources() async {
        await perform {
            let response = try await self.requests.send(path: "/api/v1/meals?from=\(self.sourceWeek)&to=\(PlannerDates.add(self.sourceWeek, days: 6))")
            self.sources = try APIJSON.decoder().decode(Components.Schemas.MealCalendarRead.self, from: response.data).items.filter { $0.cookingEvent != nil }
        }
    }
    func choose(_ id: String) async { sourceId = id; source = nil; await perform { try await self.readSource() } }
    private func readSource() async throws {
        guard !sourceId.isEmpty else { return }
        let response = try await requests.send(path: "/api/v1/cooking-events/\(sourceId)/leftovers")
        source = try APIJSON.decoder().decode(Components.Schemas.LeftoversAvailability.self, from: response.data)
        actualPrepared = source?.actualPrepared ?? source?.plannedPrepared ?? ""
        actualEaten = source?.actualEaten ?? source?.plannedEaten ?? ""
    }
    func save() async {
        await perform {
            guard let source = self.source, source.sourceCookingEventId == self.sourceId else { return }
            let previous = self.meal?.leftovers
            let write = Components.Schemas.LeftoverWrite(sourceCookingEventId: source.sourceCookingEventId, sourceRevision: source.sourceRevision,
                date: self.date, slotId: self.slotId, plannedPortions: self.planned, reservedRemainder: self.meal == nil ? nil : self.reserved,
                previousSourceRevision: previous?.sourceCookingEventId != self.sourceId ? previous?.sourceRevision : nil)
            _ = try await self.requests.send(path: self.meal.map { "/api/v1/meals/\($0.id)/leftovers" } ?? "/api/v1/meals/leftovers", method: self.meal == nil ? "POST" : "PUT", body: try APIJSON.encoder().encode(write), etag: self.etag, retryKey: self.meal == nil ? self.retry : nil)
            self.saved = true
        }
    }
    func consumption(clear: Bool = false) async {
        guard canConsume, let meal, let source else { error = "Save or discard reservation changes before confirming consumption."; return }
        await perform {
            let body: Data
            if clear { body = try APIJSON.encoder().encode(Components.Schemas.ClearConsumptionWrite(reservedRemainder: self.remaining, sourceRevision: source.sourceRevision)) }
            else {
                let actuals = self.confirmSource ? Components.Schemas.ConsumptionWrite.sourceActualsPayload(actualPrepared: self.actualPrepared, actualEaten: self.actualEaten) : nil
                body = try APIJSON.encoder().encode(Components.Schemas.ConsumptionWrite(consumedPortions: self.consumed, reservedRemainder: self.remaining, sourceRevision: source.sourceRevision, sourceActuals: actuals))
            }
            _ = try await self.requests.send(path: "/api/v1/meals/\(meal.id)/consumption", method: clear ? "DELETE" : "PUT", body: body, etag: self.etag)
            self.saved = true
        }
    }
    func duplicate() async {
        guard let meal, let source, sourceId == meal.leftovers?.sourceCookingEventId,
              planned == meal.leftovers?.plannedPortions, reserved == meal.leftovers?.reservedRemainder else { error = "Save reservation changes before duplicating."; return }
        await perform {
            _ = try await self.requests.send(path: "/api/v1/meals/\(meal.id)/duplicate", method: "POST", body: try APIJSON.encoder().encode(Components.Schemas.DuplicateMealWrite(date: self.date, slotId: self.slotId, sourceRevision: source.sourceRevision)), etag: self.etag, retryKey: self.retry)
            self.saved = true
        }
    }
    func remove() async {
        guard let meal else { return }
        await perform {
            _ = try await self.requests.send(path: "/api/v1/meals/\(meal.id)", method: "DELETE", body: try APIJSON.encoder().encode(Components.Schemas.DeleteMealWrite(sourceRevision: self.meal?.leftovers?.sourceRevision, confirmConsumedRemoval: self.confirmRemoval)), etag: self.etag)
            self.saved = true
        }
    }
    private func perform(_ action: () async throws -> Void) async {
        guard !busy else { return }; busy = true; error = nil; defer { busy = false }
        do { try await action() } catch { self.error = apiErrorMessage(error) }
    }
}

struct LeftoversView: View {
    let slots: [Components.Schemas.MealSlotRead]
    @State private var model: LeftoversModel
    @Environment(\.dismiss) private var dismiss
    @State private var discard = false
    @State private var deleting = false
    init(selection: ServerSelection, meal: Components.Schemas.ScheduledMealRead?, date: String, slotId: String, slots: [Components.Schemas.MealSlotRead]) {
        self.slots = slots; _model = State(initialValue: LeftoversModel(selection: selection, meal: meal, date: date, slotId: slotId))
    }
    var body: some View {
        Form {
            if let error = model.error { Text(error).foregroundStyle(.red).accessibilityIdentifier("leftovers-error") }
            Section("When") { MealPositionFields(date: $model.date, slotId: $model.slotId, slots: slots) }
            Section("Previous cooking event") {
                DatePicker("Source week", selection: Binding(get: { PlannerDates.date(model.sourceWeek) }, set: { model.sourceWeek = PlannerDates.monday(PlannerDates.string($0)) }), displayedComponents: .date)
                    .environment(\.timeZone, PlannerDates.calendar.timeZone)
                Button("Find cooking events") { Task { await model.searchSources() } }
                ForEach(model.sources, id: \.id) { meal in
                    if let cook = meal.cookingEvent {
                        Button { Task { await model.choose(cook.id) } } label: {
                            VStack(alignment: .leading) {
                                Text(cook.recipeSnapshot.title + (model.sourceId == cook.id ? " ✓" : ""))
                                Text("\(meal.date) · \(cook.availablePortions) available (\(cook.availabilityBasis))").font(.caption)
                            }
                        }.accessibilityIdentifier("leftovers-source-\(cook.id)")
                    }
                }
                if let source = model.source { Text("\(source.recipeTitle): \(source.availablePortions) available (\(source.basis))").accessibilityIdentifier("leftovers-availability") }
            }
            Section("Reservation") {
                PortionField(title: "Planned leftover portions", text: $model.planned)
                if model.meal != nil {
                    PortionField(title: "Remaining reserved portions", text: $model.reserved)
                    Button("Release unused reservation") { model.reserved = "0" }
                    Text("Releasing changes only the remaining reservation. Confirmed consumption and original planned portions are retained.").font(.caption)
                }
                Button("Save leftover meal") { Task { await model.save() } }.disabled(model.source == nil).accessibilityIdentifier("leftovers-save")
            }
            if model.meal != nil {
                Section("Optional consumption confirmation") {
                    if let allocation = model.meal?.leftovers { Text("Recorded consumption: \(allocation.consumedPortions ?? "Not confirmed") · Reserved: \(allocation.reservedRemainder)") }
                    if !model.canConsume { Text("Save reservation changes before recording consumption.").font(.caption) }
                    PortionField(title: "Actually consumed portions", text: $model.consumed)
                    PortionField(title: "Reservation after confirmation", text: $model.remaining)
                    if model.source?.actualPrepared == nil {
                        Toggle("Also confirm original cooking quantities", isOn: $model.confirmSource)
                            .accessibilityIdentifier("leftovers-confirm-source")
                        if !model.confirmSource { Text("Confirm the original cooking quantities to record actual leftover consumption.").font(.caption) }
                        if model.confirmSource {
                            PortionField(title: "Actually prepared portions", text: $model.actualPrepared)
                            PortionField(title: "Actually eaten at cooking meal", text: $model.actualEaten)
                        }
                    }
                    Button("Confirm leftover consumption") { Task { await model.consumption() } }
                        .disabled(!model.canConsume || (model.source?.actualPrepared == nil && !model.confirmSource))
                    if model.meal?.leftovers?.consumedPortions != nil {
                        Button("Clear consumption with entered reservation") { Task { await model.consumption(clear: true) } }.disabled(!model.canConsume)
                    }
                }
                Section {
                    Button("Duplicate leftover meal to selected date and slot") { Task { await model.duplicate() } }
                    if model.meal?.leftovers?.consumedPortions != nil { Toggle("Confirm removal of recorded consumption", isOn: $model.confirmRemoval) }
                    Button("Delete leftover meal", role: .destructive) { deleting = true }.disabled(model.meal?.leftovers?.consumedPortions != nil && !model.confirmRemoval)
                }
            }
            if model.error != nil { Button("Reload and discard entered changes") { Task { await model.load() } } }
        }.disabled(model.busy).navigationTitle("Eat leftovers")
        .interactiveDismissDisabled(model.dirty || model.busy)
        .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Cancel") { if model.dirty { discard = true } else { dismiss() } }.disabled(model.busy) } }
        .confirmationDialog("Discard reservation changes?", isPresented: $discard) { Button("Discard changes", role: .destructive) { dismiss() } }
        .confirmationDialog("Delete leftover meal?", isPresented: $deleting) { Button("Delete leftover meal", role: .destructive) { Task { await model.remove() } } }
        .task { await model.load() }.onChange(of: model.saved) { _, saved in if saved { dismiss() } }
    }
}
