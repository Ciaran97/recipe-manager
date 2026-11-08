using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace RecipeManager.Testing;

// A nonshipping, loopback-only compatible provider. The API child still uses the production
// HTTP adapter and ordinary administrator configuration; no production test endpoint is added.
internal static class ImportProviderFixture
{
    public const string RecipeText = "Imported rice\nServes 2\n1/2 cup rice\nsalt to taste\nCook rice and season.";
    public static async Task<WebApplication> StartAsync(string directory, CancellationToken ct)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1024 * 1024);
        var app = builder.Build();
        app.MapPost("/v1/chat/completions", async (HttpRequest request, CancellationToken cancellationToken) =>
        {
            using var body = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
            var source = body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString();
            if (source == "FAIL_PROVIDER") return Results.StatusCode(503);
            if (source != RecipeText) return Results.BadRequest();
            var recipe = new
            {
                recipes = new[] { new {
                    title = "Imported rice", description = (string?)null, servingsText = "Serves 2",
                    prepTimeText = (string?)null, cookTimeText = (string?)null, notes = (string?)null,
                    measurementStandard = (string?)null, measurementStandardEvidence = (string?)null,
                    ingredients = new[] { new { sourceText = "1/2 cup rice", group = (string?)null }, new { sourceText = "salt to taste", group = (string?)null } },
                    instructions = new[] { new { sourceText = "Cook rice and season.", group = (string?)null } }
                } }
            };
            return Results.Json(new { choices = new[] { new { finish_reason = "stop", message = new { role = "assistant", content = JsonSerializer.Serialize(recipe) } } } });
        });
        await app.StartAsync(ct);
        foreach (var (name, content) in new[] { ("ai-origin", app.Urls.Single().TrimEnd('/') + "/v1"), ("ai-recipe-text", RecipeText) })
        {
            var file = Path.Combine(directory, name); await File.WriteAllTextAsync(file, content, ct);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        return app;
    }
}
