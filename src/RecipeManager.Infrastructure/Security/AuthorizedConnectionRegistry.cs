using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Infrastructure.Persistence;

namespace RecipeManager.Infrastructure.Security;

// Single-host authorization fence. No direct SignalR group broadcast bypasses this registry.
public sealed class AuthorizedConnectionRegistry(IServiceScopeFactory scopes, TimeProvider clock)
{
    private sealed class UserGate
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public List<Task> UnfinishedSends { get; } = [];
    }
    private sealed record Connection(RequestActor Actor, string Id, Action Abort,
        Func<string, object, CancellationToken, Task> Send);
    private readonly ConcurrentDictionary<Guid, UserGate> _users = new();
    private readonly ConcurrentDictionary<string, Connection> _connections = new(StringComparer.Ordinal);

    public async Task ConnectAsync(RequestActor actor, string connectionId, Action abort,
        Func<string, object, CancellationToken, Task> send, CancellationToken cancellationToken = default)
    {
        var gate = _users.GetOrAdd(actor.UserId, _ => new());
        await gate.Gate.WaitAsync(cancellationToken);
        try
        {
            if (!await Valid(actor, cancellationToken)) { abort(); throw new UnauthorizedAccessException("session_invalid"); }
            if (!_connections.TryAdd(connectionId, new(actor, connectionId, abort, send)))
                throw new InvalidOperationException("Duplicate connection identity.");
        }
        finally { gate.Gate.Release(); }
    }

    public void Disconnect(string connectionId) => _connections.TryRemove(connectionId, out _);

    public async Task DispatchHouseholdAsync(Guid householdId, string method, object payload, CancellationToken cancellationToken = default)
        => await Dispatch(householdId, null, method, payload, cancellationToken);

    public async Task DispatchPersonalAsync(Guid householdId, Guid userId, string method, object payload, CancellationToken cancellationToken = default)
        => await Dispatch(householdId, userId, method, payload, cancellationToken);

    private async Task Dispatch(Guid householdId, Guid? userId, string method, object payload, CancellationToken cancellationToken)
    {
        if (userId is null && method == "PersonalChanged") throw new ArgumentException("Personal changes require a user recipient.", nameof(method));
        if (method is not ("HouseholdChanged" or "ShoppingListChanged" or "ShoppingListDeleted" or "PersonalChanged"))
            throw new ArgumentException("Unsupported invalidation event.", nameof(method));
        foreach (var connection in _connections.Values.Where(x => x.Actor.HouseholdId == householdId && (userId == null || x.Actor.UserId == userId)).ToArray())
        {
            var gate = _users.GetOrAdd(connection.Actor.UserId, _ => new());
            await gate.Gate.WaitAsync(cancellationToken);
            try
            {
                if (!_connections.ContainsKey(connection.Id)) continue;
                if (!await Valid(connection.Actor, cancellationToken)) { Abort(connection); continue; }
                using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                bounded.CancelAfter(TimeSpan.FromSeconds(2));
                Task? sending = null;
                try
                {
                    // Dispatch begins under the same gate that fences the durable revocation.
                    sending = connection.Send(method, payload, bounded.Token);
                    await sending.WaitAsync(bounded.Token);
                }
                catch
                {
                    bounded.Cancel();
                    Abort(connection);
                    if (sending is { IsCompleted: false }) gate.UnfinishedSends.Add(sending);
                    if (sending is not null)
                        _ = sending.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
            finally { gate.Gate.Release(); }
        }
    }

    public async Task ExpireAsync(CancellationToken cancellationToken)
    {
        foreach (var connection in _connections.Values.Where(x => x.Actor.ExpiresAt <= clock.GetUtcNow()).ToArray())
        {
            var gate = _users.GetOrAdd(connection.Actor.UserId, _ => new());
            await gate.Gate.WaitAsync(cancellationToken);
            try
            {
                if (_connections.ContainsKey(connection.Id) && connection.Actor.ExpiresAt <= clock.GetUtcNow()) Abort(connection);
            }
            finally { gate.Gate.Release(); }
        }
    }

    public async Task RevokeAsync(Guid userId, Func<Action, CancellationToken, Task> commit, CancellationToken cancellationToken)
    {
        var gate = _users.GetOrAdd(userId, _ => new());
        await gate.Gate.WaitAsync(cancellationToken);
        var commitAttempted = false;
        try
        {
            // A timed-out transport may ignore cancellation even after socket abort. Never commit
            // until those tasks have drained; a non-cooperative transport causes a safe retryable failure.
            var unfinished = gate.UnfinishedSends.Where(task => !task.IsCompleted).ToArray();
            if (unfinished.Length > 0)
            {
                try
                {
                    await Task.WhenAll(unfinished.Select(Observe)).WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
                }
                catch (TimeoutException) { throw new HouseholdConflictException("session_drain_pending"); }
            }
            gate.UnfinishedSends.RemoveAll(task => task.IsCompleted);
            await commit(() => commitAttempted = true, cancellationToken);
            commitAttempted = true;
        }
        finally
        {
            // Ambiguous commit failures also fail closed; pre-commit validation failures preserve sockets.
            if (commitAttempted)
                foreach (var connection in _connections.Values.Where(x => x.Actor.UserId == userId).ToArray()) Abort(connection);
            gate.Gate.Release();
        }
    }

    private static async Task Observe(Task task)
    {
        try { await task; } catch { /* Socket is already aborted; only completion is required. */ }
    }

    private async Task<bool> Valid(RequestActor actor, CancellationToken cancellationToken)
    {
        if (actor.ExpiresAt <= clock.GetUtcNow()) return false;
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<RecipeManagerDbContext>();
        return await database.Memberships.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.UserId == actor.UserId && x.HouseholdId == actor.HouseholdId && x.IsActive)
            .Join(database.Users.AsNoTracking().Where(x => x.IsActive && x.SecurityStamp == actor.SecurityStamp),
                member => member.UserId, user => user.Id, (member, user) => user.Id).AnyAsync(cancellationToken);
    }
    private void Abort(Connection connection)
    {
        Disconnect(connection.Id);
        try { connection.Abort(); } catch { /* Already detached; further dispatch cannot use it. */ }
    }
}
