using Microsoft.EntityFrameworkCore;
using RecipeManager.Infrastructure.Configuration;
using RecipeManager.Infrastructure.Persistence;

namespace RecipeManager.Api.Maintenance;

public static class MigrateCommand
{
    public static async Task<int> RunAsync(IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        using var logging = LoggerFactory.Create(builder => builder.AddJsonConsole(options => options.UseUtcTimestamp = true));
        var logger = logging.CreateLogger("RecipeManager.Migrations");
        try
        {
            var connection = SecretConfiguration.DatabaseConnection(configuration);
            await using var database = new RecipeManagerDbContext(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(connection).Options);
            await database.Database.MigrateAsync(cancellationToken);
            logger.LogInformation("database_migrations_applied"); return 0;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // Provider/configuration exception text can contain private connection information.
            logger.LogError("database_migration_failed"); return 1;
        }
    }
}
public static class HealthProbeCommand
{
    public static async Task<int> RunAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var response = await client.GetAsync("http://127.0.0.1:8080/health/ready");
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (HttpRequestException) { return 1; }
        catch (OperationCanceledException) { return 1; }
    }
}
