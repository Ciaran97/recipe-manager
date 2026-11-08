import Foundation
import Testing
import SwiftUI
@testable import RecipeManager

@MainActor struct RecipeEditorTests {
    private func model() -> RecipeEditorViewModel { RecipeEditorViewModel(selection: ServerSelection(credentials: MemoryCredentials(), persist: { _ in })) }
    private func fill(_ model: RecipeEditorViewModel) {
        model.draft.title = "Rice bowls"; model.draft.defaultServings = "3"
        model.draft.ingredients[0].ingredientId = "10000000-0000-0000-0000-000000000001"; model.draft.ingredients[0].ingredientName = "Rice"; model.draft.ingredients[0].quantityText = "1/3"
        model.draft.instructions[0].text = "Cook rice."
    }
    @Test func originalFractionsAndAustralianStandardArePreservedInTheGeneratedWrite() throws {
        let model = model(); fill(model); let request = try model.makeWrite()
        #expect(request.defaultServings == "3"); #expect(request.measurementStandard == .AU)
        #expect(request.ingredients[0].quantityText == "1/3"); #expect(request.ingredients[0].unit == .g)
    }
    @Test func unresolvedQuantitiesRequireReviewAndRemainUnresolved() async {
        let model = model(); fill(model); model.draft.ingredients[0].kind = .unknown; model.draft.ingredients[0].quantityText = "to taste"
        await model.save(); #expect(model.error?.contains("Review") == true); #expect(model.savedRecipe == nil)
        #expect(model.draft.ingredients[0].quantityText == "to taste")
    }
    @Test func duplicateAndMovePreserveValuesWithIndependentRowIdentities() {
        let model = model(); fill(model); let original = model.draft.ingredients[0]
        model.duplicateIngredient(original.id); let copy = model.draft.ingredients[1]
        #expect(copy.id != original.id); #expect(copy.quantityText == "1/3"); #expect(copy.ingredientId == original.ingredientId)
        model.moveIngredient(copy.id, by: -1); #expect(model.draft.ingredients[0].id == copy.id); #expect(model.draft.ingredients[1].id == original.id)
    }
    @Test func changingVisibleNameClearsItsPreviousCanonicalIdentity() {
        let model = model(); fill(model); model.setIngredientName(model.draft.ingredients[0].id, "Beef mince"); model.cancelSearch()
        #expect(model.draft.ingredients[0].ingredientId.isEmpty)
    }
    @Test func committingAnUnchangedNameDuringFocusChangeRetainsItsCanonicalIdentity() {
        let model = model(); fill(model)
        let original = model.draft.ingredients[0]
        model.setIngredientName(original.id, original.ingredientName); model.cancelSearch()
        #expect(model.draft.ingredients[0].ingredientId == original.ingredientId)
    }
    @Test func ingredientBindingTracksIdentityAndIgnoresDeletedRows() {
        let model = model(); fill(model)
        let original = model.draft.ingredients[0]
        let rows = Binding(get: { model.draft.ingredients }, set: { model.draft.ingredients = $0 })
        let binding = bindingForRecipeRow(original, in: rows)
        model.duplicateIngredient(original.id)
        let copy = model.draft.ingredients[1]
        model.moveIngredient(copy.id, by: -1)
        binding.quantityText.wrappedValue = "2"
        #expect(model.draft.ingredients[0].id == copy.id)
        #expect(model.draft.ingredients[0].quantityText == "1/3")
        #expect(model.draft.ingredients[1].id == original.id)
        #expect(model.draft.ingredients[1].quantityText == "2")
        model.removeIngredient(original.id)
        #expect(binding.kind.wrappedValue == original.kind)
        binding.quantityText.wrappedValue = "9"
        #expect(model.draft.ingredients.count == 1)
        #expect(model.draft.ingredients[0].id == copy.id)
        #expect(model.draft.ingredients[0].quantityText == "1/3")
    }
    @Test func removedGroupsAndInstructionsRemainSafeForDisappearingControls() {
        let model = model(); fill(model)
        let group = RecipeGroupDraft(title: "Sauce")
        model.draft.groups = [group]
        let groups = Binding(get: { model.draft.groups }, set: { model.draft.groups = $0 })
        let groupBinding = bindingForRecipeRow(group, in: groups)
        model.removeGroup(group.id)
        #expect(groupBinding.title.wrappedValue == "Sauce")
        groupBinding.title.wrappedValue = "Late edit"
        #expect(model.draft.groups.isEmpty)
        let step = model.draft.instructions[0]
        let steps = Binding(get: { model.draft.instructions }, set: { model.draft.instructions = $0 })
        let instructionBinding = bindingForRecipeRow(step, in: steps)
        model.removeInstruction(step.id)
        #expect(instructionBinding.text.wrappedValue == "Cook rice.")
        instructionBinding.text.wrappedValue = "Late edit"
        #expect(model.draft.instructions.isEmpty)
    }

}
