import Foundation
import Observation
import RecipeManagerAPI

struct SourceProposal {
    let action: String
    let draft: MealDraft
}

struct ReservationResolution: Identifiable {
    let id: String
    let revision: String
    let title: String
    var action = "keep"
    var planned: String
    var consumed: String
    var reserved: String
    var consumption = "keep"
    var date: String
    var slotId: String
    var targetId = ""
    var targetRevision: String?
    var targetSummary: String?
    var confirmRemoval = false
    init(_ read: Components.Schemas.AllocationReservationRead) {
        id = read.mealId; revision = read.mealRevision
        title = "\(read.date) · Planned \(read.allocation.plannedPortions) · Consumed \(read.allocation.consumedPortions ?? "Not confirmed") · Reserved \(read.allocation.reservedRemainder)"
        planned = read.allocation.plannedPortions; consumed = read.allocation.consumedPortions ?? "0"; reserved = read.allocation.reservedRemainder
        date = read.date; slotId = read.slotId
    }
    func write() -> Components.Schemas.DependentResolution? {
        guard let kind = Components.Schemas.DependentAction(rawValue: action) else { return nil }
        return .init(mealId: id, mealRevision: revision, action: kind,
            plannedPortions: action == "edit" || action == "reassign" ? planned : nil,
            consumedPortions: consumption == "replace" ? consumed : nil,
            reservedRemainder: action == "release" ? "0" : (action == "edit" || action == "reassign" || consumption != "keep" ? reserved : nil),
            date: action == "move" ? date : nil, slotId: action == "move" ? slotId : nil,
            sourceCookingEventId: action == "reassign" ? targetId : nil,
            confirmConsumedRemoval: confirmRemoval, clearConsumption: consumption == "clear")
    }
}

@Observable @MainActor final class CookingSourceModel {
    var source: Components.Schemas.LeftoversAvailability?
    var meal: Components.Schemas.ScheduledMealRead?
    var actualPrepared = ""
    var actualEaten = ""
    var rows: [ReservationResolution] = []
    var error: String?
    var busy = false
    var saved = false
    var resolving = false
    var kind: Components.Schemas.SourceResolutionKind = .actuals
    var preview: Components.Schemas.RecipeRefreshResult.previewPayload?
    var sourceTag: String?
    var mealTag: String?
    var needsReview = false
    var reviewed = false
    var targetWeek: String
    var targetMeals: [Components.Schemas.ScheduledMealRead] = []
    let sourceId: String
    let proposal: SourceProposal?
    private let requests: ScopedAPI
    private var initialActuals = ""
    var dirty: Bool { initialActuals != actualPrepared + "|" + actualEaten || rows.contains { $0.action != "keep" } }
    init(selection: ServerSelection, sourceId: String, proposal: SourceProposal?) {
        requests = ScopedAPI(selection: selection); self.sourceId = sourceId; self.proposal = proposal
        targetWeek = PlannerDates.monday(PlannerDates.today(timezone: "UTC"))
        if let proposal {
            resolving = true
            kind = proposal.action == "delete" ? .delete : proposal.action == "move" ? .move : .plan
        }
    }
    func load(preserve: Bool = false) async {
        await perform {
            let response = try await self.requests.send(path: "/api/v1/cooking-events/\(self.sourceId)/leftovers")
            let source = try APIJSON.decoder().decode(Components.Schemas.LeftoversAvailability.self, from: response.data)
            let oldRows = self.rows
            self.source = source; self.sourceTag = response.etag
            self.rows = source.allocations.map { read in
                var row = ReservationResolution(read)
                if preserve, let old = oldRows.first(where: { $0.id == row.id }) {
                    row.action = old.action; row.planned = old.planned; row.consumed = old.consumed; row.reserved = old.reserved
                    row.consumption = old.consumption; row.date = old.date; row.slotId = old.slotId
                    row.targetId = old.targetId; row.targetRevision = nil; row.confirmRemoval = old.confirmRemoval
                }
                return row
            }
            if !preserve {
                self.actualPrepared = source.actualPrepared ?? source.plannedPrepared
                self.actualEaten = source.actualEaten ?? source.plannedEaten
                self.initialActuals = self.actualPrepared + "|" + self.actualEaten
            }
            let mealResponse = try await self.requests.send(path: "/api/v1/meals/" + source.sourceMealId)
            let meal = try APIJSON.decoder().decode(Components.Schemas.ScheduledMealRead.self, from: mealResponse.data)
            guard meal.cookingEvent?.revision == source.sourceRevision else { throw PlannerPresentationFailure.changed }
            self.meal = meal; self.mealTag = mealResponse.etag; self.preview = nil
            if preserve { self.needsReview = true; self.reviewed = false }
            else { self.needsReview = false; self.reviewed = false; self.resolving = self.proposal != nil }
        }
    }
    func actuals(clear: Bool = false) async {
        guard !needsReview || reviewed else { return }
        kind = clear ? .clearActuals : .actuals
        await perform(resolveOnConflict: true) {
            let body = clear ? try APIJSON.encoder().encode(Components.Schemas.ClearActualsWrite()) : try APIJSON.encoder().encode(Components.Schemas.ActualsWrite(actualPrepared: self.actualPrepared, actualEaten: self.actualEaten))
            _ = try await self.requests.send(path: "/api/v1/cooking-events/\(self.sourceId)/actuals", method: clear ? "DELETE" : "PUT", body: body, etag: self.sourceTag)
            self.saved = true
        }
    }
    func findTargets() async {
        await perform {
            let response = try await self.requests.send(path: "/api/v1/meals?from=\(self.targetWeek)&to=\(PlannerDates.add(self.targetWeek, days: 6))")
            self.targetMeals = try APIJSON.decoder().decode(Components.Schemas.MealCalendarRead.self, from: response.data).items.filter { $0.cookingEvent != nil && $0.cookingEvent?.id != self.sourceId }
        }
    }
    func selectTarget(rowId: String, sourceId: String) async {
        await perform {
            let response = try await self.requests.send(path: "/api/v1/cooking-events/\(sourceId)/leftovers")
            let target = try APIJSON.decoder().decode(Components.Schemas.LeftoversAvailability.self, from: response.data)
            guard let index = self.rows.firstIndex(where: { $0.id == rowId }) else { return }
            self.rows[index].targetId = target.sourceCookingEventId; self.rows[index].targetRevision = target.sourceRevision
            self.rows[index].targetSummary = "\(target.date) · \(target.recipeTitle) · \(target.availablePortions) available (\(target.basis))"
        }
    }
    func resolve() async {
        guard !needsReview || reviewed, let source else { return }
        await perform {
            var expected: [Components.Schemas.ExpectedSourceVersion] = [.init(sourceCookingEventId: source.sourceCookingEventId, revision: source.sourceRevision)]
            for row in self.rows where row.action == "reassign" {
                guard let revision = row.targetRevision, !row.targetId.isEmpty else { throw PlannerPresentationFailure.targetRequired }
                if !expected.contains(where: { $0.sourceCookingEventId == row.targetId }) { expected.append(.init(sourceCookingEventId: row.targetId, revision: revision)) }
                else if expected.first(where: { $0.sourceCookingEventId == row.targetId })?.revision != revision { throw PlannerPresentationFailure.changed }
            }
            let plan = self.proposal?.draft
            let body = Components.Schemas.SourceResolutionWrite(kind: self.kind, expectedSources: expected, dependents: self.rows.compactMap { $0.write() },
                plannedPrepared: self.kind == .plan ? plan?.prepared : nil, plannedEaten: self.kind == .plan ? plan?.eaten : nil,
                actualPrepared: self.kind == .actuals ? self.actualPrepared : nil, actualEaten: self.kind == .actuals ? self.actualEaten : nil,
                date: self.kind == .plan || self.kind == .move ? plan?.date : nil, slotId: self.kind == .plan || self.kind == .move ? plan?.slotId : nil,
                recipeId: self.kind == .plan && plan?.recipeId != source.sourceRecipeId ? plan?.recipeId : nil,
                expectedRecipeRevision: self.kind == .plan && plan?.recipeId != source.sourceRecipeId ? plan?.recipeRevision : nil)
            _ = try await self.requests.send(path: "/api/v1/cooking-events/\(self.sourceId)/resolve", method: "POST", body: try APIJSON.encoder().encode(body), etag: self.sourceTag)
            self.saved = true
        }
    }
    func refresh(apply: Bool) async {
        guard !dirty, !resolving, !needsReview else { error = "Save or discard quantity changes before refreshing the recipe."; return }
        await perform {
            guard let source = self.source, let meal = self.meal else { return }
            let revision: String
            if apply { guard let preview = self.preview else { return }; revision = preview.recipeRevision }
            else {
                let response = try await self.requests.send(path: "/api/v1/recipes/" + source.sourceRecipeId)
                revision = try APIJSON.decoder().decode(Components.Schemas.RecipeRead.self, from: response.data).revision
            }
            let body = Components.Schemas.RecipeRefreshWrite(mode: apply ? .apply : .preview, expectedRecipeRevision: revision,
                expectedEventRevision: apply ? self.preview?.eventRevision : meal.cookingEvent?.revision)
            let response = try await self.requests.send(path: "/api/v1/meals/\(meal.id)/recipe-refresh", method: "POST", body: try APIJSON.encoder().encode(body), etag: self.mealTag)
            let result = try APIJSON.decoder().decode(Components.Schemas.RecipeRefreshResult.self, from: response.data)
            if apply { self.saved = true } else { self.preview = result.preview }
        }
    }
    private func perform(resolveOnConflict: Bool = false, _ action: () async throws -> Void) async {
        guard !busy else { return }; busy = true; error = nil; defer { busy = false }
        do { try await action() } catch {
            self.error = (error as? PlannerPresentationFailure)?.message ?? apiErrorMessage(error)
            if let failure = error as? PlannerPresentationFailure, case .changed = failure { needsReview = true; reviewed = false }
            if let rejection = error as? APIRejection {
                if rejection.status == 409 && resolveOnConflict { resolving = true }
                if rejection.status == 412 { needsReview = true; reviewed = false; preview = nil }
            }
        }
    }
}

enum PlannerPresentationFailure: Error {
    case changed, targetRequired
    var message: String {
        switch self {
        case .changed: "The cooking event changed. Reload current balances and review your choices."
        case .targetRequired: "Select and review a target cooking event for every reassignment."
        }
    }
}
