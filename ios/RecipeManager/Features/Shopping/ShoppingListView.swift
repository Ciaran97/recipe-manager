import SwiftUI
import RecipeManagerAPI

struct ShoppingListView: View {
    let selection: ServerSelection
    @State private var model: ShoppingListModel
    @State private var discard = false
    @State private var deleting = false
    init(selection: ServerSelection) { self.selection = selection; _model = State(initialValue: ShoppingListModel(selection: selection)) }
    var body: some View {
        List {
            if let error = model.error { Section { Text(error).foregroundStyle(.red).accessibilityIdentifier("shopping-error") } }
            if let notice = model.notice { Section { Text(notice).accessibilityIdentifier("shopping-notice") } }
            Section {
                ForEach(model.lists, id: \.id) { list in
                    Button { Task { await model.open(list.id) } } label: {
                        VStack(alignment: .leading) { Text(list.name); Text("\(list.summary.knownItemCount) known · \(list.summary.unresolvedCount) unresolved · \(list.summary.purchasedCount) purchased").font(.caption).foregroundStyle(Color(uiColor: .label).opacity(0.8)) }
                            .frame(maxWidth: .infinity, alignment: .leading).contentShape(Rectangle())
                    }.buttonStyle(.borderless).accessibilityIdentifier("shopping-list-" + list.id)
                }
                if model.cursor != nil { Button("More shopping lists") { Task { await model.loadIndex(append: true) } } }
                if model.lists.isEmpty { Text("Create a list from meals, selected recipes or manual items.").foregroundStyle(Color(uiColor: .label).opacity(0.8)) }
            } header: { Text("Saved lists").foregroundStyle(Color(uiColor: .label)) }
            if let list = model.current {
                Section {
                    Text(list.name).font(.title2).accessibilityIdentifier("shopping-current-name")
                    Text("\(list.summary.knownItemCount) known · \(list.summary.unresolvedCount) unresolved · \(list.summary.purchasedCount) purchased")
                    Text(list.basis == .planned ? "Uses planned portions to prepare. Leftovers do not add ingredients." : "Uses explicitly reviewed actual preparation.").font(.caption)
                    if model.deletedRemotely { Text("This list was removed by another member. Your entered text is retained.").foregroundStyle(.orange) }
                    if model.outdated { Text("This list changed on another device. Reload before saving; your draft is retained.").foregroundStyle(.orange) }
                    if model.sourcesChanged { Text("Recipes or meals may have changed. Review requirements to update this saved snapshot.").foregroundStyle(.orange) }
                    Button("Add manual item") { model.begin(.manual) }.accessibilityIdentifier("shopping-add-manual")
                    if list.selection != nil { Button("Review updated requirements") { model.begin(.reconcile) } }
                    Button("Rename list") { model.begin(.rename) }
                    Button("Refresh list") { Task { await model.open(list.id) } }
                    Button("Delete list", role: .destructive) { deleting = true }
                    Toggle("Show removed generated items", isOn: $model.showRemoved)
                }
                ForEach(model.groups, id: \.0) { group in
                    Section { ForEach(group.1, id: \.id) { row in shoppingRow(row) } } header: { Text(group.0).foregroundStyle(Color(uiColor: .label)) }
                }
                if model.groups.isEmpty { Section { Text("No visible items. Add a manual item or review requirements.") } }
            }
        }
        .navigationTitle("Shopping Lists")
        .toolbar { ToolbarItem(placement: .topBarTrailing) { Button("New list", systemImage: "plus") { model.begin(.create) }.accessibilityIdentifier("shopping-new-list") } }
        .disabled(model.busy)
        .refreshable { if let id = model.current?.id { await model.open(id) }; await model.loadIndex() }
        .task { await model.initialize(); await model.synchronize() }
        .onHouseholdChange(domains: [.shopping, .recipes, .planner, .settings]) { model.receive($0) }
        .confirmationDialog("Delete this list and all its items?", isPresented: $deleting) {
            Button("Delete shopping list", role: .destructive) { Task { await model.deleteList() } }
        }
        .sheet(item: $model.editor) { editor in
            NavigationStack {
                ShoppingEditorView(model: model, editor: editor)
                    .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Cancel") { if model.dirty { discard = true } else { model.cancel() } } } }
                    .confirmationDialog("Discard unsaved shopping changes?", isPresented: $discard) { Button("Discard changes", role: .destructive) { model.cancel() } }
            }.interactiveDismissDisabled(model.dirty || model.busy)
        }
    }
    private func shoppingRow(_ row: Components.Schemas.ShoppingItemRead) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            Toggle(isOn: Binding(get: { row.purchased }, set: { _ in Task { await model.purchase(row) } })) {
                VStack(alignment: .leading) { Text(row.label).font(.headline); Text(row.displayText).strikethrough(row.purchased) }
            }.disabled(row.removed || model.dirty).accessibilityIdentifier("shopping-purchased-" + row.id)
            if row.unresolved { Label("Amount unresolved", systemImage: "exclamationmark.circle").foregroundStyle(.orange) }
            if row.additionalAmountUnresolved { Text("Additional amount unresolved").foregroundStyle(.orange) }
            if row.hasOverride { Text("Your quantity").font(.caption) }
            if row.manual { Text("Manual item").font(.caption) }
            if row.isApproximate { Text("Rounded for display").font(.caption) }
            if row.removed { Button("Restore \(row.label)") { Task { await model.restore(row) } } }
            else {
                HStack {
                    Button("Edit \(row.label)") { model.begin(.item, item: row) }.accessibilityIdentifier("shopping-edit-" + row.id)
                    Spacer()
                    Button("Remove \(row.label)", role: .destructive) { Task { await model.remove(row) } }
                }.buttonStyle(.borderless)
            }
            if !row.contributions.isEmpty {
                DisclosureGroup("Contributing recipes") {
                    ForEach(Array(row.contributions.enumerated()), id: \.offset) { _, source in
                        Text("\(source.sourceLabel): \(source.quantityText) \(source.unit.rawValue) · \(source.servings) prepared servings" + (source.preparation.map { " · " + $0 } ?? ""))
                            .font(.caption).frame(maxWidth: .infinity, alignment: .leading)
                    }
                }
            }
        }.padding(.vertical, 4)
    }
}

private struct ShoppingEditorView: View {
    @Bindable var model: ShoppingListModel
    let editor: ShoppingListModel.Editor
    private var title: String {
        switch editor { case .create: "Create shopping list"; case .manual: "Add manual item"; case .item: "Edit shopping item"; case .rename: "Rename list"; case .reconcile: "Review requirements" }
    }
    var body: some View {
        Form {
            if let error = model.error { Section { Text(error).foregroundStyle(.red) } }
            if model.outdated { Section { Text("The saved list changed. Your input is retained.").foregroundStyle(.orange) } }
            switch editor {
            case .create:
                Section { TextField("List name", text: $model.name).accessibilityIdentifier("shopping-list-name") }
                ShoppingSourcesView(model: model, allowManual: true, allowActual: false)
                Section { Button("Create shopping list") { Task { await model.create() } }.disabled(model.name.trimmingCharacters(in: .whitespaces).isEmpty).accessibilityIdentifier("shopping-create") }
            case .manual:
                Section { TextField("Item name", text: $model.label).accessibilityIdentifier("shopping-item-name"); ShoppingQuantityFields(draft: $model.quantity) }
                Section { Picker("Shopping category", selection: $model.categoryId) { Text("Other").tag(""); ForEach(model.categories, id: \.id) { Text($0.name).tag($0.id) } } }
                Section { Text("Blank or text amounts stay independently visible as unresolved."); Button("Save manual item") { Task { await model.saveManual() } }.disabled(model.label.trimmingCharacters(in: .whitespaces).isEmpty).accessibilityIdentifier("shopping-save-manual") }
            case .item:
                Section {
                    if model.item?.manual == true { TextField("Item name", text: $model.label) }
                    else { Text(model.item?.label ?? "Item").font(.headline); Text("Ingredient name corrections belong in the household catalogue. This list keeps its saved name until reconciliation.").font(.caption) }
                    ShoppingQuantityFields(draft: $model.quantity)
                }
                Section {
                    Button("Save item changes") { Task { await model.saveItem() } }.accessibilityIdentifier("shopping-save-item")
                    if model.item?.hasOverride == true { Button("Use generated quantity") { Task { await model.clearOverride() } } }
                }
            case .rename:
                Section { TextField("List name", text: $model.name); Button("Save list name") { Task { await model.rename() } }.disabled(model.name.trimmingCharacters(in: .whitespaces).isEmpty) }
            case .reconcile:
                ShoppingSourcesView(model: model, allowManual: false, allowActual: true)
                Section {
                    Button("Preview requirements") { Task { await model.previewChanges() } }.accessibilityIdentifier("shopping-preview")
                    if model.outdated { Button("Reload list and keep review choices") { Task { await model.reloadReview() } } }
                }
                if let preview = model.preview {
                    Section { Text("\(preview.changes.count) changed requirements. \(preview.manualItems.count) manual items remain unchanged.") }
                    ForEach(preview.changes, id: \.key) { change in ShoppingRequirementReview(model: model, change: change) }
                    Section {
                        if model.sourcesChanged { Text("Requirements changed during review. Preview them again.").foregroundStyle(.orange) }
                        if model.needsReview { Toggle("I reviewed the latest requirements and choices", isOn: $model.reviewed).accessibilityIdentifier("shopping-review-confirm") }
                        Button("Apply reviewed requirements") { Task { await model.applyChanges() } }
                            .disabled(model.sourcesChanged || model.needsReview && !model.reviewed).accessibilityIdentifier("shopping-apply")
                    }
                }
            }
        }.navigationTitle(title).navigationBarTitleDisplayMode(.inline).disabled(model.busy)
        .onHouseholdChange(domains: [.shopping, .recipes, .planner, .settings]) { model.receive($0) }
    }
}

private struct ShoppingQuantityFields: View {
    @Binding var draft: ShoppingQuantityDraft
    var body: some View {
        Picker("Amount type", selection: $draft.kind) { Text("Text / unresolved").tag(Components.Schemas.QuantityKind.unknown); Text("Known quantity").tag(Components.Schemas.QuantityKind.numeric) }
            .accessibilityIdentifier("shopping-quantity-kind")
        TextField("Quantity or text", text: $draft.text).keyboardType(.numbersAndPunctuation).autocorrectionDisabled().accessibilityIdentifier("shopping-quantity")
        if draft.kind == .numeric {
            Picker("Unit", selection: $draft.unit) {
                ForEach(Components.Schemas.IngredientUnit.allCases, id: \.self) { Text($0.rawValue).tag($0) }
            }
            Picker("Measurement standard", selection: $draft.standard) { Text("Australian").tag(Components.Schemas.MeasurementStandard.AU); Text("US").tag(Components.Schemas.MeasurementStandard.US) }
        }
    }
}

private struct ShoppingSourcesView: View {
    @Bindable var model: ShoppingListModel
    let allowManual: Bool
    let allowActual: Bool
    var body: some View {
        Section {
            Picker("Source", selection: $model.source.kind) {
                Text("Meal plan date range").tag("mealRange"); Text("Selected recipes").tag("recipes")
                if allowManual { Text("Empty list").tag("manual") }
            }.accessibilityIdentifier("shopping-source-kind")
            if model.source.kind == "mealRange" {
                DatePicker("From date", selection: Binding(get: { PlannerDates.date(model.source.from) }, set: { model.source.from = PlannerDates.string($0) }), displayedComponents: .date).environment(\.timeZone, PlannerDates.calendar.timeZone)
                DatePicker("Through date", selection: Binding(get: { PlannerDates.date(model.source.to) }, set: { model.source.to = PlannerDates.string($0) }), displayedComponents: .date).environment(\.timeZone, PlannerDates.calendar.timeZone)
                if allowActual {
                    Picker("Preparation basis", selection: $model.source.basis) { Text("Planned portions prepared").tag(Components.Schemas.ShoppingBasis.planned); Text("Confirmed actual portions prepared").tag(Components.Schemas.ShoppingBasis.actual) }
                    Text("Actual preparation requires confirmation for every selected cooking event.").font(.caption)
                } else { Text("Uses planned cooking portions. Leftovers never add ingredients.").font(.caption) }
            }
            if model.source.kind == "recipes" {
                TextField("Find recipes", text: $model.recipeQuery).accessibilityIdentifier("shopping-recipe-query")
                Button("Search recipes") { Task { await model.searchRecipes() } }
                ForEach(model.recipes, id: \.id) { recipe in
                    Button("Add \(recipe.title)") { model.addRecipe(recipe) }.disabled(model.source.entries.contains { $0.recipeId == recipe.id })
                }
                if model.recipeCursor != nil { Button("More recipes") { Task { await model.searchRecipes(append: true) } } }
                ForEach($model.source.entries) { $entry in
                    VStack(alignment: .leading) {
                        Text(entry.title).font(.headline)
                        PortionField(title: "Servings for \(entry.title)", text: $entry.servings)
                        Button("Remove \(entry.title)", role: .destructive) { model.source.entries.removeAll { $0.recipeId == entry.recipeId } }
                    }
                }
            }
        } header: { Text("Requirement source").foregroundStyle(Color(uiColor: .label)) }.onChange(of: model.source) { _, _ in model.sourceEdited() }
    }
}

private struct ShoppingRequirementReview: View {
    @Bindable var model: ShoppingListModel
    let change: Components.Schemas.ShoppingChange
    private var choice: Binding<Components.Schemas.ReconciliationChoice> {
        Binding(get: { model.choices[change.key]?.choice ?? change.defaultChoice }, set: { model.choices[change.key] = .init(key: change.key, choice: $0); model.reviewed = false })
    }
    private var purchase: Binding<String> {
        Binding(get: { model.choices[change.key]?.purchased.map { $0 ? "yes" : "no" } ?? "" }, set: { model.choices[change.key]?.purchased = $0.isEmpty ? nil : $0 == "yes"; model.reviewed = false })
    }
    var body: some View {
        Section(change.newBaseline?.label ?? change.oldBaseline?.label ?? "Requirement") {
            LabeledContent("Saved requirement", value: change.oldBaseline?.displayText ?? "Not present")
            LabeledContent("Updated requirement", value: change.newBaseline?.displayText ?? "No longer required")
                .accessibilityElement(children: .combine).accessibilityIdentifier("shopping-updated-" + change.key)
            LabeledContent("Your item", value: change.userState?.displayText ?? "Not present")
            if change.userState?.purchased == true { Text("Purchased") }
            if change.userState?.removed == true { Text("Removed") }
            if change.conflict { Text("This affects an edit, removal or purchased requirement. Choose explicitly.").foregroundStyle(.orange) }
            Picker("Requirement decision", selection: choice) {
                ForEach(change.allowedChoices, id: \.self) { value in Text(value == .acceptNew ? "Accept updated requirement" : value == .keepUser ? "Keep my current item" : "Remove requirement").tag(value) }
            }
            if change.purchaseDecisionRequired && choice.wrappedValue == .acceptNew {
                Picker("Purchase state for increased requirement", selection: purchase) { Text("Choose purchase state").tag(""); Text("Already purchased, including increase").tag("yes"); Text("Still needs purchasing").tag("no") }
            }
        }
    }
}
