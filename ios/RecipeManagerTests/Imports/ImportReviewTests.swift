import Foundation
import Testing
import RecipeManagerAPI
@testable import RecipeManager

@MainActor struct ImportReviewTests {
    private func candidate() throws -> Components.Schemas.ImportedRecipe {
        try APIJSON.decoder().decode(Components.Schemas.ImportedRecipe.self, from: Data(#"{"title":"Rice","description":null,"defaultServings":null,"prepMinutes":null,"cookMinutes":null,"ingredients":[{"originalText":"1/3 cup rice","ingredientName":"rice","quantityText":"1/3","kind":"numeric","unit":"cup","originalUnit":"cup","preparation":null,"group":"Base","uncertain":false,"matchedIngredientId":"10000000-0000-0000-0000-000000000001","matchedIngredientName":"Rice"},{"originalText":"salt to taste","ingredientName":"salt","quantityText":"to taste","kind":"unknown","unit":null,"originalUnit":null,"preparation":null,"group":"Base","uncertain":true,"matchedIngredientId":"10000000-0000-0000-0000-000000000002","matchedIngredientName":"Salt"}],"instructions":[{"text":"Cook rice.","group":null}],"notes":null,"sourceUrl":null,"imageUrl":null,"measurementStandard":null,"suggestedTags":[],"uncertaintyFields":["measurementStandard","defaultServings","ingredients[1].unit"]}"#.utf8))
    }
    @Test func missingSourceFieldsStayUnsetAndPreventCommitInsteadOfUsingManualDefaults() throws {
        let model = RecipeEditorViewModel(selection: ServerSelection(credentials: MemoryCredentials(), persist: { _ in }))
        model.applyImported(try candidate())
        #expect(model.draft.measurementStandard == nil)
        #expect(model.draft.defaultServings.isEmpty)
        #expect(model.draft.ingredients[1].unit == nil)
        #expect(model.draft.ingredients[1].quantityText == "to taste")
        #expect(throws: RecipeEditorFailure.self) { try model.makeWrite() }
        #expect(model.draft.groups.count == 1)
        #expect(model.draft.ingredients[0].groupId == model.draft.ingredients[1].groupId)
    }
    @Test func explicitCorrectionsPreserveFractionsAndUnknownQuantitiesWithoutClientCalculations() throws {
        let model = RecipeEditorViewModel(selection: ServerSelection(credentials: MemoryCredentials(), persist: { _ in }))
        model.applyImported(try candidate()); model.draft.defaultServings = "2"; model.draft.measurementStandard = .AU
        model.draft.acknowledgeUncertain = true
        let request = try model.makeWrite()
        #expect(request.ingredients[0].quantityText == "1/3"); #expect(request.ingredients[0].unit == .cup)
        #expect(request.ingredients[1].kind == .unknown); #expect(request.ingredients[1].quantityText == "to taste")
        #expect(model.draft.ingredients[1].unit == nil); #expect(request.ingredients[1].unit == .item)
        #expect(model.originalLines[model.draft.ingredients[1].id] == "salt to taste")
    }
    @Test func numericQuantityStillRequiresAnExplicitUnit() throws {
        let model = RecipeEditorViewModel(selection: ServerSelection(credentials: MemoryCredentials(), persist: { _ in }))
        model.applyImported(try candidate()); model.draft.defaultServings = "2"; model.draft.measurementStandard = .AU
        model.draft.acknowledgeUncertain = true; model.draft.ingredients[0].unit = nil
        #expect(throws: RecipeEditorFailure.self) { try model.makeWrite() }
    }

}
