using RecipeManager.Core.Shared;

namespace RecipeManager.UnitTests.Shared;

public sealed class DomainValuesTests
{
    [Fact]
    public void Tenant_and_user_ids_reject_empty_values()
    {
        Assert.Throws<DomainValidationException>(() => new TenantId(Guid.Empty));
        Assert.Throws<DomainValidationException>(() => new UserId(Guid.Empty));
    }

    [Fact]
    public void Tenant_and_user_ids_preserve_valid_values()
    {
        var id = Guid.NewGuid();
        Assert.Equal(id, new TenantId(id).Value);
        Assert.Equal(id, new UserId(id).Value);
    }

    [Fact]
    public void Revision_rejects_negative_values_and_advances_without_mutating_original()
    {
        Assert.Throws<DomainValidationException>(() => new AggregateRevision(-1));
        var revision = new AggregateRevision(1);
        Assert.Equal(2, revision.Next().Value);
        Assert.Equal(1, revision.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Names_are_required(string? value) => Assert.Throws<DomainValidationException>(() => DomainRules.Name(value));

    [Fact]
    public void Names_trim_surrounding_space_and_enforce_200_characters()
    {
        Assert.Equal("Dinner", DomainRules.Name(" Dinner "));
        Assert.Equal(200, DomainRules.Name(new string('a', 200)).Length);
        Assert.Throws<DomainValidationException>(() => DomainRules.Name(new string('a', 201)));
    }

    [Fact]
    public void Optional_notes_retain_original_content_and_enforce_20000_characters()
    {
        Assert.Null(DomainRules.Description(null));
        Assert.Equal("  keep notes  ", DomainRules.Description("  keep notes  "));
        Assert.Equal(20000, DomainRules.Description(new string('n', 20000))!.Length);
        Assert.Throws<DomainValidationException>(() => DomainRules.Description(new string('n', 20001)));
    }

    [Theory]
    [InlineData("https://example.test/recipe")]
    [InlineData("http://example.test/recipe")]
    public void Source_urls_preserve_valid_http_addresses(string url) => Assert.Equal(url, DomainRules.HttpUrl(url));

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:password@example.test/recipe")]
    [InlineData("not a URL")]
    public void Source_urls_reject_unsafe_or_invalid_addresses(string url) => Assert.Throws<DomainValidationException>(() => DomainRules.HttpUrl(url));

    [Fact]
    public void Source_url_is_optional_and_bounded()
    {
        Assert.Null(DomainRules.HttpUrl(null));
        Assert.Throws<DomainValidationException>(() => DomainRules.HttpUrl("https://example.test/" + new string('a', 2048)));
    }

    [Fact]
    public void Calendar_range_is_inclusive_and_allows_at_most_366_days()
    {
        var range = new MealDateRange(new(2024, 1, 1), new(2024, 12, 31));
        Assert.Equal(366, range.Days);
        Assert.Throws<DomainValidationException>(() => new MealDateRange(new(2024, 1, 1), new(2025, 1, 1)));
        Assert.Throws<DomainValidationException>(() => new MealDateRange(new(2024, 1, 2), new(2024, 1, 1)));
    }

    [Fact]
    public void Calendar_dates_use_iso_input_and_the_household_time_zone()
    {
        Assert.Equal(new DateOnly(2026, 11, 1), HouseholdDates.Parse("2026-11-01"));
        Assert.Throws<DomainValidationException>(() => HouseholdDates.Parse("01/11/2026"));
        Assert.Throws<DomainValidationException>(() => HouseholdDates.TimeZone("Invalid/Zone"));
        var clock = new FixedClock(DateTimeOffset.Parse("2026-11-01T18:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(new DateOnly(2026, 11, 2), HouseholdDates.Today(clock, "Australia/Perth"));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
