using Microsoft.EntityFrameworkCore;
using RecipeManager.Infrastructure.Persistence;

namespace RecipeManager.Api.Health;

public sealed record HealthStatus(string Status);
public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/health/live", () => TypedResults.Ok(new HealthStatus("live"))).AllowAnonymous().WithName("GetLiveness");
        app.MapGet("/health/ready", async (RecipeManagerDbContext database, CancellationToken ct) =>
        {
            try
            {
                if (await database.Database.CanConnectAsync(ct) && !(await database.Database.GetPendingMigrationsAsync(ct)).Any())
                    return Results.Ok(new HealthStatus("ready"));
            }
            catch (Exception error) when (error is not OperationCanceledException) { /* Return no database details. */ }
            return Results.Json(new HealthStatus("unavailable"), statusCode: StatusCodes.Status503ServiceUnavailable);
        }).AllowAnonymous().WithName("GetReadiness").Produces<HealthStatus>().Produces<HealthStatus>(503);
    }
    public static void UseAngularClient(this WebApplication app)
    {
        var webRoot = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
        if (Directory.Exists(webRoot)) app.UseStaticFiles();
        // A catch-all endpoint can compete with optional-body API routes or synthesize a 405
        // for deliberately absent registration routes. Serve only unmatched browser pages here.
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path;
            var page = context.GetEndpoint() is null && (context.Request.Method is "GET" or "HEAD")
                && !Path.HasExtension(path.Value) && !path.StartsWithSegments("/api") && !path.StartsWithSegments("/hubs")
                && !path.StartsWithSegments("/health") && !path.StartsWithSegments("/openapi") && !path.StartsWithSegments("/__maintenance");
            var index = Path.Combine(webRoot, "index.html");
            if (page && File.Exists(index))
            {
                context.Response.Headers.CacheControl = "no-cache";
                await Results.File(index, "text/html").ExecuteAsync(context); return;
            }
            await next(context);
        });
    }
}
