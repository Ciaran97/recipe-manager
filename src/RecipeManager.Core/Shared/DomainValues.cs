using System.Globalization;

namespace RecipeManager.Core.Shared;

public sealed class DomainValidationException(string field, string code, string message) : ArgumentException(message, field)
{
    public string Code { get; } = code;
    public string UserMessage { get; } = message;
}

public readonly record struct TenantId
{
    public Guid Value { get; }
    public TenantId(Guid value) => Value = DomainRules.NonEmptyId(value, "householdId");
}

public readonly record struct UserId
{
    public Guid Value { get; }
    public UserId(Guid value) => Value = DomainRules.NonEmptyId(value, "userId");
}

public readonly record struct AggregateRevision
{
    public long Value { get; }
    public AggregateRevision(long value) => Value = DomainRules.Revision(value);
    public AggregateRevision Next() => new(checked(Value + 1));
}

public static class DomainRules
{
    public static Guid NonEmptyId(Guid value, string field)
    {
        if (value == Guid.Empty)
        {
            throw new DomainValidationException(field, "required", "A non-empty UUID is required.");
        }

        return value;
    }
    public static int Position(int value, string field = "position")
    {
        if (value < 0) throw new DomainValidationException(field, "range", "Positions must be nonnegative.");
        return value;
    }
    public static long Revision(long value)
    {
        if (value < 0)
        {
            throw new DomainValidationException("revision", "invalid_revision", "Revision must not be negative.");
        }

        return value;
    }
    public static string Name(string? value, string field = "name")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException(field, "required", "A name is required.");
        }

        var name = value.Trim();
        if (name.Length > 200)
        {
            throw new DomainValidationException(field, "too_long", "The maximum length is 200 characters.");
        }

        return name;
    }
    public static string? Description(string? value, string field = "description")
    {
        if (value?.Length > 20000)
        {
            throw new DomainValidationException(field, "too_long", "The maximum length is 20000 characters.");
        }

        return value;
    }
    public static string? HttpUrl(string? value, string field = "sourceUrl")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.Length > 2048)
        {
            throw new DomainValidationException(field, "too_long", "The maximum URL length is 2048 characters.");
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || uri.UserInfo.Length != 0)
        {
            throw new DomainValidationException(field, "invalid_url", "Use an HTTP or HTTPS URL without embedded credentials.");
        }

        return value;
    }
}

public readonly record struct MealDateRange
{
    public DateOnly From { get; }
    public DateOnly To { get; }
    public MealDateRange(DateOnly from, DateOnly to)
    {
        if (to < from || to.DayNumber - from.DayNumber + 1 > 366)
        {
            throw new DomainValidationException("dateRange", "invalid_range", "Choose an inclusive range of one to 366 days.");
        }

        From = from;
        To = to;
    }
    public int Days => To.DayNumber - From.DayNumber + 1;
}

public static class HouseholdDates
{
    public static DateOnly Parse(string value)
    {
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new DomainValidationException("date", "invalid_date", "Use an ISO calendar date (YYYY-MM-DD).");
        }

        return date;
    }
    public static TimeZoneInfo TimeZone(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || (value != "UTC" && !TimeZoneInfo.TryConvertIanaIdToWindowsId(value, out _))
            || !TimeZoneInfo.TryFindSystemTimeZoneById(value, out var zone))
        {
            throw new DomainValidationException("timeZone", "invalid_time_zone", "Choose a valid IANA time-zone identifier.");
        }

        return zone;
    }
    public static DateOnly Today(TimeProvider clock, string timeZone)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZone(timeZone)).DateTime);
}
