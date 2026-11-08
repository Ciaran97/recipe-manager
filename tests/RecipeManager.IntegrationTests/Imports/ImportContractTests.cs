using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Modules.Imports;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.IntegrationTests.Fixtures;
using RecipeManager.IntegrationTests.Households;
using RecipeManager.IntegrationTests.MealPlanning;
using RecipeManager.IntegrationTests.Recipes;

namespace RecipeManager.IntegrationTests.Imports;

public sealed class ImportContractTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Authenticated_handoff_commit_replays_original_result_and_enforces_owner_and_review()
    {
        await using var fixture = await MealScenario.Create(postgres);
        var id = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        var registered = await HouseholdContractTests.Write(fixture.Admin, HttpMethod.Post, "/api/v1/import-handoffs", new { handoffId = id, capturedAt = now, inputHash = ImportInput.Hash(ImportSourceKind.Text, "salt to taste") });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        RequestActor actor; Guid ingredient; ImportDraftRead draft;
        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecipeManagerDbContext>();
            var member = await db.Memberships.IgnoreQueryFilters().SingleAsync(); var user = await db.Users.SingleAsync();
            actor = new(user.Id, member.HouseholdId, user.SecurityStamp!, now.AddDays(14));
            ingredient = await db.Ingredients.IgnoreQueryFilters().Select(x => x.Id).SingleAsync();
            draft = await scope.ServiceProvider.GetRequiredService<ImportHandoffService>().SaveExtractionAsync(actor, new(ImportSourceKind.Text, "salt to taste", id), "{}", "[\"quantity\"]", null);
        }
        var draftPath = "/api/v1/imports/" + draft.Id;
        var draftResponse = await fixture.Admin.GetAsync(draftPath);
        Assert.Equal(HttpStatusCode.OK, draftResponse.StatusCode);
        var draftJson = await draftResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("salt to taste", draftJson.GetProperty("originalInput").GetString());
        Assert.Equal(JsonValueKind.Null, draftJson.GetProperty("measurementStandard").ValueKind);
        using (var conditional = new HttpRequestMessage(HttpMethod.Get, draftPath))
        {
            conditional.Headers.IfNoneMatch.Add(draftResponse.Headers.ETag!);
            Assert.Equal(HttpStatusCode.NotModified, (await fixture.Admin.SendAsync(conditional)).StatusCode);
        }
        var reviewBody = new { recipe = RecipeContractTests.RecipeWrite(ingredient), confirmedMeasurementStandard = "AU", reviewConfirmed = true };
        Assert.Equal((HttpStatusCode)428, (await HouseholdContractTests.Write(fixture.Admin, HttpMethod.Put, draftPath, reviewBody)).StatusCode);
        var reviewed = await HouseholdContractTests.Write(fixture.Admin, HttpMethod.Put, draftPath, reviewBody, draftResponse.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, reviewed.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await HouseholdContractTests.Write(fixture.Admin, HttpMethod.Put, draftPath, reviewBody, draftResponse.Headers.ETag.ToString())).StatusCode);
        draft = draft with { Revision = 2 };
        var path = "/api/v1/import-handoffs/" + id;
        var body = new { draftId = draft.Id, expectedDraftRevision = draft.Revision.ToString(CultureInfo.InvariantCulture), review = new { recipe = RecipeContractTests.RecipeWrite(ingredient), confirmedMeasurementStandard = "AU", reviewConfirmed = true } };
        var rejected = await HouseholdContractTests.Write(fixture.Admin, HttpMethod.Post, path + "/commit", new { body.draftId, body.expectedDraftRevision, review = new { body.review.recipe, confirmedMeasurementStandard = (string?)null, reviewConfirmed = false } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        var first = await HouseholdContractTests.Write(fixture.Admin, HttpMethod.Post, path + "/commit", body);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var original = await first.Content.ReadFromJsonAsync<JsonElement>();
        var replay = await HouseholdContractTests.Write(fixture.Admin, HttpMethod.Post, path + "/commit", body);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(original.GetRawText(), (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetRawText());
        var status = await fixture.Admin.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(original.GetProperty("recipeId").GetGuid(), status.GetProperty("result").GetProperty("recipeId").GetGuid());
        using var other = await fixture.Member();
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await other.GetAsync(draftPath)).StatusCode);
        using (var forbiddenConditional = new HttpRequestMessage(HttpMethod.Get, draftPath))
        {
            forbiddenConditional.Headers.IfNoneMatch.Add(reviewed.Headers.ETag!);
            Assert.Equal(HttpStatusCode.Gone, (await other.SendAsync(forbiddenConditional)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await HouseholdContractTests.Write(other, HttpMethod.Post, path + "/commit", body)).StatusCode);
        using var anonymous = fixture.Host.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
        var recipePath = "/api/v1/recipes/" + original.GetProperty("recipeId").GetGuid();
        var liveRecipe = await fixture.Admin.GetAsync(recipePath);
        Assert.Equal(HttpStatusCode.NoContent, (await HouseholdContractTests.Write(fixture.Admin, HttpMethod.Delete, recipePath, etag: liveRecipe.Headers.ETag!.ToString())).StatusCode);
        var deleted = await fixture.Admin.GetAsync(path);
        Assert.Equal(HttpStatusCode.Gone, deleted.StatusCode);
        var problem = await deleted.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("committed_recipe_deleted", problem.GetProperty("code").GetString());
        Assert.Equal(original.GetProperty("recipeId").GetGuid(), problem.GetProperty("commitResult").GetProperty("recipeId").GetGuid());
    }
    [Fact]
    public async Task Ordinary_review_commit_requires_validator_and_replays_original_with_retry_key()
    {
        await using var fixture = await MealScenario.Create(postgres);
        ImportDraftRead draft; Guid ingredient;
        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecipeManagerDbContext>();
            var membership = await db.Memberships.IgnoreQueryFilters().SingleAsync(); var user = await db.Users.SingleAsync();
            var actor = new RequestActor(user.Id, membership.HouseholdId, user.SecurityStamp!, DateTimeOffset.UtcNow.AddDays(14));
            ingredient = await db.Ingredients.IgnoreQueryFilters().Select(x => x.Id).SingleAsync();
            draft = await scope.ServiceProvider.GetRequiredService<ImportHandoffService>().SaveExtractionAsync(actor, new(ImportSourceKind.Text, "Soup"), "{}", "[]", null);
        }
        var path = "/api/v1/imports/" + draft.Id;
        var read = await fixture.Admin.GetAsync(path); var etag = read.Headers.ETag!.ToString(); var key = Guid.NewGuid();
        var body = new { recipe = RecipeContractTests.RecipeWrite(ingredient), confirmedMeasurementStandard = "AU", reviewConfirmed = true };
        Assert.Equal((HttpStatusCode)428, (await HouseholdContractTests.Write(fixture.Admin, HttpMethod.Post, path + "/commit", body, retryKey: key)).StatusCode);
        var first = await HouseholdContractTests.Write(fixture.Admin, HttpMethod.Post, path + "/commit", body, etag, key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var replay = await HouseholdContractTests.Write(fixture.Admin, HttpMethod.Post, path + "/commit", body, etag, key);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Gone, (await HouseholdContractTests.Write(fixture.Admin, HttpMethod.Post, path + "/commit", body, etag, Guid.NewGuid())).StatusCode);
    }

}
