using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RecipeManager.Api.Maintenance;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.IntegrationTests.Fixtures;
using RecipeManager.IntegrationTests.Households;
using RecipeManager.IntegrationTests.MealPlanning;

namespace RecipeManager.IntegrationTests.Operations;

public sealed class HostingTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Liveness_is_independent_of_database_readiness_and_one_shot_migration_is_repeatable()
    {
        await using var db = new RecipeManagerDbContext(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(postgres.ConnectionString).Options);
        await db.Database.EnsureDeletedAsync();
        await using var host = new ApiFixture(postgres.ConnectionString);
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Database"] = postgres.ConnectionString }).Build();
        Assert.Equal(0, await MigrateCommand.RunAsync(configuration));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(0, await MigrateCommand.RunAsync(configuration));
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
    }
    [Fact]
    public async Task Static_client_is_public_but_unknown_api_hub_and_maintenance_paths_never_return_the_spa()
    {
        var root = Path.Combine(Path.GetTempPath(), "recipe-manager-static-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "index.html"), "<html><body>Angular fixture</body></html>");
            await File.WriteAllTextAsync(Path.Combine(root, "app.js"), "console.log('fixture');");
            await using var database = new RecipeManagerDbContext(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(postgres.ConnectionString).Options);
            await database.Database.EnsureDeletedAsync(); await database.Database.MigrateAsync();
            await using var host = new ApiFixture(postgres.ConnectionString, new Dictionary<string, string?> { ["Setup:Token"] = "test-only-operator-secret-which-is-never-a-production-default" }, webRoot: root);
            using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
            var page = await client.GetAsync("/recipes"); Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            Assert.Contains("Angular fixture", await page.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/app.js")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/missing")).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await HouseholdContractTests.Write(client, HttpMethod.Post, "/api/v1/setup", HouseholdContractTests.SetupBody())).StatusCode);
            foreach (var path in new[] { "/api/v1/missing", "/hubs/missing", "/health/missing", "/__maintenance/missing" })
            {
                var missing = await client.GetAsync(path); Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
                Assert.DoesNotContain("Angular fixture", await missing.Content.ReadAsStringAsync());
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Optional_body_delete_keeps_its_api_route_when_the_spa_is_enabled()
    {
        var observations = new List<string>();
        await using var scenario = await MealScenario.Create(postgres, configure: services => services.AddSingleton<IStartupFilter>(new RouteObserver(observations)));
        var created = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, "/api/v1/meals/other", new { date = "2026-11-04", slotId = scenario.SlotId, label = "Takeaway" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var deleted = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Delete, "/api/v1/meals/" + id, etag: created.Headers.ETag!.ToString());
        Assert.True(deleted.StatusCode == HttpStatusCode.NoContent, string.Join(" | ", observations) + " " + await deleted.Content.ReadAsStringAsync());
    }
    private sealed class RouteObserver(List<string> observations) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, continuation) =>
            {
                await continuation();
                if (context.Request.Method == "DELETE") observations.Add(context.GetEndpoint()?.DisplayName ?? "no endpoint");
            }); next(app);
        };
    }

    [Fact]
    public async Task Invalid_migrator_configuration_fails_without_starting_the_web_host()
    {
        Assert.Equal(1, await MigrateCommand.RunAsync(new ConfigurationBuilder().Build()));
    }
}
