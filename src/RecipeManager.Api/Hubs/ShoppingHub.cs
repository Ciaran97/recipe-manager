using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using RecipeManager.Api.Security;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Modules.Shopping.Contracts;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.Infrastructure.Security;

namespace RecipeManager.Api.Hubs;

public interface IShoppingListNotification { Guid ListId { get; } }
public sealed record ShoppingListChanged(Guid ListId, string Revision, Guid ChangeId) : IShoppingListNotification;
public sealed record ShoppingListDeleted(Guid ListId, string Revision) : IShoppingListNotification;

public sealed class HouseholdSubscriptions
{
    private sealed class Subscription
    {
        public volatile bool Household;
        public ConcurrentDictionary<Guid, byte> Lists { get; } = new();
    }
    private readonly ConcurrentDictionary<string, Subscription> _connections = new(StringComparer.Ordinal);
    public void Register(string connectionId) => _connections.TryAdd(connectionId, new());
    public void Remove(string connectionId) => _connections.TryRemove(connectionId, out _);
    public void Household(string connectionId) { if (_connections.TryGetValue(connectionId, out var state)) state.Household = true; }
    public void List(string connectionId, Guid id) { if (_connections.TryGetValue(connectionId, out var state)) state.Lists.TryAdd(id, 0); }
    public void RemoveList(string connectionId, Guid id) { if (_connections.TryGetValue(connectionId, out var state)) state.Lists.TryRemove(id, out _); }
    public bool Sends(string connectionId, string method, object payload)
    {
        if (!_connections.TryGetValue(connectionId, out var state)) return false;
        return method switch
        {
            "PersonalChanged" => true,
            "HouseholdChanged" => state.Household,
            "ShoppingListChanged" or "ShoppingListDeleted" => payload is IShoppingListNotification item && state.Lists.ContainsKey(item.ListId),
            _ => false
        };
    }
}

[Authorize(Policy = IdentityConfiguration.MemberPolicy)]
public sealed class ShoppingHub(CurrentMembership membership, AuthorizedConnectionRegistry registry,
    HouseholdSubscriptions subscriptions, IHubContext<ShoppingHub> hub, IShoppingStore shopping) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var actor = await Actor(); var id = Context.ConnectionId;
        subscriptions.Register(id);
        try
        {
            await registry.ConnectAsync(actor, id, Context.Abort, (method, payload, ct) => subscriptions.Sends(id, method, payload)
                ? hub.Clients.Client(id).SendCoreAsync(method, [payload], ct) : Task.CompletedTask, Context.ConnectionAborted);
            await base.OnConnectedAsync();
        }
        catch { subscriptions.Remove(id); throw; }
    }
    public async Task SubscribeHousehold() { _ = await Actor(); subscriptions.Household(Context.ConnectionId); }
    public async Task SubscribeList(Guid listId)
    {
        var actor = await Actor();
        try { _ = await shopping.ReadAsync(actor, listId, Context.ConnectionAborted); }
        catch (ResourceMissingException) { throw new HubException("list_unavailable"); }
        catch (TransactionAccessException) { Context.Abort(); throw new HubException("session_invalid"); }
        subscriptions.List(Context.ConnectionId, listId);
    }
    public Task UnsubscribeList(Guid listId) { subscriptions.RemoveList(Context.ConnectionId, listId); return Task.CompletedTask; }
    private async Task<RequestActor> Actor()
    {
        var actor = await membership.ResolveActorAsync(Context.GetHttpContext(), Context.ConnectionAborted);
        if (actor is not null) return actor;
        Context.Abort(); throw new HubException("session_invalid");
    }
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        registry.Disconnect(Context.ConnectionId); subscriptions.Remove(Context.ConnectionId); return base.OnDisconnectedAsync(exception);
    }
}

public sealed class HubOriginMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/hubs/shopping") && context.Request.Headers.TryGetValue("Origin", out var origins))
        {
            var origin = origins.ToString(); var expected = new Uri(context.Request.Scheme + "://" + context.Request.Host);
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var supplied) || supplied.UserInfo.Length != 0 || supplied.GetLeftPart(UriPartial.Authority) != origin.TrimEnd('/')
                || !string.Equals(supplied.Scheme, expected.Scheme, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(supplied.Host, expected.Host, StringComparison.OrdinalIgnoreCase) || supplied.Port != expected.Port)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden; return;
            }
        }
        await next(context);
    }
}

public sealed class ConnectionExpiryService(AuthorizedConnectionRegistry registry, TimeProvider clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), clock);
        try { while (await timer.WaitForNextTickAsync(stoppingToken)) await registry.ExpireAsync(stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
