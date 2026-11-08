using Microsoft.AspNetCore.SignalR;

namespace RecipeManager.Api.Security;

public sealed class HouseholdHubFilter(CurrentMembership membership) : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        if (await membership.ResolveActorAsync(invocationContext.Context.GetHttpContext(), invocationContext.Context.ConnectionAborted) is null)
        {
            invocationContext.Context.Abort();
            throw new HubException("session_invalid");
        }
        return await next(invocationContext);
    }
}
