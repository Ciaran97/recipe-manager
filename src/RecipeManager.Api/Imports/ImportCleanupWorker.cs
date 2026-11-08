using RecipeManager.Infrastructure.Imports;

namespace RecipeManager.Api.Imports;

// Startup plus periodic bounded work; no job broker or durable scheduling infrastructure.
public sealed class ImportCleanupWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<ImportCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Guid? cursor = null;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5), clock);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<ExpiredDraftCleanup>().SweepAsync(cursor, stoppingToken);
                cursor = result.NextHousehold;
                if (result.Drafts + result.RetryRecords + result.ExpiredHandoffs > 0)
                    logger.LogInformation("Import maintenance removed {Drafts} expired drafts and {Retries} retry records; retained {Handoffs} newly expired handoff tombstones.", result.Drafts, result.RetryRecords, result.ExpiredHandoffs);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception)
            {
                // No raw source text, database exception details, credentials or provider errors.
                logger.LogWarning("Import maintenance could not finish this batch; it will retry on the next interval.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
