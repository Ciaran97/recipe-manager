import SwiftUI
import Observation
import RecipeManagerAPI

@Observable @MainActor final class MealPlannerModel {
    var week = PlannerDates.monday(PlannerDates.today(timezone: "UTC"))
    var day = PlannerDates.today(timezone: "UTC")
    var calendar: Components.Schemas.MealCalendarRead?
    var error: String?
    var loading = false
    private let requests: ScopedAPI
    private var generation = 0
    init(selection: ServerSelection) { requests = ScopedAPI(selection: selection) }
    func initialize() async {
        if calendar != nil { await load(); return }
        do {
            let response = try await requests.send(path: "/api/v1/household/settings")
            let settings = try APIJSON.decoder().decode(Components.Schemas.HouseholdSettingsRead.self, from: response.data)
            day = PlannerDates.today(timezone: settings.timezone); week = PlannerDates.monday(day)
            await load()
        } catch { self.error = apiErrorMessage(error) }
    }
    func shift(_ days: Int) async { week = PlannerDates.add(week, days: days); day = week; await load() }
    func load() async {
        generation += 1; let current = generation; loading = true; error = nil
        defer { if current == generation { loading = false } }
        do {
            let response = try await requests.send(path: "/api/v1/meals?from=\(week)&to=\(PlannerDates.add(week, days: 6))")
            let value = try APIJSON.decoder().decode(Components.Schemas.MealCalendarRead.self, from: response.data)
            guard current == generation else { return }; calendar = value
        } catch { if current == generation { self.error = apiErrorMessage(error) } }
    }
}

private struct MealPresentation: Identifiable {
    let id = UUID()
    let meal: Components.Schemas.ScheduledMealRead?
    let type: Components.Schemas.MealType
    let date: String
    let slotId: String
}

struct MealPlannerView: View {
    let selection: ServerSelection
    @State private var model: MealPlannerModel
    @State private var editor: MealPresentation?
    @State private var slots = false
    init(selection: ServerSelection) { self.selection = selection; _model = State(initialValue: MealPlannerModel(selection: selection)) }
    var body: some View {
        List {
            if let error = model.error { Text(error).foregroundStyle(.red).accessibilityIdentifier("planner-error") }
            Section {
                NavigationLink("Add this week's ingredients to shopping list") {
                    ShoppingListView(selection: selection, incoming: ShoppingSourceDraft(from: model.week, to: PlannerDates.add(model.week, days: 6)))
                }
                HStack {
                    Button { Task { await model.shift(-7) } } label: {
                        Image(systemName: "chevron.left").frame(minWidth: 44, minHeight: 44).contentShape(Rectangle())
                    }.accessibilityLabel("Previous week")
                    Spacer(); Text("Week of \(model.week)").font(.headline).fixedSize(horizontal: false, vertical: true); Spacer()
                    Button { Task { await model.shift(7) } } label: {
                        Image(systemName: "chevron.right").frame(minWidth: 44, minHeight: 44).contentShape(Rectangle())
                    }.accessibilityLabel("Next week")
                }.buttonStyle(.borderless).disabled(model.loading)
                ScrollView(.horizontal) {
                    HStack {
                        ForEach(0..<7) { offset in
                            let date = PlannerDates.add(model.week, days: offset)
                            Button { model.day = date } label: {
                                VStack { Text(PlannerDates.date(date), format: .dateTime.weekday(.abbreviated)); Text(PlannerDates.date(date), format: .dateTime.day()) }
                                    .padding(8).background(model.day == date ? Color.accentColor.opacity(0.18) : Color.clear, in: RoundedRectangle(cornerRadius: 8))
                            }.buttonStyle(.borderless).accessibilityLabel(date).accessibilityIdentifier("planner-day-\(offset)")
                        }
                    }
                }
            }
            if let calendar = model.calendar {
                ForEach(calendar.slots.filter { slot in !slot.archived || calendar.items.contains(where: { $0.slotId == slot.id }) }, id: \.id) { slot in
                    Section {
                        ForEach(calendar.items.filter { $0.date == model.day && $0.slotId == slot.id }, id: \.id) { meal in
                            Button { editor = MealPresentation(meal: meal, type: meal._type, date: meal.date, slotId: meal.slotId) } label: {
                                VStack(alignment: .leading, spacing: 5) {
                                    Text(meal.cookingEvent?.recipeSnapshot.title ?? meal.leftovers?.recipeTitle ?? meal.label ?? "Meal").font(.headline)
                                    if let cook = meal.cookingEvent {
                                        Text("Prepare \(cook.plannedPrepared) · Eat \(cook.plannedEaten)")
                                        Text("\(cook.availablePortions) available (\(cook.availabilityBasis))").font(.caption)
                                    } else if let leftover = meal.leftovers {
                                        Text("Leftovers · Planned \(leftover.plannedPortions)")
                                        Text("Consumed \(leftover.consumedPortions ?? "Not confirmed") · Reserved \(leftover.reservedRemainder)").font(.caption)
                                    }
                                }
                            }.accessibilityIdentifier("planner-meal-\(meal.id)")
                        }
                        if !slot.archived {
                            Menu {
                                Button("Cook recipe") { editor = MealPresentation(meal: nil, type: .cook, date: model.day, slotId: slot.id) }
                                Button("Eat leftovers") { editor = MealPresentation(meal: nil, type: .leftovers, date: model.day, slotId: slot.id) }
                                Button("Other meal") { editor = MealPresentation(meal: nil, type: .other, date: model.day, slotId: slot.id) }
                            } label: {
                                Text("Add meal").frame(maxWidth: .infinity, alignment: .leading).contentShape(Rectangle())
                            }.buttonStyle(.borderless).accessibilityIdentifier("planner-add-\(slot.name)")
                        }
                    } header: { Text(slot.name).foregroundStyle(Color(uiColor: .label)) }
                }
            }
        }
        .environment(\.timeZone, PlannerDates.calendar.timeZone)
        .navigationTitle("Meal Planner")
        .toolbar { ToolbarItem(placement: .topBarTrailing) {
            Button("Meal slots", systemImage: "slider.horizontal.3") { slots = true }.labelStyle(.iconOnly).accessibilityLabel("Meal slots")
        } }
        .refreshable { await model.load() }
        .task { await model.initialize() }
        .onHouseholdChange(domains: [.planner, .recipes, .settings]) { _ in Task { await model.load() } }
        .sheet(item: $editor, onDismiss: { Task { await model.load() } }) { entry in
            NavigationStack {
                if entry.type == .leftovers {
                    LeftoversView(selection: selection, meal: entry.meal, date: entry.date, slotId: entry.slotId, slots: model.calendar?.slots ?? [])
                } else {
                    MealEditorView(selection: selection, meal: entry.meal, type: entry.type, date: entry.date, slotId: entry.slotId, slots: model.calendar?.slots ?? [])
                }
            }
        }
        .sheet(isPresented: $slots, onDismiss: { Task { await model.load() } }) {
            NavigationStack { MealSlotsView(selection: selection) }
        }
    }
}
