import Foundation
import Observation
import CryptoKit
import RecipeManagerAPI
import ShareHandoff

enum RecipeImportMethod: String, CaseIterable { case url, text }

@Observable @MainActor final class ImportReviewViewModel {
    var method: RecipeImportMethod = .url
    var input = ""
    var aiEnabled = false
    var settingsLoaded = false
    var busy = false
    var error: String?
    var notice: String?
    var draft: Components.Schemas.ImportDraftRead?
    var selectedCandidate: Int?
    var savedRecipe: Components.Schemas.RecipeRead?
    var uncertainSave = false
    let editor: RecipeEditorViewModel
    var isCapture: Bool { capture != nil }
    var dirty: Bool { savedRecipe == nil && (!input.isEmpty || draft != nil || editor.dirty) }
    var candidates: [Components.Schemas.ImportedRecipe] { draft?.extraction?.recipes ?? [] }
    private let selection: ServerSelection
    private let requests: ScopedAPI
    private let context: UUID
    private let store: HandoffStore?
    private var capture: HandoffEnvelope?
    private var intentContext: UUID?
    private var etag: String?
    private var sourceKey = UUID()
    private var sourceFingerprint: Data?
    private var commitKey = UUID()
    private var operation = UUID()
    private var processing: Task<Void, Error>?
    private struct Attempt {
        let draft: Components.Schemas.ImportDraftRead
        let etag: String
        let review: Components.Schemas.ImportReview
        let retryKey: UUID
    }
    private var pending: Attempt?

    init(selection: ServerSelection, intent: PendingImportIntent? = nil, store: HandoffStore? = nil) {
        self.selection = selection; requests = ScopedAPI(selection: selection); context = selection.sessionIdentity
        editor = RecipeEditorViewModel(selection: selection)
        self.capture = intent?.capture; intentContext = intent?.sessionIdentity
        self.store = store ?? (try? HandoffStore(directory: HandoffContainer.directory()))
        if let capture = intent?.capture { method = capture.kind == .url ? .url : .text; input = capture.content; sourceKey = capture.idempotencyKey }
        editor.importedSave = { [weak self] request in
            guard let self else { throw SessionFailure.sessionChanged }
            return try await self.commit(request)
        }
    }
    func loadSettings() async {
        do {
            let result = try await requests.send(path: "/api/v1/household/settings")
            aiEnabled = try APIJSON.decoder().decode(Components.Schemas.HouseholdSettingsRead.self, from: result.data).ai.enabled
        } catch { self.error = message(error) }
        settingsLoaded = true
    }
    func start() async {
        guard !busy, !editor.busy else { return }
        let current = UUID(); operation = current; busy = true; error = nil; notice = nil
        let task = Task { try await self.extract() }; processing = task
        defer { if operation == current { busy = false; processing = nil } }
        do { try await task.value }
        catch { if operation == current { self.error = message(error) } }
    }
    func cancelExtraction() {
        guard processing != nil else { return }
        operation = UUID(); processing?.cancel(); processing = nil; busy = false
        error = nil; notice = "Import cancelled. Your input is retained."
    }
    private func extract() async throws {
        try checkContext()
        // Resolve a durable result before local source/draft expiry or optional provider checks.
        if capture != nil, try await resumeKnownCapture() { return }
        guard !input.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { throw RecipeEditorFailure.invalid("Enter a recipe URL or paste recipe text.") }
        guard method == .url ? input.utf16.count <= 2048 : input.utf8.count <= 100 * 1024 else {
            throw RecipeEditorFailure.invalid("Use a URL up to 2,048 characters or pasted text up to 100 KiB. Your input is retained.")
        }
        if let capture {
            guard method.rawValue == capture.kind.rawValue, Data(input.utf8) == Data(capture.content.utf8) else { throw HandoffFailure.identityConflict }
            try capture.validate()
            let hash = SHA256.hash(data: Data((capture.kind.rawValue + "\0" + capture.content).utf8)).map { String(format: "%02x", $0) }.joined()
            let registration = Components.Schemas.RegisterImportHandoff(handoffId: capture.handoffId.uuidString, capturedAt: capture.createdAt, inputHash: hash)
            let response = try await requests.send(path: "/api/v1/import-handoffs", method: "POST", body: try APIJSON.encoder().encode(registration))
            let state = try APIJSON.decoder().decode(Components.Schemas.ImportHandoffRead.self, from: response.data)
            if let result = state.result { try await finish(result.recipeId); return }
            guard state.state == .Open else { throw SessionFailure.invalidResponse }
        }
        if method == .text && !aiEnabled { throw RecipeEditorFailure.invalid("AI text importing is not configured. URL and manual recipe entry remain available; shared content is retained.") }
        let fingerprint = Data((method.rawValue + "\0" + input).utf8)
        if capture == nil, fingerprint != sourceFingerprint { sourceKey = UUID(); sourceFingerprint = fingerprint }
        if let capture, let binding = capture.binding {
            guard let store else { throw HandoffFailure.persistence }
            self.capture = try await store.markSubmitted(capture.handoffId, destination: binding)
            try checkContext(); try Task.checkCancellation()
        }
        let body: Data
        if method == .url { body = try APIJSON.encoder().encode(Components.Schemas.UrlImportWrite(url: input, handoffId: capture?.handoffId.uuidString)) }
        else { body = try APIJSON.encoder().encode(Components.Schemas.TextImportWrite(text: input, handoffId: capture?.handoffId.uuidString)) }
        let response = try await requests.send(path: "/api/v1/imports/" + method.rawValue, method: "POST", body: body, retryKey: sourceKey)
        try checkContext(); try Task.checkCancellation()
        let result = try APIJSON.decoder().decode(Components.Schemas.ImportStartResult.self, from: response.data)
        if let committed = result.commitResult { try await finish(committed.recipeId); return }
        guard let payload = result.draft else { throw SessionFailure.invalidResponse }
        let incoming: Components.Schemas.ImportDraftRead = try convert(payload)
        try await adopt(incoming, etag: response.etag)
    }
    func resumeCapture() async {
        guard capture != nil, !busy else { return }
        busy = true; defer { busy = false }
        do { try checkContext(); _ = try await resumeKnownCapture() }
        catch { self.error = message(error) }
    }
    private func resumeKnownCapture() async throws -> Bool {
        guard let capture else { return false }
        try checkContext()
        let state: Components.Schemas.ImportHandoffRead
        do { state = try APIJSON.decoder().decode(Components.Schemas.ImportHandoffRead.self, from: await requests.send(path: "/api/v1/import-handoffs/" + capture.handoffId.uuidString).data) }
        catch let rejected as APIRejection where rejected.status == 404 { return false }
        try Task.checkCancellation()
        if let result = state.result { try await finish(result.recipeId); return true }
        guard state.state == .Open else { throw SessionFailure.invalidResponse }
        guard let id = capture.serverDraftId else { return false }
        do {
            let response = try await requests.send(path: "/api/v1/imports/" + id.uuidString)
            try await adopt(APIJSON.decoder().decode(Components.Schemas.ImportDraftRead.self, from: response.data), etag: response.etag)
            return true
        } catch let rejected as APIRejection where rejected.status == 410 {
            notice = "The previous review expired. Import again while this capture remains valid."
            return false
        }
    }
    private func adopt(_ incoming: Components.Schemas.ImportDraftRead, etag: String?) async throws {
        try checkContext(); try Task.checkCancellation()
        guard capture == nil || UUID(uuidString: incoming.handoffId ?? "") == capture?.handoffId else { throw SessionFailure.invalidResponse }
        guard let etag else {
            let response = try await requests.send(path: "/api/v1/imports/" + incoming.id)
            guard let current = response.etag else { throw SessionFailure.invalidResponse }
            try await adopt(APIJSON.decoder().decode(Components.Schemas.ImportDraftRead.self, from: response.data), etag: current); return
        }
        if let capture, let binding = capture.binding {
            guard let store, let id = UUID(uuidString: incoming.id) else { throw HandoffFailure.persistence }
            self.capture = try await store.markSubmitted(capture.handoffId, destination: binding, draftId: id)
        }
        try checkContext(); try Task.checkCancellation()
        if let reviewed = incoming.reviewedRecipe {
            let recipe: Components.Schemas.RecipeWrite = try convert(reviewed)
            var names: [String: String] = [:]
            for id in Set(recipe.ingredients.map(\.ingredientId)) {
                do { names[id] = try APIJSON.decoder().decode(Components.Schemas.IngredientRead.self, from: await requests.send(path: "/api/v1/ingredients/" + id).data).canonicalName }
                catch let rejected as APIRejection where rejected.status == 404 { notice = "An ingredient was removed. Select a replacement before saving." }
            }
            editor.applyReviewed(recipe, ingredientNames: names); selectedCandidate = nil
        } else if let candidate = incoming.extraction?.recipes.first { editor.applyImported(candidate); selectedCandidate = 0 }
        else { throw SessionFailure.invalidResponse }
        try checkContext(); try Task.checkCancellation()
        self.draft = incoming; self.etag = etag
    }
    func selectCandidate(_ index: Int) {
        guard !busy, !editor.busy, !uncertainSave, candidates.indices.contains(index) else { return }
        editor.applyImported(candidates[index]); selectedCandidate = index
    }
    func reload() async {
        guard let draft, !busy, !editor.busy, !uncertainSave else { return }
        busy = true; error = nil; defer { busy = false }
        do {
            let response = try await requests.send(path: "/api/v1/imports/" + draft.id)
            try await adopt(APIJSON.decoder().decode(Components.Schemas.ImportDraftRead.self, from: response.data), etag: response.etag)
        } catch { self.error = message(error) }
    }
    private func commit(_ recipe: Components.Schemas.RecipeWrite) async throws -> Components.Schemas.RecipeRead {
        guard !uncertainSave, let draft, let etag, let standard = recipe.measurementStandard else {
            throw RecipeEditorFailure.invalid("Confirm the source measures, or check the previous save before changing this review.")
        }
        try checkContext()
        let review = Components.Schemas.ImportReview(recipe: recipe, confirmedMeasurementStandard: .init(rawValue: standard.rawValue), reviewConfirmed: true)
        let attempt = Attempt(draft: draft, etag: etag, review: review, retryKey: commitKey); pending = attempt
        do { let recipe = try await submit(attempt); pending = nil; return recipe }
        catch {
            uncertainSave = uncertain(error); editor.submissionUncertain = uncertainSave
            if !uncertainSave { pending = nil }
            throw error
        }
    }
    private func submit(_ attempt: Attempt) async throws -> Components.Schemas.RecipeRead {
        try checkContext()
        let result: Components.Schemas.ImportCommitResult
        if let handoff = attempt.draft.handoffId {
            let state = try APIJSON.decoder().decode(Components.Schemas.ImportHandoffRead.self, from: await requests.send(path: "/api/v1/import-handoffs/" + handoff).data)
            if let original = state.result { return try await finish(original.recipeId) }
            guard state.state == .Open else { throw SessionFailure.invalidResponse }
            let body = Components.Schemas.CommitImport(draftId: attempt.draft.id, expectedDraftRevision: attempt.draft.revision, review: attempt.review)
            result = try APIJSON.decoder().decode(Components.Schemas.ImportCommitResult.self, from: await requests.send(path: "/api/v1/import-handoffs/\(handoff)/commit", method: "POST", body: try APIJSON.encoder().encode(body)).data)
        } else {
            result = try APIJSON.decoder().decode(Components.Schemas.ImportCommitResult.self, from: await requests.send(path: "/api/v1/imports/\(attempt.draft.id)/commit", method: "POST", body: try APIJSON.encoder().encode(attempt.review), etag: attempt.etag, retryKey: attempt.retryKey).data)
        }
        return try await finish(result.recipeId)
    }
    @discardableResult private func finish(_ id: String) async throws -> Components.Schemas.RecipeRead {
        let recipe = try APIJSON.decoder().decode(Components.Schemas.RecipeRead.self, from: await requests.send(path: "/api/v1/recipes/" + id).data)
        try checkContext(); try Task.checkCancellation()
        if let capture, let store {
            do { try await store.discard(capture.handoffId) }
            catch { notice = "The recipe was saved. Its local capture could not be removed; reopening it will resolve the same saved recipe." }
        }
        try checkContext(); savedRecipe = recipe; uncertainSave = false; editor.submissionUncertain = false
        return recipe
    }
    func retrySave() async {
        guard let pending, !busy, !editor.busy else { return }
        busy = true; error = nil; defer { busy = false }
        do { editor.acceptImported(try await submit(pending)); self.pending = nil }
        catch { self.error = message(error); uncertainSave = uncertain(error); editor.submissionUncertain = uncertainSave; if !uncertainSave { self.pending = nil } }
    }
    func discardDraft() async -> Bool {
        guard !busy, !editor.busy else { return false }
        if capture != nil || uncertainSave { return true } // Captures remain bound and resumable; no unbound fallback.
        do { if let draft, let etag { _ = try await requests.send(path: "/api/v1/imports/" + draft.id, method: "DELETE", etag: etag) }; return true }
        catch let rejected as APIRejection where rejected.status == 410 { return true }
        catch { self.error = message(error); return false }
    }
    private func checkContext() throws {
        guard selection.sessionIdentity == context else { throw SessionFailure.sessionChanged }
        if let capture {
            guard intentContext == context, let binding = capture.binding,
                selection.origin?.key == binding.origin, selection.user?.id == binding.userId,
                selection.user?.householdId == binding.householdId else { throw SessionFailure.sessionChanged }
        }
    }
    private func uncertain(_ error: Error) -> Bool {
        if let rejected = error as? APIRejection { return rejected.status >= 500 }
        if let failure = error as? SessionFailure { if case .sessionChanged = failure { return false }; if case .authenticationRequired = failure { return false } }
        return true
    }
    private func message(_ error: Error) -> String {
        if case RecipeEditorFailure.invalid(let message) = error { return message }
        if let failure = error as? HandoffFailure {
            switch failure {
            case .expired: return "This capture expired. A committed result is checked before starting a new extraction."
            case .identityConflict: return "This capture must retain its original content and import method."
            default: return "The capture could not be updated on this device. Its original content is retained; retry without starting an unbound import."
            }
        }
        return apiErrorMessage(error)
    }
    private func convert<T: Encodable, U: Decodable>(_ source: T) throws -> U { try APIJSON.decoder().decode(U.self, from: APIJSON.encoder().encode(source)) }
}
