import Foundation
import RecipeManagerAPI

struct ShoppingRecipeEntry: Identifiable, Equatable {
    var id: String { recipeId }
    var recipeId: String
    var title: String
    var servings: String
}
struct ShoppingSourceDraft: Equatable {
    var kind = "mealRange"
    var from = PlannerDates.monday(PlannerDates.today(timezone: "UTC"))
    var to = PlannerDates.add(PlannerDates.monday(PlannerDates.today(timezone: "UTC")), days: 6)
    var basis: Components.Schemas.ShoppingBasis = .planned
    var entries: [ShoppingRecipeEntry] = []
    var generationBasis: Components.Schemas.ShoppingBasis { kind == "recipes" ? .planned : basis }
    func selection() -> Components.Schemas.ShoppingSelection {
        if kind == "recipes" { return .init(kind: .recipes, entries: entries.map { .init(recipeId: $0.recipeId, servings: $0.servings) }) }
        return .init(kind: .mealRange, from: from, to: to)
    }
}
struct ShoppingQuantityDraft: Equatable {
    var text = ""
    var kind: Components.Schemas.QuantityKind = .unknown
    var unit: Components.Schemas.IngredientUnit = .item
    var standard: Components.Schemas.MeasurementStandard = .AU
    init() {}
    init(_ row: Components.Schemas.ShoppingItemRead) {
        if let quantity = row.quantityOverride {
            text = quantity.quantityText; kind = quantity.kind; unit = quantity.unit; standard = quantity.measurementStandard ?? .AU
        } else if let exact = row.effectiveExact {
            text = exact.denominator == "1" ? exact.numerator : "\(exact.numerator)/\(exact.denominator)"
            kind = .numeric
            unit = row.dimension?.rawValue == "Mass" ? .g : row.dimension?.rawValue == "Volume" ? .ml : .item
        } else { text = row.contributions.first?.quantityText ?? row.displayText }
    }
    func write() -> Components.Schemas.ShoppingQuantityInput { .init(quantityText: text, kind: kind, unit: unit, measurementStandard: standard) }
}
