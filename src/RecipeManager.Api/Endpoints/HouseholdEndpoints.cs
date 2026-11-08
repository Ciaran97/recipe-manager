using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using RecipeManager.Api.Contracts;
using RecipeManager.Api.Security;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.Infrastructure.Security;

namespace RecipeManager.Api.Endpoints;

public sealed record SetupStatus(bool SetupRequired);
public sealed record CsrfBootstrap(string Token);
public sealed record LoginWrite(string Email, string Password);
public sealed record ProfileWrite(string Name, JsonElement Preferences);
public sealed record PasswordWrite(string CurrentPassword, string NewPassword);
public sealed record HouseholdSettingsWrite(string Name, string Timezone, AiSettingsWrite? Ai = null);
public sealed record IssueInvitationWrite;
public sealed record InvitationCreated(Guid Id, string Token, string Link, DateTimeOffset ExpiresAt, long Revision);
public sealed record MemberRead(Guid Id, Guid UserId, string Email, string Name, HouseholdRole Role, long Revision, string Etag);
public sealed record MemberPage(IReadOnlyList<MemberRead> Items, string? NextCursor = null);
public sealed record InvitationRead(Guid Id, DateTimeOffset ExpiresAt, DateTimeOffset? AcceptedAt, DateTimeOffset? RevokedAt, long Revision, string Etag);
public sealed record InvitationPage(IReadOnlyList<InvitationRead> Items, string? NextCursor = null);

public static class HouseholdEndpoints
{
    public static IServiceCollection AddHouseholdRateLimits(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.AddPolicy("credentials", context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
            options.OnRejected = async (context, _) => await Results.Problem(statusCode: 429, title: "Too many attempts.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "rate_limited",
                    ["traceId"] = context.HttpContext.TraceIdentifier,
                    ["errors"] = Array.Empty<object>(),
                    ["conflicts"] = Array.Empty<object>()
                }).ExecuteAsync(context.HttpContext);
        });
        return services;
    }

    public static void MapHouseholdEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1").WithTags("Households");
        api.MapGet("/setup", async (HttpContext context, SetupHousehold setup, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return TypedResults.Ok(new SetupStatus(await setup.SetupRequiredAsync(ct)));
        }).AllowAnonymous().WithName("GetSetupStatus");
        api.MapPost("/setup", async (SetupRequest request, HttpContext context, SetupHousehold setup, UserManager<ApplicationUser> users,
            SignInManager<ApplicationUser> signIn, TimeProvider clock, CancellationToken ct) =>
        {
            var result = await setup.CreateAsync(request, ct);
            await SignIn(result.Id, context, users, signIn, clock);
            return TypedResults.Created("/api/v1/household/settings", result);
        }).AllowAnonymous().RequireRateLimiting("credentials").WithName("CompleteSetup");
        api.MapGet("/auth/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return TypedResults.Ok(new CsrfBootstrap(antiforgery.GetAndStoreTokens(context).RequestToken!));
        }).AllowAnonymous().WithName("GetCsrfToken");
        api.MapPost("/auth/login", Login).AllowAnonymous().RequireRateLimiting("credentials").WithName("Login").Produces<HouseholdSession>();
        api.MapGet("/auth/me", async (HttpContext context, CurrentMembership membership, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct);
            var access = await membership.ResolveAsync(context.User, ct) ?? throw new TransactionAccessException();
            context.Response.Headers.CacheControl = "no-store";
            return TypedResults.Ok(new HouseholdSession(actor.UserId, actor.HouseholdId, access.Role));
        }).RequireAuthorization(IdentityConfiguration.MemberPolicy).WithName("GetCurrentSession");
        api.MapPost("/auth/logout", async (HttpContext context, CurrentMembership membership, SessionRevocationService revocation, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct);
            await revocation.RevokeAsync(actor.UserId, RevocationCause.Logout, actor, cancellationToken: ct);
            await context.SignOutAsync(IdentityConstants.ApplicationScheme);
            return TypedResults.NoContent();
        }).RequireAuthorization(IdentityConfiguration.MemberPolicy).WithName("LogoutAllSessions");
        api.MapGet("/profile", async (HttpContext context, CurrentMembership membership, SettingsService settings, CancellationToken ct) =>
        {
            var result = await settings.ProfileAsync(await Actor(membership, ct), ct);
            return Read(context, result, result.Id, result.Revision);
        }).RequireAuthorization(IdentityConfiguration.MemberPolicy).WithName("GetProfile").WithMetadata(new AggregateContract(ReturnsETag: true, ConditionalGet: true)).Produces<ProfileRead>().Produces(304);
        api.MapPut("/profile", async (ProfileWrite request, HttpContext context, CurrentMembership membership, SettingsService settings, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct);
            var revision = TransportConventions.RequiredRevision(context.Request.Headers.IfMatch, actor.UserId);
            var result = await settings.UpdateProfileAsync(actor, revision, request.Name, request.Preferences, ct);
            return Write(context, result, result.Id, result.Revision);
        }).RequireAuthorization(IdentityConfiguration.MemberPolicy).WithName("UpdateProfile").WithMetadata(new AggregateContract(RequiresIfMatch: true, ReturnsETag: true)).Produces<ProfileRead>();
        api.MapPost("/profile/password", async (PasswordWrite request, HttpContext context, CurrentMembership membership, SettingsService settings, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct);
            await settings.ChangePasswordAsync(actor, TransportConventions.RequiredRevision(context.Request.Headers.IfMatch, actor.UserId), request.CurrentPassword, request.NewPassword, ct);
            await context.SignOutAsync(IdentityConstants.ApplicationScheme);
            return TypedResults.NoContent();
        }).RequireAuthorization(IdentityConfiguration.MemberPolicy).RequireRateLimiting("credentials").WithName("ChangePassword").WithMetadata(new AggregateContract(RequiresIfMatch: true));
        api.MapGet("/household/settings", async (HttpContext context, CurrentMembership membership, SettingsService settings, CancellationToken ct) =>
        {
            var result = await settings.HouseholdAsync(await Actor(membership, ct), ct);
            return Read(context, result, result.Id, result.Revision);
        }).RequireAuthorization(IdentityConfiguration.MemberPolicy).WithName("GetHouseholdSettings").WithMetadata(new AggregateContract(ReturnsETag: true, ConditionalGet: true)).Produces<HouseholdSettingsRead>().Produces(304);
        api.MapPut("/household/settings", async (HouseholdSettingsWrite request, HttpContext context, CurrentMembership membership, SettingsService settings, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct);
            var result = await settings.UpdateHouseholdAsync(actor, TransportConventions.RequiredRevision(context.Request.Headers.IfMatch, actor.HouseholdId), request.Name, request.Timezone, request.Ai, ct);
            return Write(context, result, result.Id, result.Revision);
        }).RequireAuthorization(IdentityConfiguration.AdministratorPolicy).WithName("UpdateHouseholdSettings").WithMetadata(new AggregateContract(RequiresIfMatch: true, ReturnsETag: true)).Produces<HouseholdSettingsRead>();
        api.MapGet("/household/members", async (int? limit, string? cursor, CurrentMembership membership, MembershipService members, CancellationToken ct) =>
        {
            var rows = await members.MembersAsync(await Actor(membership, ct), ct, TransportConventions.ReadCursor(cursor), TransportConventions.PageSize(limit));
            return TypedResults.Ok(new MemberPage(rows.Items.Select(x => new MemberRead(x.Id, x.UserId, x.Email, x.Name, x.Role, x.Revision, TransportConventions.ETag(x.Id, x.Revision))).ToArray(), rows.NextId is { } next ? TransportConventions.Cursor(next) : null));
        }).RequireAuthorization(IdentityConfiguration.MemberPolicy).WithName("GetHouseholdMembers");
        api.MapDelete("/household/members/{id:guid}", async (Guid id, HttpContext context, CurrentMembership membership, MembershipService members, RecipeManagerDbContext database, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct);
            _ = await database.Memberships.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.IsActive, ct) ?? throw new ResourceMissingException();
            await members.RemoveAsync(actor, id, TransportConventions.RequiredRevision(context.Request.Headers.IfMatch, id), ct);
            return TypedResults.NoContent();
        }).RequireAuthorization(IdentityConfiguration.AdministratorPolicy).WithName("RemoveHouseholdMember").WithMetadata(new AggregateContract(RequiresIfMatch: true));
        api.MapPost("/household/invitations", async (IssueInvitationWrite request, HttpContext context, CurrentMembership membership, MembershipService members, CancellationToken ct) =>
        {
            Guid? retryKey = null;
            if (context.Request.Headers.TryGetValue("Idempotency-Key", out var supplied))
            {
                if (!Guid.TryParse(supplied, out var parsed) || parsed == Guid.Empty) throw new ApiContractException(422, "invalid_idempotency_key");
                retryKey = parsed;
            }
            var issued = await members.IssueAsync(await Actor(membership, ct), ct, retryKey);
            context.Response.Headers.ETag = TransportConventions.ETag(issued.Id, issued.Revision);
            return TypedResults.Created("/api/v1/household/invitations/" + issued.Id,
                new InvitationCreated(issued.Id, issued.Token, "/invite?token=" + Uri.EscapeDataString(issued.Token), issued.ExpiresAt, issued.Revision));
        }).RequireAuthorization(IdentityConfiguration.AdministratorPolicy).RequireRateLimiting("credentials").WithName("CreateInvitation").WithMetadata(new RetryableCreationContract(), new AggregateContract(ReturnsETag: true));
        api.MapGet("/household/invitations", async (int? limit, string? cursor, CurrentMembership membership, MembershipService members, CancellationToken ct) =>
        {
            var rows = await members.InvitationsAsync(await Actor(membership, ct), ct, TransportConventions.ReadCursor(cursor), TransportConventions.PageSize(limit));
            return TypedResults.Ok(new InvitationPage(rows.Items.Select(x => new InvitationRead(x.Id, x.ExpiresAt, x.AcceptedAt, x.RevokedAt, x.Revision, TransportConventions.ETag(x.Id, x.Revision))).ToArray(), rows.NextId is { } next ? TransportConventions.Cursor(next) : null));
        }).RequireAuthorization(IdentityConfiguration.AdministratorPolicy).WithName("GetInvitations");
        api.MapDelete("/household/invitations/{id:guid}", async (Guid id, HttpContext context, CurrentMembership membership, MembershipService members, RecipeManagerDbContext database, CancellationToken ct) =>
        {
            var actor = await Actor(membership, ct);
            _ = await database.Invitations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ResourceMissingException();
            await members.RevokeInvitationAsync(actor, id, TransportConventions.RequiredRevision(context.Request.Headers.IfMatch, id), ct);
            return TypedResults.NoContent();
        }).RequireAuthorization(IdentityConfiguration.AdministratorPolicy).WithName("RevokeInvitation").WithMetadata(new AggregateContract(RequiresIfMatch: true));
        api.MapPost("/invitations/accept", async (AcceptInvitationRequest request, HttpContext context, MembershipService members,
            UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, TimeProvider clock, CancellationToken ct) =>
        {
            if (context.User.Identity?.IsAuthenticated == true) throw new HouseholdConflictException("sign_out_before_accepting");
            var result = await members.AcceptAsync(request, ct);
            await SignIn(result.Id, context, users, signIn, clock);
            return TypedResults.Created("/api/v1/auth/me", result);
        }).AllowAnonymous().RequireRateLimiting("credentials").WithName("AcceptInvitation");
    }

    private static async Task<IResult> Login(LoginWrite request, HttpContext context, UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn, RecipeManagerDbContext database, SessionRevocationService revocation, TimeProvider clock)
    {
        AccountRules.Password(request.Password);
        var user = await users.FindByEmailAsync(AccountRules.Email(request.Email));
        if (user is null || !user.IsActive || user.PasswordHash is null || await users.IsLockedOutAsync(user)) throw new ApiContractException(401, "invalid_credentials");
        var verified = users.PasswordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verified == PasswordVerificationResult.Failed)
        {
            _ = await users.AccessFailedAsync(user);
            throw new ApiContractException(401, "invalid_credentials");
        }
        var member = await database.Memberships.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.UserId == user.Id && x.IsActive);
        if (member is null) throw new ApiContractException(401, "invalid_credentials");
        if (verified == PasswordVerificationResult.SuccessRehashNeeded)
        {
            // Identity's implicit rehash would rotate stamps outside the fence. Rehash through it.
            var actor = new RequestActor(user.Id, member.HouseholdId, user.SecurityStamp!, clock.GetUtcNow().AddDays(14));
            await revocation.RevokeAsync(user.Id, RevocationCause.PasswordChange, actor, request.Password, request.Password);
        }
        user = await users.FindByIdAsync(user.Id.ToString()) ?? throw new ApiContractException(401, "invalid_credentials");
        if (user.AccessFailedCount > 0) _ = await users.ResetAccessFailedCountAsync(user);
        await SignIn(user.Id, context, users, signIn, clock);
        return TypedResults.Ok(new HouseholdSession(user.Id, member.HouseholdId, member.Role));
    }
    private static async Task SignIn(Guid id, HttpContext context, UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, TimeProvider clock)
    {
        var user = await users.FindByIdAsync(id.ToString()) ?? throw new TransactionAccessException();
        await signIn.SignInAsync(user, new AuthenticationProperties { IsPersistent = true, AllowRefresh = false, ExpiresUtc = clock.GetUtcNow().AddDays(14) });
        context.Response.Headers.CacheControl = "no-store";
    }
    private static async Task<RequestActor> Actor(CurrentMembership membership, CancellationToken ct) => await membership.ResolveActorAsync(ct) ?? throw new TransactionAccessException();
    private static IResult Read<T>(HttpContext context, T body, Guid id, long revision)
    {
        var etag = TransportConventions.ETag(id, revision);
        context.Response.Headers.ETag = etag;
        context.Response.Headers.CacheControl = "private, no-cache";
        return context.Request.Headers.IfNoneMatch == etag ? Results.StatusCode(304) : TypedResults.Ok(body);
    }
    private static IResult Write<T>(HttpContext context, T body, Guid id, long revision)
    {
        context.Response.Headers.ETag = TransportConventions.ETag(id, revision);
        context.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(body);
    }
}
