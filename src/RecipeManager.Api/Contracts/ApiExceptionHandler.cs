using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Imports;
using RecipeManager.Core.Modules.Ingredients;
using RecipeManager.Core.Modules.Leftovers;
using RecipeManager.Core.Modules.MealPlanning;
using RecipeManager.Core.Modules.Recipes;
using RecipeManager.Core.Modules.Shopping;
using RecipeManager.Core.Shared;
using RecipeManager.Infrastructure.Files;
using RecipeManager.Infrastructure.Persistence;

namespace RecipeManager.Api.Contracts;

public sealed class ApiExceptionHandler(IProblemDetailsService problems, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var (status, code) = exception switch
        {
            BadHttpRequestException badRequest => (badRequest.StatusCode, "invalid_request"),
            HouseholdAccessDeniedException => (403, "forbidden"),
            ResourceMissingException => (404, "not_found"),
            RevisionConflictException => (412, "stale_revision"),
            HouseholdConflictException conflict => (409, conflict.Code),
            CatalogueConflictException conflict => (409, conflict.Code),
            ImportFailureException failure when failure.Code == "import_timeout" => (504, failure.Code),
            ImportFailureException failure when failure.Code is "import_fetch_failed" or "import_provider_failed" => (502, failure.Code),
            ImportFailureException failure when failure.Code is "import_ai_disabled" or "import_provider_configuration" => (409, failure.Code),
            ImportFailureException failure => (422, failure.Code),
            ImportStateException import when import.Code is "handoff_expired" or "draft_expired" or "committed_recipe_deleted" => (410, import.Code),
            ImportStateException import when import.Code == "handoff_required" => (422, import.Code),
            ImportStateException import => (409, import.Code),
            RecipeConflictException conflict => (409, conflict.Code),
            PlannerConflictException conflict => (409, conflict.Code),
            AllocationConflictException => (409, "leftovers_overallocated"),
            RecipeSnapshotConflictException => (412, "stale_revision"),
            ShoppingSourceConflictException => (409, "shopping_review_required"),
            ApiContractException contract => (contract.Status, contract.Code),
            RecipeImageException image => (image.Status, image.Code),
            DomainValidationException => (422, "validation_failed"),
            RetryPayloadConflictException => (409, "idempotency_payload_changed"),
            DbUpdateConcurrencyException => (412, "stale_revision"),
            TransactionAccessException => (401, "session_invalid"),
            _ => (500, "unexpected_error")
        };
        logger.LogWarning("API request failed: {Code} {TraceId}", code, context.TraceIdentifier);
        context.Response.StatusCode = status;
        var detail = new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = status,
            Title = status == 500 ? "An unexpected error occurred." : "The request could not be completed.",
            Type = "about:blank",
            Instance = context.Request.Path
        };
        detail.Extensions["code"] = code;
        detail.Extensions["traceId"] = context.TraceIdentifier;
        detail.Extensions["errors"] = exception is DomainValidationException validation
            ? new[] { new { path = validation.ParamName, code = validation.Code, message = validation.UserMessage } }
            : [];
        if (exception is ImportStateException { Result: { } original }) detail.Extensions["commitResult"] = original;
        detail.Extensions["conflicts"] = exception is PlannerConflictException planner
            ? planner.MealIds.Select(id => (object)new { resourceId = id, reason = planner.Message }).ToArray()
            : Array.Empty<object>();
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = detail });
    }
}
