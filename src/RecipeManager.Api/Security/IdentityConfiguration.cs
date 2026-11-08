using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.Infrastructure.Security;

namespace RecipeManager.Api.Security;

public static class IdentityConfiguration
{
    public const string CookieName = "RecipeManager.Session";
    public const string MemberPolicy = "HouseholdMember";
    public const string AdministratorPolicy = "HouseholdAdministrator";

    public static IServiceCollection AddRecipeManagerAuthentication(this IServiceCollection services)
    {
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.AddScoped<CurrentMembership>();
        services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.SignIn.RequireConfirmedEmail = false; // Invitation-only; no mandatory email provider.
            options.Password.RequiredLength = 12;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireDigit = false;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }).AddEntityFrameworkStores<RecipeManagerDbContext>()
            .AddSignInManager().AddDefaultTokenProviders();
        services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromMinutes(15));
        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddCookie(IdentityConstants.ApplicationScheme, options =>
        {
            options.Cookie.Name = CookieName;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.IsEssential = true;
            options.ExpireTimeSpan = TimeSpan.FromDays(14);
            options.SlidingExpiration = false; // Absolute lifetime; a fresh sign-in issues a new ticket.
            options.Events = new CookieAuthenticationEvents
            {
                OnValidatePrincipal = async context =>
                {
                    var membership = context.HttpContext.RequestServices.GetRequiredService<CurrentMembership>();
                    if (await membership.ResolveAsync(context.Principal, context.HttpContext.RequestAborted) is null)
                    {
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                    }
                },
                OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; },
                OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; }
            };
        });
        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<TimeProvider>((options, clock) => options.TimeProvider = clock);
        services.AddScoped<IAuthorizationHandler, HouseholdAuthorizationHandler>();
        services.AddAuthorizationBuilder()
            .AddPolicy(MemberPolicy, policy => policy.RequireAuthenticatedUser().AddRequirements(new HouseholdRequirement(false)))
            .AddPolicy(AdministratorPolicy, policy => policy.RequireAuthenticatedUser().AddRequirements(new HouseholdRequirement(true)))
            .SetFallbackPolicy(new AuthorizationPolicyBuilder(IdentityConstants.ApplicationScheme)
                .RequireAuthenticatedUser().AddRequirements(new HouseholdRequirement(false)).Build());
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-XSRF-TOKEN";
            options.Cookie.Name = "RecipeManager.Csrf";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });
        return services;
    }
}

internal sealed record HouseholdRequirement(bool AdministratorOnly) : IAuthorizationRequirement;

internal sealed class HouseholdAuthorizationHandler(CurrentMembership membership, IHttpContextAccessor accessor)
    : AuthorizationHandler<HouseholdRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, HouseholdRequirement requirement)
    {
        var actor = await membership.ResolveAsync(context.User, accessor.HttpContext?.RequestAborted ?? CancellationToken.None);
        if (actor is not null && (!requirement.AdministratorOnly || actor.Role == HouseholdRole.Administrator))
        {
            context.Succeed(requirement);
        }
    }
}
