import Foundation
import Testing
import RecipeManagerAPI
@testable import RecipeManager

struct ShoppingContractTests {
    @Test func nestedOverridesKeepTheirExactQuantityAndDimension() throws {
        let data = Data("""
        {"quantityText":"5","kind":"numeric","unit":"g","measurementStandard":"AU",
        "exact":{"numerator":"5","denominator":"1"},"dimension":"Mass","displayText":"5 g","isApproximate":false}
        """.utf8)
        let value = try APIJSON.decoder().decode(Components.Schemas.ShoppingItemRead.quantityOverridePayload.self, from: data)
        #expect(value.exact?.numerator == "5"); #expect(value.dimension?.rawValue == "Mass")
    }
    @Test func nativePreviewKeepsItsExplicitActualBasis() throws {
        let request = Components.Schemas.ShoppingPreviewWrite(basis: .actual)
        let data = try APIJSON.encoder().encode(request)
        let json = try #require(JSONSerialization.jsonObject(with: data) as? [String: Any])
        #expect(json["basis"] as? String == "actual")
    }
    @Test func unresolvedShoppingRowsDecodeWithoutInventingADimensionOrQuantity() throws {
        let data = Data("""
        {"id":"10000000-0000-0000-0000-000000000001","key":"unknown:source:row",
        "ingredientId":"20000000-0000-0000-0000-000000000001","label":"Salt",
        "categoryId":null,"categoryName":"Pantry","dimension":null,"baselineExact":null,
        "effectiveExact":null,"quantityOverride":null,"hasOverride":false,"displayText":"to taste",
        "isApproximate":false,"unresolved":true,"additionalAmountUnresolved":false,
        "purchased":false,"manual":false,"removed":false,"position":0,"contributions":[]}
        """.utf8)
        let row = try APIJSON.decoder().decode(Components.Schemas.ShoppingItemRead.self, from: data)
        #expect(row.dimension == nil); #expect(row.effectiveExact == nil)
        #expect(row.unresolved); #expect(row.displayText == "to taste")
    }
}
