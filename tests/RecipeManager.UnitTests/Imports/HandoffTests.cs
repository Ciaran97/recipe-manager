using RecipeManager.Core.Modules.Imports;
using RecipeManager.Core.Shared;

namespace RecipeManager.UnitTests.Imports;

public sealed class HandoffTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);
    private static ImportHandoff Capture(DateTimeOffset? captured = null)
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), captured ?? Now, Now,
            ImportInput.Hash(ImportSourceKind.Text, "salt to taste"));

    [Fact]
    public void Expiry_is_fixed_from_capture_and_cannot_be_extended_by_registration()
    {
        var handoff = Capture(Now.AddDays(-6));
        Assert.Equal(Now.AddDays(1), handoff.ExpiresAt);
        handoff.RequireOpen(Now.AddHours(23));
        Assert.Throws<ImportStateException>(() => handoff.RequireOpen(Now.AddDays(1)));
        Assert.Equal(ImportHandoffState.Expired, handoff.State);
        Assert.Equal(Now.AddDays(1), handoff.ExpiresAt);
    }

    [Fact]
    public void Committed_receipt_survives_expiry_and_rejects_changed_commit_without_mutation()
    {
        var handoff = Capture();
        var result = new ImportCommitResult(Guid.NewGuid(), 1, Now);
        var hash = ImportInput.Hash(ImportSourceKind.Text, "reviewed body");
        handoff.Commit(hash, result, Now);
        Assert.Equal(result, handoff.Replay(hash));
        Assert.Equal(result, handoff.Result);
        Assert.Equal(ImportHandoffState.Committed, handoff.State);
        Assert.Throws<ImportStateException>(() => handoff.Replay(ImportInput.Hash(ImportSourceKind.Text, "changed")));
        Assert.Equal(result, handoff.Result);
        Assert.Equal(result, handoff.Replay(hash)); // No draft or clock dependency on replay.
        Assert.Throws<ImportStateException>(() => handoff.Commit(hash, result with { RecipeId = Guid.NewGuid() }, Now.AddDays(8)));
    }

    [Theory]
    [InlineData(-7, 0)]
    [InlineData(0, 6)]
    public void Old_or_future_capture_is_rejected(int days, int minutes)
        => Assert.Throws<ImportStateException>(() => Capture(Now.AddDays(days).AddMinutes(minutes)));

    [Fact]
    public void Hash_preserves_kind_whitespace_and_original_unicode_bytes()
    {
        Assert.NotEqual(ImportInput.Hash(ImportSourceKind.Text, "café"), ImportInput.Hash(ImportSourceKind.Text, "cafe\u0301"));
        Assert.NotEqual(ImportInput.Hash(ImportSourceKind.Url, "https://example.org/"), ImportInput.Hash(ImportSourceKind.Text, "https://example.org/"));
        Assert.NotEqual(ImportInput.Hash(ImportSourceKind.Text, "salt"), ImportInput.Hash(ImportSourceKind.Text, "salt "));
        Assert.Equal(64, ImportInput.Hash(ImportSourceKind.Text, "salt").Length);
    }

    [Fact]
    public void Input_bound_is_utf8_bytes_and_invalid_surrogates_are_rejected()
    {
        ImportInput.Validate(ImportSourceKind.Text, new string('é', 51200));
        Assert.Throws<DomainValidationException>(() => ImportInput.Validate(ImportSourceKind.Text, new string('é', 51201)));
        Assert.Throws<DomainValidationException>(() => ImportInput.Validate(ImportSourceKind.Text, "\ud800"));
        Assert.Throws<DomainValidationException>(() => ImportInput.Validate(ImportSourceKind.Url, "file:///private/recipe"));
    }
}
