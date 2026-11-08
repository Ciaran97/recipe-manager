using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Shared;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.IntegrationTests.Fixtures;

namespace RecipeManager.IntegrationTests.Households;

public sealed class SettingsServiceTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Personal_revisions_are_isolated_and_provider_secrets_are_tenant_bound_and_write_only()
    {
        await using var database = new RecipeManagerDbContext(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(postgres.ConnectionString).Options);
        await database.Database.MigrateAsync();
        await using var host = new ApiFixture(postgres.ConnectionString, new Dictionary<string, string?> { ["Setup:Token"] = "test-only-operator-secret-which-is-never-a-production-default" });
        HouseholdSession admin;
        await using (var scope = host.Services.CreateAsyncScope())
            admin = await scope.ServiceProvider.GetRequiredService<SetupHousehold>().CreateAsync(new("test-only-operator-secret-which-is-never-a-production-default",
                "admin@example.test", "original-test-passphrase", "Household", "UTC"));
        var stamp = (await database.Users.AsNoTracking().SingleAsync(x => x.Id == admin.Id)).SecurityStamp!;
        var actor = new RequestActor(admin.Id, admin.HouseholdId, stamp, DateTimeOffset.UtcNow.AddHours(1));
        async Task<T> Using<T>(Func<SettingsService, Task<T>> operation)
        {
            await using var scope = host.Services.CreateAsyncScope();
            return await operation(scope.ServiceProvider.GetRequiredService<SettingsService>());
        }
        var before = await Using(service => service.ProfileAsync(actor));
        var profile = await Using(service => service.UpdateProfileAsync(actor, before.Revision, "Ciaran", JsonSerializer.SerializeToElement(new { compact = true })));
        Assert.Equal(before.Revision + 1, profile.Revision);
        Assert.True(profile.Preferences.GetProperty("compact").GetBoolean());
        database.BindHouseholdScope(actor.HouseholdId);
        Assert.Equal(0, (await database.Households.AsNoTracking().SingleAsync()).ContentRevision);
        await Assert.ThrowsAsync<RevisionConflictException>(() => Using(service => service.UpdateProfileAsync(actor, before.Revision, "Stale", JsonSerializer.SerializeToElement(new { }))));
        var settings = await Using(service => service.HouseholdAsync(actor));
        var updated = await Using(service => service.UpdateHouseholdAsync(actor, settings.Revision, "Updated", "Australia/Perth",
            new(true, AiProviderKind.OpenAICompatible, "fixture-model", "http://localhost:11434/v1", "private-provider-fixture-key")));
        Assert.True(updated.Ai.HasApiKey);
        Assert.DoesNotContain("private-provider-fixture-key", JsonSerializer.Serialize(updated));
        var row = await database.Households.AsNoTracking().SingleAsync();
        Assert.DoesNotContain("private-provider-fixture-key", row.ProtectedAiSettings!);
        Assert.Equal(0, row.ShoppingSourcesRevision);
        var preserved = await Using(service => service.UpdateHouseholdAsync(actor, updated.Revision, "Updated", "Australia/Perth",
            new(true, AiProviderKind.OpenAICompatible, "other-model", "http://localhost:11434/v1")));
        Assert.True(preserved.Ai.HasApiKey);
        var cleared = await Using(service => service.UpdateHouseholdAsync(actor, preserved.Revision, "Updated", "Australia/Perth",
            new(false, AiProviderKind.OpenAICompatible, "", "", ClearApiKey: true)));
        Assert.False(cleared.Ai.HasApiKey);
        await using var passwordScope = host.Services.CreateAsyncScope();
        var service = passwordScope.ServiceProvider.GetRequiredService<SettingsService>();
        await Assert.ThrowsAsync<DomainValidationException>(() => service.ChangePasswordAsync(actor, profile.Revision, "incorrect", "replacement-test-passphrase"));
        await service.ChangePasswordAsync(actor, profile.Revision, "original-test-passphrase", "replacement-test-passphrase");
        Assert.NotEqual(actor.SecurityStamp, (await database.Users.AsNoTracking().SingleAsync(x => x.Id == actor.UserId)).SecurityStamp);
    }
}
