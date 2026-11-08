using Microsoft.AspNetCore.Antiforgery;

namespace RecipeManager.Api.Security;

public sealed class ApiCsrfMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IAntiforgery antiforgery)
    {
        var request = context.Request;
        if (request.Path.StartsWithSegments("/api/v1")
            && !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) && !HttpMethods.IsOptions(request.Method))
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                await Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request verification failed",
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = "invalid_csrf",
                        ["traceId"] = context.TraceIdentifier,
                        ["errors"] = Array.Empty<object>(),
                        ["conflicts"] = Array.Empty<object>()
                    }).ExecuteAsync(context);
                return;
            }
        }

        await next(context);
    }
}
