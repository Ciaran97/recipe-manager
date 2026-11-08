using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RecipeManager.Core.Modules.Imports;
using RecipeManager.Infrastructure.Imports;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.IntegrationTests.Fixtures;
using RecipeManager.IntegrationTests.Households;
using RecipeManager.IntegrationTests.MealPlanning;
using RecipeManager.IntegrationTests.Recipes;

namespace RecipeManager.IntegrationTests.Imports;

public sealed class ImportExtractionTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private sealed class Resolver : IImportDnsResolver
    {
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct) => Task.FromResult(new[] { IPAddress.Parse("8.8.8.8") });
    }
    private sealed class Website(string connection) : IWebsiteHttpTransport
    {
        public int Calls { get; private set; }
        public bool ProbeHouseholdLock { get; set; } = true;
        public async Task<HttpResponseMessage> SendAsync(Uri uri, CancellationToken ct)
        {
            Calls++;
            // External work must run after the preflight transaction releases the household lock.
            await using var db = new RecipeManagerDbContext(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(connection).Options);
            var home = await db.Households.IgnoreQueryFilters().Select(x => x.Id).SingleAsync(ct);
            if (ProbeHouseholdLock)
            {
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM households.\"Households\" WHERE \"Id\" = {home} FOR UPDATE NOWAIT", ct);
            }
            return new(HttpStatusCode.OK) { Content = new StringContent("""<script type="application/ld+json">[{"@type":"Recipe","name":"Rice","recipeIngredient":["1 cup rice","salt to taste","1 red pepper","250g beef strips"]},{"@type":"Recipe","name":"Second"}]</script>""", Encoding.UTF8, "text/html") };
        }
    }
    private sealed class Provider : IAiRecipeProvider
    {
        public int Calls { get; private set; }
        public Task<ImportExtraction> ExtractAsync(string source, AiRecipeProviderConfiguration config, CancellationToken ct)
        {
            Calls++; Assert.Equal("fixture-provider-key", config.ApiKey); Assert.Equal("fixture-model", config.Model);
            return new FakeAiRecipeProvider(AiProviderTests.Document()).ExtractAsync(source, config, ct);
        }
    }

    [Fact]
    public async Task Ai_off_website_import_preserves_multiple_candidates_unknowns_and_retries_without_creating_recipes()
    {
        var website = new Website(postgres.ConnectionString);
        await using var fixture = await MealScenario.Create(postgres, configure: services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IImportDnsResolver>(new Resolver()));
            services.Replace(ServiceDescriptor.Singleton<IWebsiteHttpTransport>(website));
        });
        var capsicum = await RecipeContractTests.Ingredient(fixture.Admin, "Red capsicum");
        var pepperRead = await fixture.Admin.GetAsync("/api/v1/ingredients/" + capsicum);
        Assert.Equal(HttpStatusCode.OK, (await HouseholdContractTests.Write(fixture.Admin, HttpMethod.Post, $"/api/v1/ingredients/{capsicum}/aliases", new { name = "red pepper" }, pepperRead.Headers.ETag!.ToString())).StatusCode);
        _ = await RecipeContractTests.Ingredient(fixture.Admin, "Beef mince");
        var key = Guid.NewGuid(); var body = new { url = "https://recipe.example/", handoffId = (Guid?)null };
        var first = await HouseholdContractTests.Write(fixture.Admin, HttpMethod.Post, "/api/v1/imports/url", body, retryKey: key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var json = await first.Content.ReadFromJsonAsync<JsonElement>(); var draft = json.GetProperty("draft");
        Assert.Equal(body.url, draft.GetProperty("originalInput").GetString());
        Assert.Equal(2, draft.GetProperty("extraction").GetProperty("recipes").GetArrayLength());
        Assert.Equal("unknown", draft.GetProperty("extraction").GetProperty("recipes")[0].GetProperty("ingredients")[1].GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, draft.GetProperty("measurementStandard").ValueKind);
        var rows = draft.GetProperty("extraction").GetProperty("recipes")[0].GetProperty("ingredients");
        Assert.Equal(capsicum, rows[2].GetProperty("matchedIngredientId").GetGuid());
        Assert.Equal("Red capsicum", rows[2].GetProperty("matchedIngredientName").GetString());
        Assert.Equal(JsonValueKind.Null, rows[3].GetProperty("matchedIngredientId").ValueKind);
        var replay = await HouseholdContractTests.Write(fixture.Admin, HttpMethod.Post, "/api/v1/imports/url", body, retryKey: key);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(draft.GetProperty("id").GetGuid(), (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("draft").GetProperty("id").GetGuid());
        Assert.Equal(1, website.Calls);
        Assert.Equal(HttpStatusCode.Conflict, (await HouseholdContractTests.Write(fixture.Admin, HttpMethod.Post, "/api/v1/imports/url", new { url = "https://different.example/" }, retryKey: key)).StatusCode);
        website.ProbeHouseholdLock = false; // Other requests may legitimately hold the household lock briefly.
        var concurrentKey = Guid.NewGuid();
        var parallel = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => HouseholdContractTests.Write(fixture.Admin, HttpMethod.Post, "/api/v1/imports/url", body, retryKey: concurrentKey)));
        Assert.Single(parallel, response => response.StatusCode == HttpStatusCode.Created);
        Assert.All(parallel, response => Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created));
        var outcomes = await Task.WhenAll(parallel.Select(response => response.Content.ReadFromJsonAsync<JsonElement>()));
        Assert.Single(outcomes.Select(value => value.GetProperty("draft").GetProperty("id").GetGuid()).Distinct());
        var recipes = await fixture.Admin.GetFromJsonAsync<JsonElement>("/api/v1/recipes"); Assert.Single(recipes.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Text_import_uses_protected_household_settings_and_requires_enabled_ai_before_processing()
    {
        var provider = new Provider();
        await using var fixture = await MealScenario.Create(postgres, configure: services => services.Replace(ServiceDescriptor.Singleton<IAiRecipeProvider>(provider)));
        var body = new { text = AiProviderTests.Input }; var key = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await HouseholdContractTests.Write(fixture.Admin, HttpMethod.Post, "/api/v1/imports/text", body, retryKey: key)).StatusCode);
        Assert.Equal(0, provider.Calls);
        var settings = await fixture.Admin.GetAsync("/api/v1/household/settings");
        var current = await settings.Content.ReadFromJsonAsync<JsonElement>();
        var changed = await HouseholdContractTests.Write(fixture.Admin, HttpMethod.Put, "/api/v1/household/settings", new
        {
            name = current.GetProperty("name").GetString(),
            timezone = current.GetProperty("timezone").GetString(),
            ai = new { enabled = true, provider = "OpenAICompatible", model = "fixture-model", baseUrl = "http://localhost:11434/v1", apiKey = "fixture-provider-key", clearApiKey = false }
        }, settings.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var first = await HouseholdContractTests.Write(fixture.Admin, HttpMethod.Post, "/api/v1/imports/text", body, retryKey: key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.DoesNotContain("fixture-provider-key", await first.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await HouseholdContractTests.Write(fixture.Admin, HttpMethod.Post, "/api/v1/imports/text", body, retryKey: key)).StatusCode);
        Assert.Equal(1, provider.Calls);
    }
    [Fact]
    public async Task Handoff_extraction_resolves_committed_result_before_refetch_after_cleanup_and_retains_candidates_during_review()
    {
        var clock = new ControlledClock(DateTimeOffset.UtcNow); var website = new Website(postgres.ConnectionString);
        await using var fixture = await MealScenario.Create(postgres, clock, services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IImportDnsResolver>(new Resolver()));
            services.Replace(ServiceDescriptor.Singleton<IWebsiteHttpTransport>(website));
        });
        var id = Guid.NewGuid(); const string url = "https://recipe.example/";
        Assert.Equal(HttpStatusCode.Created, (await HouseholdContractTests.Write(fixture.Admin, HttpMethod.Post, "/api/v1/import-handoffs", new { handoffId = id, capturedAt = clock.GetUtcNow(), inputHash = ImportInput.Hash(ImportSourceKind.Url, url) })).StatusCode);
        var sourceBody = new { url, handoffId = id };
        var start = await HouseholdContractTests.Write(fixture.Admin, HttpMethod.Post, "/api/v1/imports/url", sourceBody);
        Assert.Equal(HttpStatusCode.Created, start.StatusCode);
        var draft = (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("draft"); var draftId = draft.GetProperty("id").GetGuid();
        var draftPath = "/api/v1/imports/" + draftId; var read = await fixture.Admin.GetAsync(draftPath);
        var recipe = await fixture.Admin.GetFromJsonAsync<JsonElement>("/api/v1/recipes/" + fixture.RecipeId);
        var ingredient = recipe.GetProperty("ingredients")[0].GetProperty("ingredientId").GetGuid();
        var review = new { recipe = RecipeContractTests.RecipeWrite(ingredient), confirmedMeasurementStandard = "AU", reviewConfirmed = true };
        var edited = await HouseholdContractTests.Write(fixture.Admin, HttpMethod.Put, draftPath, review, read.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var corrected = await edited.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, corrected.GetProperty("extraction").GetProperty("recipes").GetArrayLength());
        Assert.Equal("Rice bowls", corrected.GetProperty("reviewedRecipe").GetProperty("title").GetString());
        Assert.Equal(url, corrected.GetProperty("originalInput").GetString());
        var committed = await HouseholdContractTests.Write(fixture.Admin, HttpMethod.Post, $"/api/v1/import-handoffs/{id}/commit", new { draftId, expectedDraftRevision = corrected.GetProperty("revision").GetString(), review });
        Assert.Equal(HttpStatusCode.Created, committed.StatusCode);
        var original = await committed.Content.ReadFromJsonAsync<JsonElement>();
        clock.Advance(TimeSpan.FromDays(6));
        await using (var scope = fixture.Host.Services.CreateAsyncScope()) await scope.ServiceProvider.GetRequiredService<ExpiredDraftCleanup>().SweepAsync(null, CancellationToken.None);
        var retry = await HouseholdContractTests.Write(fixture.Admin, HttpMethod.Post, "/api/v1/imports/url", sourceBody);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode); var outcome = await retry.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, outcome.GetProperty("draft").ValueKind);
        Assert.Equal(original.GetProperty("recipeId").GetGuid(), outcome.GetProperty("commitResult").GetProperty("recipeId").GetGuid());
        Assert.Equal(1, website.Calls);
    }

}
