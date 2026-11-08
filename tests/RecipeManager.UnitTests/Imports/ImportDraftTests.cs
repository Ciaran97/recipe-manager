using RecipeManager.Core.Modules.Imports;
using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Core.Shared;

namespace RecipeManager.UnitTests.Imports;

public sealed class ImportDraftTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);
    private static ImportDraft Draft() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
        ImportSourceKind.Text, "salt to taste", "{\"title\":\"Soup\"}", "[\"ingredients[0].quantity\"]", null, Now);

    [Fact]
    public void Unknown_source_standard_requires_explicit_review_and_expiry_never_slides()
    {
        var draft = Draft();
        Assert.Null(draft.MeasurementStandard);
        Assert.Throws<DomainValidationException>(() => draft.RequireReviewed(Now));
        draft.Review("{\"title\":\"Corrected soup\"}", "[]", MeasurementStandard.AU, Now.AddHours(23));
        draft.RequireReviewed(Now.AddHours(23));
        Assert.Equal(Now.AddHours(24), draft.ExpiresAt);
        Assert.Equal(2, draft.Revision);
        Assert.Equal("salt to taste", draft.OriginalInput);
        Assert.Throws<ImportStateException>(() => draft.RequireReviewed(Now.AddHours(24)));
    }

    [Fact]
    public void Known_standard_does_not_replace_explicit_review()
    {
        var draft = new ImportDraft(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, ImportSourceKind.Url,
            "https://example.org/recipe", "{}", "[]", MeasurementStandard.US, Now);
        Assert.Throws<DomainValidationException>(() => draft.RequireReviewed(Now));
        draft.Review("{}", "[]", MeasurementStandard.US, Now);
        draft.RequireReviewed(Now);
    }

    [Fact]
    public void Invalid_or_oversized_extraction_does_not_replace_the_previous_review()
    {
        var draft = Draft();
        Assert.Throws<DomainValidationException>(() => draft.Review("[]", "[]", MeasurementStandard.AU, Now));
        Assert.Throws<DomainValidationException>(() => draft.Review("{}", "{}", MeasurementStandard.AU, Now));
        Assert.Throws<DomainValidationException>(() => draft.Review("{\"text\":\"" + new string('é', 524288) + "\"}", "[]", MeasurementStandard.AU, Now));
        Assert.Equal(1, draft.Revision);
        Assert.Null(draft.ReviewedAt);
        Assert.Null(draft.MeasurementStandard);
    }
}
