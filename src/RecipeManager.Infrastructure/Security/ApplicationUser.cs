using Microsoft.AspNetCore.Identity;
using RecipeManager.Core.Shared;

namespace RecipeManager.Infrastructure.Security;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    private ApplicationUser() { }

    public ApplicationUser(Guid id, string email)
    {
        Id = DomainRules.NonEmptyId(id, "userId");
        Email = email;
        UserName = email;
        NormalizedEmail = email.ToUpperInvariant();
        NormalizedUserName = NormalizedEmail;
        SecurityStamp = Guid.NewGuid().ToString("N");
    }

    public bool IsActive { get; private set; } = true;
    public string DisplayName { get; private set; } = "";
    public string PreferencesJson { get; private set; } = "{}";
    public long PreferencesRevision { get; private set; }
}
