using System.Net.Mail;
using RecipeManager.Core.Shared;

namespace RecipeManager.Core.Modules.Households;

public sealed record SetupRequest(string SetupToken, string Email, string Password, string HouseholdName, string Timezone);
public sealed record HouseholdSession(Guid Id, Guid HouseholdId, HouseholdRole Role);
public interface ISetupHouseholdStore
{
    Task<bool> SetupRequiredAsync(CancellationToken cancellationToken);
    Task<HouseholdSession> CreateAsync(SetupRequest request, CancellationToken cancellationToken);
}

public sealed class HouseholdConflictException(string code) : Exception
{
    public string Code { get; } = code;
}

public sealed class SetupHousehold(ISetupHouseholdStore store)
{
    public Task<bool> SetupRequiredAsync(CancellationToken cancellationToken = default) => store.SetupRequiredAsync(cancellationToken);
    public Task<HouseholdSession> CreateAsync(SetupRequest request, CancellationToken cancellationToken = default)
    {
        var email = AccountRules.Email(request.Email);
        AccountRules.Password(request.Password);
        var name = DomainRules.Name(request.HouseholdName);
        var timezone = HouseholdDates.TimeZone(request.Timezone).Id;
        if (string.IsNullOrWhiteSpace(request.SetupToken) || request.SetupToken.Length > 1024)
            throw new DomainValidationException("setupToken", "invalid", "An operator setup secret is required.");
        return store.CreateAsync(request with { Email = email, HouseholdName = name, Timezone = timezone }, cancellationToken);
    }
}

public static class AccountRules
{
    public static string Email(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 254 || !MailAddress.TryCreate(value.Trim(), out var parsed)
            || !string.Equals(parsed.Address, value.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new DomainValidationException("email", "invalid", "A valid email address is required.");
        return parsed.Address;
    }
    public static void Password(string? value)
    {
        if (value is not { Length: >= 12 and <= 256 })
            throw new DomainValidationException("password", "length", "Use a password between 12 and 256 characters.");
    }
}
