import SwiftUI
import RecipeManagerAPI

struct CookingSourceView: View {
    let selection: ServerSelection
    let slots: [Components.Schemas.MealSlotRead]
    let completed: () -> Void
    @State private var model: CookingSourceModel
    @Environment(\.dismiss) private var dismiss
    @State private var discard = false
    init(selection: ServerSelection, sourceId: String, slots: [Components.Schemas.MealSlotRead], proposed: SourceProposal? = nil, completed: @escaping () -> Void = {}) {
        self.selection = selection; self.slots = slots; self.completed = completed
        _model = State(initialValue: CookingSourceModel(selection: selection, sourceId: sourceId, proposal: proposed))
    }
    var body: some View {
        Form {
            if let error = model.error { Section { Text(error).foregroundStyle(.red).accessibilityIdentifier("source-error") } }
            if let source = model.source {
                Section(source.recipeTitle) {
                    Text("\(source.date) · Planned prepare \(source.plannedPrepared), eat \(source.plannedEaten)")
                    Text("Actual prepare \(source.actualPrepared ?? "Not confirmed"), eat \(source.actualEaten ?? "Not confirmed")")
                    Text("Available: \(source.availablePortions) (\(source.basis))").accessibilityIdentifier("source-availability")
                }
                if model.proposal == nil {
                    Section("Optional cooking confirmation") {
                        PortionField(title: "Actually prepared portions", text: $model.actualPrepared)
                        PortionField(title: "Actually eaten at cooking meal", text: $model.actualEaten)
                        Button("Confirm cooking quantities") { Task { await model.actuals() } }.disabled(model.needsReview && !model.reviewed)
                        if source.actualPrepared != nil { Button("Clear actual cooking confirmation") { Task { await model.actuals(clear: true) } }.disabled(model.needsReview && !model.reviewed) }
                        Text("Shopping requirements continue to use planned quantities. Confirmation does not create leftover meals.").font(.caption)
                    }
                } else if let proposal = model.proposal {
                    Section("Proposed change") {
                        Text(proposal.action == "delete" ? "Delete this cooking event" : "\(proposal.draft.date) · Prepare \(proposal.draft.prepared), eat \(proposal.draft.eaten)")
                        Text("The source change and your explicit leftover resolutions are applied together.").font(.caption)
                    }
                }
                if model.resolving {
                    resolutionSections
                } else if !source.allocations.isEmpty {
                    Section("Scheduled leftovers") {
                        ForEach(source.allocations, id: \.mealId) { item in
                            VStack(alignment: .leading) {
                                Text(item.date)
                                Text("Planned \(item.allocation.plannedPortions) · Consumed \(item.allocation.consumedPortions ?? "Not confirmed") · Reserved \(item.allocation.reservedRemainder)").font(.caption)
                            }
                        }
                    }
                }
                if model.proposal == nil {
                    Section("Recipe snapshot") {
                        if let snapshot = model.meal?.cookingEvent?.recipeSnapshot {
                            NavigationLink("View saved recipe") { Form { SnapshotContent(selection: selection, snapshot: snapshot) }.navigationTitle("Saved recipe") }
                        }
                        Button("Review latest recipe") { Task { await model.refresh(apply: false) } }.disabled(model.dirty || model.resolving || model.needsReview)
                        if let preview = model.preview {
                            NavigationLink("Compare saved and latest recipe") {
                                Form {
                                    Section("Saved recipe") { SnapshotContent(selection: selection, snapshot: preview.current) }
                                    Section("Latest recipe") { SnapshotContent(selection: selection, snapshot: preview.proposed) }
                                }.navigationTitle("Recipe changes")
                            }
                            Button("Apply reviewed recipe refresh") { Task { await model.refresh(apply: true) } }
                        }
                        Text("Recipe edits never automatically change this saved recipe.").font(.caption)
                    }
                }
            }
            if model.needsReview || model.error != nil {
                Section("Review concurrent changes") {
                    Button("Reload balances; keep proposed changes") { Task { await model.load(preserve: true) } }
                    Button("Reload and discard quantity changes") { Task { await model.load() } }
                    if model.needsReview {
                        Toggle("I reviewed the latest balances and resolutions", isOn: $model.reviewed)
                    }
                }
            }
        }.disabled(model.busy).navigationTitle("Cooking and leftovers")
        .interactiveDismissDisabled(model.dirty || model.busy)
        .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Done") { if model.dirty { discard = true } else { dismiss() } }.disabled(model.busy) } }
        .confirmationDialog("Discard cooking changes?", isPresented: $discard) { Button("Discard changes", role: .destructive) { dismiss() } }
        .task { if model.meal == nil { await model.load() } }
        .onChange(of: model.saved) { _, saved in if saved { completed(); dismiss() } }
    }

    @ViewBuilder private var resolutionSections: some View {
        Section("Resolve dependent meals") {
            Text("Keep meals unchanged or explicitly release, edit, move, reassign or delete them. Recorded consumption is never removed automatically.")
            DatePicker("Target cooking week", selection: Binding(get: { PlannerDates.date(model.targetWeek) }, set: { model.targetWeek = PlannerDates.monday(PlannerDates.string($0)) }), displayedComponents: .date)
                .environment(\.timeZone, PlannerDates.calendar.timeZone)
            Button("Find reassignment targets") { Task { await model.findTargets() } }
        }
        ForEach($model.rows) { $row in
            Section(row.title) {
                Picker("Resolution", selection: $row.action) {
                    Text("Keep unchanged").tag("keep"); Text("Release unused reservation").tag("release")
                    Text("Edit quantities").tag("edit"); Text("Move meal").tag("move")
                    Text("Reassign source").tag("reassign"); Text("Delete meal").tag("delete")
                }.accessibilityIdentifier("resolution-\(row.id)")
                if row.action == "edit" || row.action == "reassign" {
                    PortionField(title: "Planned leftover portions", text: $row.planned)
                    PortionField(title: "Remaining reserved portions", text: $row.reserved)
                    Picker("Recorded consumption", selection: $row.consumption) {
                        Text("Keep recorded consumption").tag("keep"); Text("Correct consumption").tag("replace"); Text("Clear confirmation").tag("clear")
                    }
                    if row.consumption == "replace" { PortionField(title: "Corrected consumed portions", text: $row.consumed) }
                }
                if row.action == "move" { MealPositionFields(date: $row.date, slotId: $row.slotId, slots: slots) }
                if row.action == "reassign" {
                    if let summary = row.targetSummary { Text(summary).font(.caption) }
                    ForEach(model.targetMeals, id: \.id) { target in
                        if let cook = target.cookingEvent {
                            Button("\(target.date) · \(cook.recipeSnapshot.title) · \(cook.availablePortions) available" + (row.targetId == cook.id && row.targetRevision != nil ? " ✓" : "")) {
                                Task { await model.selectTarget(rowId: row.id, sourceId: cook.id) }
                            }
                        }
                    }
                }
                if row.action == "delete" { Toggle("Confirm removal of recorded consumption", isOn: $row.confirmRemoval) }
                if row.action == "release" { Text("Only the unused reservation is released. Consumption is retained.").font(.caption) }
            }
        }
        Section {
            Button("Apply source change and resolutions") { Task { await model.resolve() } }.disabled(model.needsReview && !model.reviewed)
        }
    }
}

struct SnapshotContent: View {
    let selection: ServerSelection
    let snapshot: Components.Schemas.RecipeSnapshot
    var body: some View {
        Text(snapshot.title).font(.headline)
        if let image = snapshot.imageAssetId { PrivateRecipeImage(selection: selection, id: image, title: snapshot.title) }
        if let description = snapshot.description { Text(description) }
        Text("\(snapshot.defaultServings) servings · \(snapshot.measurementStandard.rawValue) measures")
        if let prep = snapshot.prepMinutes { Text("Preparation: \(prep) minutes") }
        if let cook = snapshot.cookMinutes { Text("Cooking: \(cook) minutes") }
        ForEach(snapshot.ingredients, id: \.id) { line in
            VStack(alignment: .leading) {
                if let group = snapshot.groups.first(where: { $0.id == line.groupId }) { Text(group.title).font(.caption) }
                Text("\(line.quantityText) \(line.unit.rawValue) \(line.ingredientName)")
                if line.kind == .unknown || line.uncertain { Text("Quantity requires review").font(.caption).foregroundStyle(.orange) }
                if let preparation = line.preparation { Text(preparation).font(.caption) }
            }
        }
        ForEach(snapshot.instructions, id: \.id) { instruction in Text("\(instruction.position + 1). \(instruction.text)") }
        if let notes = snapshot.notes { Text(notes) }
        if let url = snapshot.sourceUrl { Text(url).textSelection(.enabled) }
    }
}
