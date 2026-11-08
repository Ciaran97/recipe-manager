namespace RecipeManager.Api.Contracts;

public static class ApiStatusPages
{
    public static void UseApiStatusPages(this WebApplication app)
        => app.UseStatusCodePages(async status =>
        {
            var context = status.HttpContext;
            if (!context.Request.Path.StartsWithSegments("/api/v1")) return;
            var code = context.Response.StatusCode switch
            {
                401 => "authentication_required",
                403 => "forbidden",
                404 => "not_found",
                405 => "method_not_allowed",
                413 => "payload_too_large",
                415 => "unsupported_media",
                429 => "rate_limited",
                _ => "request_failed"
            };
            await Results.Problem(statusCode: context.Response.StatusCode, title: "The request could not be completed.",
                instance: context.Request.Path,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = code,
                    ["traceId"] = context.TraceIdentifier,
                    ["errors"] = Array.Empty<object>(),
                    ["conflicts"] = Array.Empty<object>()
                }).ExecuteAsync(context);
        });
}
