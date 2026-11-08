import Foundation
import Testing
import RecipeManagerAPI
@testable import RecipeManager

struct PlannerContractTests {
    @Test func deletionCanCarryExplicitConsumedStateConfirmation() throws {
        let body = Operations.DeleteScheduledMeal.Input.Body.json(Components.Schemas.DeleteMealWrite(sourceRevision: "3", confirmConsumedRemoval: true))
        switch body {
        case .json(let payload):
            let encoded = try APIJSON.encoder().encode(payload)
            let values = try #require(JSONSerialization.jsonObject(with: encoded) as? [String: Any])
            #expect(values["sourceRevision"] as? String == "3")
            #expect(values["confirmConsumedRemoval"] as? Bool == true)
        }
    }
    @Test func clearingSourceActualsCanCarryExplicitDependentCorrections() throws {
        let dependent = Components.Schemas.DependentResolution(mealId: UUID().uuidString, mealRevision: "2", action: .edit, reservedRemainder: "0", clearConsumption: true)
        let body = Operations.ClearCookingActuals.Input.Body.json(Components.Schemas.ClearActualsWrite(dependentResolutions: [dependent]))
        switch body {
        case .json(let payload):
            #expect(payload.dependentResolutions?.first?.reservedRemainder == "0")
            #expect(payload.dependentResolutions?.first?.clearConsumption == true)
            #expect(payload.dependentResolutions?.first?.consumedPortions == nil)
        }
    }
}
