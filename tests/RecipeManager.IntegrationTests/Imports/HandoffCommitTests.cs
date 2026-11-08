using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Modules.Imports;
using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Core.Modules.Recipes.Contracts;
using RecipeManager.Infrastructure.Imports;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.IntegrationTests.Fixtures;
using RecipeManager.IntegrationTests.MealPlanning;

namespace RecipeManager.IntegrationTests.Imports;

public sealed class HandoffCommitTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly RecipeWrite Recipe = new("Imported soup", 2, [], [], [], [], []);
    private static readonly ImportReview Review = new(Recipe, MeasurementStandard.AU, true);
    private static async Task<RequestActor> Actor(MealScenario fixture, TimeProvider clock)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RecipeManagerDbContext>();
        var member = await db.Memberships.IgnoreQueryFilters().FirstAsync();
        var user = await db.Users.SingleAsync(x => x.Id == member.UserId);
        return new(user.Id, member.HouseholdId, user.SecurityStamp!, clock.GetUtcNow().AddDays(14));
    }
    private static async Task<T> Call<T>(MealScenario fixture, Func<ImportHandoffService, Task<T>> action)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ImportHandoffService>());
    }
    private static Task<ImportDraftRead> Draft(MealScenario fixture, RequestActor actor, Guid handoff)
        => Call(fixture, s => s.SaveExtractionAsync(actor, new(ImportSourceKind.Text, "salt to taste", handoff), "{}", "[\"quantity\"]", null));

    [Fact]
    public async Task Lost_success_response_then_day_six_retry_and_post_expiry_status_return_original_after_draft_cleanup()
    {
        var clock = new ControlledClock(new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero).AddTicks(7));
        await using var fixture = await MealScenario.Create(postgres, clock);
        var actor = await Actor(fixture, clock); var id = Guid.NewGuid();
        var register = new RegisterImportHandoff(id, clock.GetUtcNow(), ImportInput.Hash(ImportSourceKind.Text, "salt to taste"));
        var original = await Call(fixture, s => s.RegisterAsync(actor, register));
        var draft = await Draft(fixture, actor, id);
        var commit = new CommitImport(draft.Id, draft.Revision, Review);
        var first = await Call(fixture, s => s.CommitHandoffAsync(actor, id, commit));
        Assert.False(first.Replayed);
        clock.Advance(TimeSpan.FromDays(6));
        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var cleanup = scope.ServiceProvider.GetRequiredService<ExpiredDraftCleanup>();
            var removed = await cleanup.SweepAsync(null, CancellationToken.None);
            Assert.True(removed.Drafts >= 1);
            var db = scope.ServiceProvider.GetRequiredService<RecipeManagerDbContext>(); db.BindHouseholdScope(actor.HouseholdId);
            Assert.Empty(await db.ImportDrafts.ToArrayAsync());
            Assert.Single(await db.ImportHandoffs.ToArrayAsync());
        }
        var replay = await Call(fixture, s => s.CommitHandoffAsync(actor, id, commit));
        Assert.True(replay.Replayed); Assert.Equal(first.Result, replay.Result);
        Assert.Equal(original.ExpiresAt, (await Call(fixture, s => s.RegisterAsync(actor, register))).ExpiresAt);
        clock.Advance(TimeSpan.FromDays(2));
        Assert.Equal(first.Result, (await Call(fixture, s => s.StatusAsync(actor, id))).Result);
        Assert.Equal(first.Result, (await Call(fixture, s => s.CommitHandoffAsync(actor, id, commit))).Result);
        var changed = await Assert.ThrowsAsync<ImportStateException>(() => Call(fixture, s => s.CommitHandoffAsync(actor, id, commit with { Review = Review with { Recipe = Recipe with { Title = "Changed" } } })));
        Assert.Equal("handoff_already_committed", changed.Code);
    }

    [Fact]
    public async Task Concurrent_registration_and_commit_create_exactly_one_recipe_and_one_receipt()
    {
        var clock = new ControlledClock(DateTimeOffset.UtcNow);
        await using var fixture = await MealScenario.Create(postgres, clock); var actor = await Actor(fixture, clock);
        var id = Guid.NewGuid(); var register = new RegisterImportHandoff(id, clock.GetUtcNow(), ImportInput.Hash(ImportSourceKind.Text, "salt to taste"));
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Call(fixture, s => s.RegisterAsync(actor, register))));
        var draft = await Draft(fixture, actor, id); var command = new CommitImport(draft.Id, draft.Revision, Review);
        var commits = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Call(fixture, s => s.CommitHandoffAsync(actor, id, command))));
        Assert.Single(commits, x => !x.Replayed); Assert.Single(commits.Select(x => x.Result.RecipeId).Distinct());
        await using var scope = fixture.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<RecipeManagerDbContext>();
        db.BindHouseholdScope(actor.HouseholdId);
        Assert.Equal(2, await db.Recipes.CountAsync()); Assert.Single(await db.ImportHandoffs.ToArrayAsync());
    }

    [Fact]
    public async Task Unreviewed_unknown_standard_and_foreign_ingredient_fail_without_creating_a_recipe_or_committing_receipt()
    {
        var clock = new ControlledClock(DateTimeOffset.UtcNow);
        await using var fixture = await MealScenario.Create(postgres, clock); var actor = await Actor(fixture, clock); var id = Guid.NewGuid();
        await Call(fixture, s => s.RegisterAsync(actor, new(id, clock.GetUtcNow(), ImportInput.Hash(ImportSourceKind.Text, "salt to taste"))));
        var draft = await Draft(fixture, actor, id);
        await Assert.ThrowsAsync<RecipeManager.Core.Shared.DomainValidationException>(() => Call(fixture, s => s.CommitHandoffAsync(actor, id, new(draft.Id, 1, Review with { ConfirmedMeasurementStandard = null }))));
        await Assert.ThrowsAsync<RecipeManager.Core.Shared.DomainValidationException>(() => Call(fixture, s => s.CommitHandoffAsync(actor, id, new(draft.Id, 1, Review with { ReviewConfirmed = false }))));
        await Assert.ThrowsAsync<RecipeManager.Core.Modules.Households.ResourceMissingException>(() => Call(fixture, s => s.CommitHandoffAsync(actor, id, new(draft.Id, 1, Review with { Recipe = Recipe with { Ingredients = [new(0, Guid.NewGuid(), "1", QuantityKind.Numeric, IngredientUnit.Gram)] } }))));
        Assert.Equal(ImportHandoffState.Open, (await Call(fixture, s => s.StatusAsync(actor, id))).State);
        Assert.False((await Call(fixture, s => s.CommitHandoffAsync(actor, id, new(draft.Id, 1, Review)))).Replayed);
    }
    [Fact]
    public async Task Expired_open_handoff_cannot_be_reopened_and_missing_draft_can_be_reextracted_only_while_live()
    {
        var clock = new ControlledClock(DateTimeOffset.UtcNow);
        await using var fixture = await MealScenario.Create(postgres, clock); var actor = await Actor(fixture, clock); var id = Guid.NewGuid();
        var register = new RegisterImportHandoff(id, clock.GetUtcNow(), ImportInput.Hash(ImportSourceKind.Text, "salt to taste"));
        await Call(fixture, s => s.RegisterAsync(actor, register)); var draft = await Draft(fixture, actor, id);
        clock.Advance(TimeSpan.FromDays(2));
        var expiredDraft = await Assert.ThrowsAsync<ImportStateException>(() => Call(fixture, s => s.CommitHandoffAsync(actor, id, new(draft.Id, draft.Revision, Review))));
        Assert.Equal("draft_expired", expiredDraft.Code);
        var replacement = await Draft(fixture, actor, id); Assert.NotEqual(draft.Id, replacement.Id);
        clock.Advance(TimeSpan.FromDays(6));
        var expired = await Assert.ThrowsAsync<ImportStateException>(() => Call(fixture, s => s.RegisterAsync(actor, register with { CapturedAt = clock.GetUtcNow() })));
        Assert.Equal("handoff_expired", expired.Code);
        await Assert.ThrowsAsync<ImportStateException>(() => Draft(fixture, actor, id));
    }

    [Fact]
    public async Task Deleted_result_and_wrong_owner_never_recreate_or_disclose_the_recipe()
    {
        var clock = new ControlledClock(DateTimeOffset.UtcNow);
        await using var fixture = await MealScenario.Create(postgres, clock); var actor = await Actor(fixture, clock); var id = Guid.NewGuid();
        await Call(fixture, s => s.RegisterAsync(actor, new(id, clock.GetUtcNow(), ImportInput.Hash(ImportSourceKind.Text, "salt to taste"))));
        var draft = await Draft(fixture, actor, id); var command = new CommitImport(draft.Id, draft.Revision, Review);
        var committed = await Call(fixture, s => s.CommitHandoffAsync(actor, id, command));
        using var member = await fixture.Member();
        RequestActor other;
        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecipeManagerDbContext>();
            var user = await db.Users.SingleAsync(x => x.Id != actor.UserId);
            other = new(user.Id, actor.HouseholdId, user.SecurityStamp!, clock.GetUtcNow().AddDays(14));
            db.BindHouseholdScope(actor.HouseholdId);
            await db.Recipes.Where(x => x.Id == committed.Result.RecipeId).ExecuteDeleteAsync();
        }
        await Assert.ThrowsAsync<RecipeManager.Core.Modules.Households.ResourceMissingException>(() => Call(fixture, s => s.StatusAsync(other, id)));
        await Assert.ThrowsAsync<RecipeManager.Core.Modules.Households.ResourceMissingException>(() => Call(fixture, s => s.CommitHandoffAsync(other, id, command)));
        var missing = await Assert.ThrowsAsync<ImportStateException>(() => Call(fixture, s => s.CommitHandoffAsync(actor, id, command)));
        Assert.Equal("committed_recipe_deleted", missing.Code);
        Assert.Equal(committed.Result, missing.Result);
        await Assert.ThrowsAsync<ImportStateException>(() => Call(fixture, s => s.StatusAsync(actor, id)));
        await using var verification = fixture.Host.Services.CreateAsyncScope();
        var verify = verification.ServiceProvider.GetRequiredService<RecipeManagerDbContext>(); verify.BindHouseholdScope(actor.HouseholdId);
        Assert.Single(await verify.Recipes.ToArrayAsync()); Assert.Single(await verify.ImportHandoffs.ToArrayAsync());
    }

    [Fact]
    public async Task Cleanup_retains_expired_key_tombstone_and_unexpired_other_household_draft()
    {
        var clock = new ControlledClock(DateTimeOffset.UtcNow);
        await using var fixture = await MealScenario.Create(postgres, clock); var actor = await Actor(fixture, clock); var id = Guid.NewGuid();
        await Call(fixture, s => s.RegisterAsync(actor, new(id, clock.GetUtcNow(), ImportInput.Hash(ImportSourceKind.Text, "salt to taste"))));
        _ = await Draft(fixture, actor, id);
        clock.Advance(TimeSpan.FromDays(8));
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RecipeManagerDbContext>();
        var other = new RecipeManager.Core.Modules.Households.Household(Guid.NewGuid(), "Other household", "UTC");
        db.Add(other);
        db.Add(new ImportDraft(Guid.NewGuid(), other.Id, actor.UserId, null, ImportSourceKind.Text, "new capture", "{}", "[]", null, clock.GetUtcNow()));
        await db.SaveChangesAsync();
        var report = await scope.ServiceProvider.GetRequiredService<ExpiredDraftCleanup>().SweepAsync(null, CancellationToken.None);
        Assert.Equal(1, report.Drafts); Assert.Equal(1, report.ExpiredHandoffs);
        db.ChangeTracker.Clear(); db.BindHouseholdScope(actor.HouseholdId);
        Assert.Equal(ImportHandoffState.Expired, (await db.ImportHandoffs.SingleAsync()).State);
        Assert.Empty(await db.ImportDrafts.ToArrayAsync());
        db.BindHouseholdScope(other.Id); Assert.Single(await db.ImportDrafts.ToArrayAsync());
        var expired = await Assert.ThrowsAsync<ImportStateException>(() => Call(fixture, s => s.RegisterAsync(actor, new(id, clock.GetUtcNow(), ImportInput.Hash(ImportSourceKind.Text, "salt to taste")))));
        Assert.Equal("handoff_expired", expired.Code);
    }

    [Fact]
    public async Task Ordinary_import_commit_replays_retry_result_after_draft_removal_without_creating_twice()
    {
        var clock = new ControlledClock(DateTimeOffset.UtcNow);
        await using var fixture = await MealScenario.Create(postgres, clock); var actor = await Actor(fixture, clock);
        var draft = await Call(fixture, s => s.SaveExtractionAsync(actor, new(ImportSourceKind.Text, "Soup"), "{}", "[]", null));
        var key = Guid.NewGuid();
        var first = await Call(fixture, s => s.CommitDraftAsync(actor, draft.Id, draft.Revision, Review, key));
        Assert.False(first.Replayed);
        var replay = await Call(fixture, s => s.CommitDraftAsync(actor, draft.Id, draft.Revision, Review, key));
        Assert.True(replay.Replayed); Assert.Equal(first.Result, replay.Result);
        await Assert.ThrowsAsync<RetryPayloadConflictException>(() => Call(fixture, s => s.CommitDraftAsync(actor, draft.Id, draft.Revision, Review with { Recipe = Recipe with { Title = "Changed" } }, key)));
        var missing = await Assert.ThrowsAsync<ImportStateException>(() => Call(fixture, s => s.CommitDraftAsync(actor, draft.Id, draft.Revision, Review, Guid.NewGuid())));
        Assert.Equal("draft_expired", missing.Code);
    }

    [Fact]
    public async Task Ordinary_commit_for_bound_draft_uses_durable_receipt_without_ordinary_retry_cache()
    {
        var clock = new ControlledClock(DateTimeOffset.UtcNow);
        await using var fixture = await MealScenario.Create(postgres, clock); var actor = await Actor(fixture, clock); var id = Guid.NewGuid();
        await Call(fixture, s => s.RegisterAsync(actor, new(id, clock.GetUtcNow(), ImportInput.Hash(ImportSourceKind.Text, "salt to taste"))));
        var draft = await Draft(fixture, actor, id);
        var first = await Call(fixture, s => s.CommitDraftAsync(actor, draft.Id, draft.Revision, Review, Guid.NewGuid()));
        var replay = await Call(fixture, s => s.CommitDraftAsync(actor, draft.Id, draft.Revision, Review, Guid.NewGuid()));
        Assert.True(replay.Replayed); Assert.Equal(first.Result, replay.Result);
        Assert.Equal(first.Result, (await Call(fixture, s => s.StatusAsync(actor, id))).Result);
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RecipeManagerDbContext>(); db.BindHouseholdScope(actor.HouseholdId);
        Assert.Empty(await db.CreationRetries.Where(x => x.Route.StartsWith("/api/v1/imports/")).ToArrayAsync());
    }

}
