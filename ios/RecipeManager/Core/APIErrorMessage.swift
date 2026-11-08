import Foundation

func apiErrorMessage(_ error: Error) -> String {
    if let rejected = error as? APIRejection {
        if let fields = rejected.problem?.errors, !fields.isEmpty { return fields.map(\.message).joined(separator: " ") }
        switch rejected.problem?.code {
        case "import_provider_failed": return "The AI provider could not process this recipe. Your input is retained; try again or enter it manually."
        case "import_provider_invalid_response": return "The AI provider returned invalid source data. Your original input is retained."
        case "import_ai_disabled": return "AI text importing is not configured. URL and manual entry remain available."
        case "import_provider_configuration": return "Ask an administrator to check the household AI provider configuration."
        case "import_timeout": return "Recipe processing timed out. Your input is retained; retry or enter it manually."
        case "import_unsafe_url": return "Choose a public HTTP or HTTPS recipe URL without embedded credentials."
        case "import_fetch_failed": return "The recipe website could not be reached. Your input is retained."
        case "import_no_recipe": return "No structured recipe was found. Use another recipe URL or enter it manually."
        case "import_unsupported_content", "import_invalid_document": return "This URL did not return a readable recipe page."
        case "import_limit": return "The input or response is too large. Use an individual recipe page or shorter text."
        case "draft_expired": return "The review expired or is unavailable. Keep the original input and start a new explicit extraction."
        case "handoff_expired": return "This shared capture expired. Share it again to start a new import."
        case "handoff_already_committed": return "This shared recipe was already saved. Check its original import result."
        case "handoff_input_changed": return "This capture is bound to its original content and import method."
        case "committed_recipe_deleted": return "The recipe saved by this import was deleted. It will not be recreated automatically."
        case "setup_already_completed": return "Setup is already complete. Sign in with your account."
        case "last_administrator": return "The household must retain an administrator."
        case "invitation_already_used": return "This invitation has already been used."
        case "sign_out_before_accepting": return "Sign out of the current account before accepting this invitation."
        case "leftovers_overallocated": return "These portions exceed the available leftovers. Review the related meals."
        case "invalid_leftover_chronology": return "Schedule leftover meals after their source cooking event."
        case "source_not_confirmed": return "Confirm source cooking quantities before recording leftover consumption."
        case "dependent_leftovers": return "This cooking event has dependent leftover meals. Resolve them before removing it."
        case "slot_archived": return "Choose an active meal slot for a new meal."
        case "consumption_removal_confirmation": return "Explicitly confirm removing recorded consumption before deleting this meal."
        case "different_meal_type": return "Reload this meal before using a different type of editor."
        case "name_conflict": return "This name is already in use. Choose another name."
        case "ingredient_referenced": return "This ingredient is used by a recipe, meal or shopping list. Correct its name or aliases instead."
        case "recipe_referenced": return "This recipe is used by a meal or shopping list. Archive it instead."
        case "image_referenced": return "This image is still used by a recipe or cooking snapshot."
        case "idempotency_payload_changed": return "The original save may already have succeeded. Reload and check it before starting a changed save."
        case "shopping_review_required": return "Requirements changed. Preview them again before applying; your edits are retained."
        case "stale_revision": return "This changed on another device. Reload before saving."
        case "rate_limited": return "Too many attempts. Wait a minute and try again."
        case "session_drain_pending": return "A connection is still closing. Try again shortly."
        default: return "Could not complete this request. Reload and try again."
        }
    }
    if let failure = error as? SessionFailure {
        switch failure {
        case .authenticationRequired: return "Sign in again. Email or password may not have been accepted."
        case .invalidOrigin: return "Enter an HTTPS server origin without a path or embedded credentials."
        case .sessionChanged: return "The account or server changed. Retry explicitly."
        case .storage: return "Could not save the session securely. Unlock the device and try again."
        default: return "The server response could not be accepted. Check your server address."
        }
    }
    if error is CancellationError { return "The request was cancelled." }
    return "Could not reach the server. Your change was not confirmed."
}
