using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RecipeManager.Core.Modules.Imports;
using RecipeManager.Core.Modules.Recipes;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.IntegrationTests.Fixtures;
using RecipeManager.IntegrationTests.Households;
using RecipeManager.IntegrationTests.MealPlanning;
using SkiaSharp;

namespace RecipeManager.IntegrationTests.Operations;

public sealed class RestoreValidationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Pg_dump_restores_snapshots_actuals_reservations_manual_edits_images_and_durable_receipt_identities()
    {
        await using var scenario = await MealScenario.Create(postgres);
        var (meal, _) = await scenario.Cook(); var source = meal.GetProperty("cookingEvent").GetProperty("id").GetGuid();
        var (balance, tag) = await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{source}/leftovers");
        Assert.Equal(HttpStatusCode.OK, (await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Put, $"/api/v1/cooking-events/{source}/actuals", new { actualPrepared = "4", actualEaten = "2" }, tag)).StatusCode);
        (balance, _) = await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{source}/leftovers");
        var allocation = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, "/api/v1/meals/leftovers", new { sourceCookingEventId = source, sourceRevision = balance.GetProperty("sourceRevision").GetString(), date = "2026-11-03", slotId = scenario.SlotId, plannedPortions = "2" });
        Assert.Equal(HttpStatusCode.Created, allocation.StatusCode);
        var allocated = await allocation.Content.ReadFromJsonAsync<JsonElement>(); var allocationId = allocated.GetProperty("id").GetGuid();
        (balance, _) = await MealScenario.Read(scenario.Admin, $"/api/v1/cooking-events/{source}/leftovers");
        Assert.Equal(HttpStatusCode.OK, (await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Put, $"/api/v1/meals/{allocationId}/consumption", new { consumedPortions = "0.5", reservedRemainder = "1", sourceRevision = balance.GetProperty("sourceRevision").GetString() }, allocation.Headers.ETag!.ToString())).StatusCode);
        var generated = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, "/api/v1/shopping-lists/generate", new { name = "Restore list", selection = new { kind = "mealRange", from = "2026-11-02", to = "2026-11-04" }, basis = "planned" });
        Assert.Equal(HttpStatusCode.Created, generated.StatusCode);
        var list = await generated.Content.ReadFromJsonAsync<JsonElement>(); var listPath = "/api/v1/shopping-lists/" + list.GetProperty("id").GetGuid();
        var manual = await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Post, listPath + "/items", new { label = "Bread", quantityText = "2", kind = "numeric", unit = "item" }, generated.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.Created, manual.StatusCode);
        var (items, listTag) = await MealScenario.Read(scenario.Admin, listPath);
        var manualId = items.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("manual").GetBoolean()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await HouseholdContractTests.Write(scenario.Admin, HttpMethod.Put, listPath + "/items/" + manualId, new { purchased = true }, listTag)).StatusCode);
        await using var scope = scenario.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<RecipeManagerDbContext>();
        var user = await db.Users.SingleAsync(x => x.Email == "admin@example.test");
        var household = await db.Memberships.IgnoreQueryFilters().Where(x => x.UserId == user.Id).Select(x => x.HouseholdId).SingleAsync(); db.BindHouseholdScope(household);
        var old = DateTimeOffset.UtcNow.AddDays(-10);
        var committed = new ImportHandoff(Guid.NewGuid(), household, user.Id, old, old, ImportInput.Hash(ImportSourceKind.Text, "Fixture recipe"));
        committed.Commit(new string('A', 64), new(scenario.RecipeId, 1, old), old);
        var expired = new ImportHandoff(Guid.NewGuid(), household, user.Id, old, old, ImportInput.Hash(ImportSourceKind.Text, "Expired fixture")); Assert.Throws<ImportStateException>(() => expired.RequireOpen(DateTimeOffset.UtcNow));
        var imageKey = Guid.NewGuid().ToString("N"); using var bitmap = new SKBitmap(2, 3); using var picture = SKImage.FromBitmap(bitmap);
        using var png = picture.Encode(SKEncodedImageFormat.Png, 100); var imageBytes = png.ToArray();
        var image = new ImageAsset(Guid.NewGuid(), household, imageKey, "image/png", imageBytes.Length);
        db.AddRange(committed, expired, image); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var before = await Snapshot(db);
        var root = Path.Combine(Path.GetTempPath(), "recipe-manager-restore-" + Guid.NewGuid().ToString("N"));
        var target = "restore_" + Guid.NewGuid().ToString("N"); var dump = "/tmp/" + target + ".dump";
        Directory.CreateDirectory(root);
        try
        {
            var originalFiles = Path.Combine(root, "original"); var restoredFiles = Path.Combine(root, "restored"); Directory.CreateDirectory(originalFiles);
            var relativeImage = Path.Combine("images", household.ToString("N"), imageKey);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(originalFiles, relativeImage))!); await File.WriteAllBytesAsync(Path.Combine(originalFiles, relativeImage), imageBytes);
            Assert.Equal(0, (await postgres.Container.ExecAsync(["pg_dump", "-U", "recipe_tests", "-d", "recipe_manager_tests", "-Fc", "-f", dump])).ExitCode);
            Assert.Equal(0, (await postgres.Container.ExecAsync(["createdb", "-U", "recipe_tests", target])).ExitCode);
            Assert.Equal(0, (await postgres.Container.ExecAsync(["pg_restore", "-U", "recipe_tests", "-d", target, "--exit-on-error", dump])).ExitCode);
            CopyDirectory(originalFiles, restoredFiles);
            var connection = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = target };
            await using var restored = new RecipeManagerDbContext(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(connection.ConnectionString).Options);
            restored.BindHouseholdScope(household);
            Assert.Equal(before, await Snapshot(restored)); Assert.Equal(before, await Snapshot(db));
            Assert.Equal(imageBytes, await File.ReadAllBytesAsync(Path.Combine(restoredFiles, relativeImage)));
            Assert.Equal(ImportHandoffState.Committed, (await restored.ImportHandoffs.SingleAsync(x => x.HandoffId == committed.HandoffId)).State);
            Assert.Equal(ImportHandoffState.Expired, (await restored.ImportHandoffs.SingleAsync(x => x.HandoffId == expired.HandoffId)).State);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            _ = await postgres.Container.ExecAsync(["dropdb", "-U", "recipe_tests", "--if-exists", target]);
            _ = await postgres.Container.ExecAsync(["rm", "-f", dump]);
            Directory.Delete(root, recursive: true);
        }
    }
    [Fact]
    public void Copied_encrypted_key_ring_and_certificate_can_recover_protected_settings()
    {
        var root = Path.Combine(Path.GetTempPath(), "recipe-manager-keys-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=Restore fixture", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            var source = Path.Combine(root, "source"); var restored = Path.Combine(root, "restored"); Directory.CreateDirectory(source);
            var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            File.WriteAllBytes(Path.Combine(source, "protection.pfx"), certificate.Export(X509ContentType.Pfx, password));
            var provider = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(source, "keys")), options => options.SetApplicationName("RestoreFixture").ProtectKeysWithCertificate(certificate));
            var protectedValue = provider.CreateProtector("settings").Protect("Synthetic provider credential");
            CopyDirectory(source, restored);
            using var restoredCertificate = X509CertificateLoader.LoadPkcs12FromFile(Path.Combine(restored, "protection.pfx"), password, OperatingSystem.IsMacOS() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet);
            var recovered = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(restored, "keys")), options => options.SetApplicationName("RestoreFixture").ProtectKeysWithCertificate(restoredCertificate));
            Assert.Equal("Synthetic provider credential", recovered.CreateProtector("settings").Unprotect(protectedValue));
            Assert.All(Directory.GetFiles(Path.Combine(source, "keys")), path => Assert.Contains("encryptedSecret", File.ReadAllText(path)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var directory in Directory.GetDirectories(source)) CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
    private static async Task<string> Snapshot(RecipeManagerDbContext db) => JsonSerializer.Serialize(new
    {
        recipes = await db.Recipes.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        events = await db.CookingEvents.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        allocations = await db.Allocations.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        lists = await db.ShoppingLists.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        items = await db.ShoppingItems.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        receipts = await db.ImportHandoffs.AsNoTracking().OrderBy(x => x.HandoffId).ToArrayAsync(),
        images = await db.ImageAssets.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync()
    });
}
