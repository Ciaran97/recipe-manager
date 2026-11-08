import SwiftUI
import RecipeManagerAPI

struct PortionField: View {
    let title: String
    @Binding var text: String
    var body: some View {
        VStack(alignment: .leading) {
            QuantityInput(title: title, text: $text)
            Stepper("Adjust \(title) by 0.5") { step(Decimal(string: "0.5")!) } onDecrement: { step(Decimal(string: "-0.5")!) }
                .font(.caption)
        }
    }
    private func step(_ delta: Decimal) {
        guard text.range(of: #"^[0-9]+(?:\.[0-9]+)?$"#, options: .regularExpression) != nil,
              let current = Decimal(string: text, locale: Locale(identifier: "en_US_POSIX")) else { return }
        let result = current + delta
        guard result >= 0 else { return }
        text = NSDecimalNumber(decimal: result).stringValue
    }
}

struct MealPositionFields: View {
    @Binding var date: String
    @Binding var slotId: String
    let slots: [Components.Schemas.MealSlotRead]
    var body: some View {
        DatePicker("Meal date", selection: Binding(get: { PlannerDates.date(date) }, set: { date = PlannerDates.string($0) }), displayedComponents: .date)
            .environment(\.timeZone, PlannerDates.calendar.timeZone)
        Picker("Meal slot", selection: $slotId) {
            ForEach(slots.filter { !$0.archived || $0.id == slotId }, id: \.id) { slot in Text(slot.name).tag(slot.id) }
        }.accessibilityIdentifier("meal-slot")
    }
}

@Observable @MainActor final class MealSlotsModel {
    var items: [Components.Schemas.MealSlotRead] = []
    var name = ""
    var editing: Components.Schemas.MealSlotRead?
    var error: String?
    var busy = false
    private let requests: ScopedAPI
    private var retry = UUID()
    init(selection: ServerSelection) { requests = ScopedAPI(selection: selection) }
    func load(reviewEditing: Bool = false) async {
        await perform {
            let response = try await self.requests.send(path: "/api/v1/meal-slots")
            self.items = try APIJSON.decoder().decode(Components.Schemas.MealSlotPage.self, from: response.data).items
            if reviewEditing, let editing = self.editing {
                if let current = self.items.first(where: { $0.id == editing.id && !$0.archived }) { self.editing = current }
                else { self.error = "This slot was removed. Cancel this edit and choose an active slot." }
            }
        }
    }
    func edit(_ slot: Components.Schemas.MealSlotRead?) { editing = slot; name = slot?.name ?? ""; retry = UUID() }
    func save() async {
        await perform {
            let slot = self.editing
            let body = Components.Schemas.MealSlotWrite(name: self.name, position: slot?.position ?? Int32(self.items.filter { !$0.archived }.count))
            let tag: String? = if let slot { try await self.validator(slot) } else { nil }
            _ = try await self.requests.send(path: "/api/v1/meal-slots" + (slot.map { "/" + $0.id } ?? ""), method: slot == nil ? "POST" : "PUT", body: try APIJSON.encoder().encode(body), etag: tag, retryKey: slot == nil ? self.retry : nil)
            self.edit(nil)
        }
        if error == nil { await load() }
    }
    func move(_ slot: Components.Schemas.MealSlotRead, by delta: Int32) async {
        await perform {
            let body = Components.Schemas.MealSlotWrite(name: slot.name, position: slot.position + delta)
            let tag = try await self.validator(slot)
            _ = try await self.requests.send(path: "/api/v1/meal-slots/" + slot.id, method: "PUT", body: try APIJSON.encoder().encode(body), etag: tag)
        }
        if error == nil { await load() }
    }
    func remove(_ slot: Components.Schemas.MealSlotRead) async {
        await perform {
            let tag = try await self.validator(slot)
            _ = try await self.requests.send(path: "/api/v1/meal-slots/" + slot.id, method: "DELETE", etag: tag)
        }
        if error == nil { await load() }
    }
    private func validator(_ slot: Components.Schemas.MealSlotRead) async throws -> String {
        let response = try await requests.send(path: "/api/v1/meal-slots/" + slot.id)
        let current = try APIJSON.decoder().decode(Components.Schemas.MealSlotRead.self, from: response.data)
        guard current.revision == slot.revision else { throw MealSlotChanged() }
        guard let tag = response.etag else { throw SessionFailure.invalidResponse }
        return tag
    }
    private func perform(_ action: () async throws -> Void) async {
        guard !busy else { return }; busy = true; error = nil; defer { busy = false }
        do { try await action() } catch { self.error = error is MealSlotChanged ? "This slot changed. Reload slots and review the edit before saving." : apiErrorMessage(error) }
    }
}

private struct MealSlotChanged: Error {}

struct MealSlotsView: View {
    @Environment(\.dismiss) private var dismiss
    @State private var model: MealSlotsModel
    @State private var removing: Components.Schemas.MealSlotRead?
    init(selection: ServerSelection) { _model = State(initialValue: MealSlotsModel(selection: selection)) }
    var body: some View {
        Form {
            if let error = model.error {
                Text(error).foregroundStyle(.red)
                Button("Reload slots; keep entered name") { Task { await model.load(reviewEditing: true) } }
            }
            Section(model.editing == nil ? "New meal slot" : "Edit meal slot") {
                TextField("Slot name", text: $model.name)
                Button("Save slot") { Task { await model.save() } }.disabled(model.name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                if model.editing != nil { Button("Cancel edit") { model.edit(nil) } }
            }
            ForEach(model.items, id: \.id) { slot in
                Section(slot.name + (slot.archived ? " (archived)" : "")) {
                    if !slot.archived {
                        Button("Rename \(slot.name)") { model.edit(slot) }
                        Button("Move \(slot.name) earlier") { Task { await model.move(slot, by: -1) } }.disabled(slot.position == 0)
                        Button("Move \(slot.name) later") { Task { await model.move(slot, by: 1) } }.disabled(slot.position == model.items.filter { !$0.archived }.count - 1)
                        Button("Remove \(slot.name)", role: .destructive) { removing = slot }
                    }
                }
            }
            Text("Removing a slot that contains meals archives it. Existing meals remain visible.").font(.caption)
        }.disabled(model.busy).navigationTitle("Meal slots").task { await model.load() }
        .toolbar { ToolbarItem(placement: .confirmationAction) { Button("Done") { dismiss() } } }
        .confirmationDialog("Remove meal slot?", isPresented: Binding(get: { removing != nil }, set: { if !$0 { removing = nil } })) {
            Button("Remove slot", role: .destructive) { if let slot = removing { Task { await model.remove(slot); removing = nil } } }
        }
    }
}
