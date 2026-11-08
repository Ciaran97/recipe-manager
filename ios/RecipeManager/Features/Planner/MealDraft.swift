import Foundation
import RecipeManagerAPI

struct MealDraft: Equatable {
    var date: String
    var slotId: String
    var recipeId = ""
    var recipeRevision: String?
    var prepared = "2"
    var eaten = "2"
    var label = ""
    func cookingWrite() -> Components.Schemas.CookingWrite {
        .init(recipeId: recipeId, date: date, slotId: slotId, plannedPrepared: prepared, plannedEaten: eaten, expectedRecipeRevision: recipeRevision)
    }
}
