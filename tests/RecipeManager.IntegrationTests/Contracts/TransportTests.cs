using System.Numerics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RecipeManager.Api.Contracts;
using RecipeManager.Core.Shared;

namespace RecipeManager.IntegrationTests.Contracts;

public sealed class TransportTests
{
    private static JsonSerializerOptions Options()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        TransportConventions.Configure(options);
        return options;
    }
    [Fact]
    public void Amounts_and_revisions_use_strings_without_binary_float_loss()
    {
        var value = new Values(0.123456m, 9007199254740993L, BigInteger.Parse("9999999999999999999999999999"));
        var json = JsonSerializer.Serialize(value, Options());
        Assert.Contains("\"amount\":\"0.123456\"", json);
        Assert.Contains("\"revision\":\"9007199254740993\"", json);
        Assert.Equal(value, JsonSerializer.Deserialize<Values>(json, Options()));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Values>("{\"amount\":0.5,\"revision\":\"1\",\"numerator\":\"1\"}", Options()));
    }
    [Fact]
    public void Decimal_responses_remove_storage_padding_without_rounding_or_exponents()
    {
        Assert.Equal("\"2\"", JsonSerializer.Serialize(2.000000m, Options()));
        Assert.Equal("\"0.5\"", JsonSerializer.Serialize(0.500000m, Options()));
        Assert.Equal("\"0.000001\"", JsonSerializer.Serialize(0.000001m, Options()));
        Assert.Equal("\"0\"", JsonSerializer.Serialize(0.000000m, Options()));
    }
    [Fact]
    public void Missing_and_stale_revisions_have_distinct_statuses()
    {
        var etag = TransportConventions.ETag(Guid.NewGuid(), 2);
        Assert.Equal(428, Assert.Throws<ApiContractException>(() => TransportConventions.RequireMatch(null, etag)).Status);
        Assert.Equal(412, Assert.Throws<ApiContractException>(() => TransportConventions.RequireMatch("*", etag)).Status);
        Assert.Equal(412, Assert.Throws<ApiContractException>(() => TransportConventions.RequireMatch("W/" + etag, etag)).Status);
        TransportConventions.RequireMatch(etag, etag);
    }
    [Fact]
    public void Pagination_and_calendar_bounds_are_enforced()
    {
        Assert.Equal(25, TransportConventions.PageSize(null));
        Assert.Equal(100, TransportConventions.PageSize(100));
        Assert.Throws<DomainValidationException>(() => TransportConventions.PageSize(101));
        Assert.Throws<DomainValidationException>(() => TransportConventions.PageSize(0));
        var id = Guid.NewGuid();
        Assert.Equal(id, TransportConventions.ReadCursor(TransportConventions.Cursor(id)));
        Assert.Throws<DomainValidationException>(() => TransportConventions.ReadCursor("invalid"));
        _ = new MealDateRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));
        Assert.Throws<DomainValidationException>(() => new MealDateRange(new DateOnly(2024, 1, 1), new DateOnly(2025, 1, 1)));
    }
    [Fact]
    public async Task Unexpected_errors_never_return_exception_details_or_secrets()
    {
        var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Response.Body = new MemoryStream();
        var secret = "private-provider-key-fixture";
        var handler = new ApiExceptionHandler(scope.ServiceProvider.GetRequiredService<IProblemDetailsService>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ApiExceptionHandler>.Instance);
        Assert.True(await handler.TryHandleAsync(context, new Exception(secret), CancellationToken.None));
        Assert.Equal(500, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var json = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.DoesNotContain(secret, json);
        Assert.Contains("unexpected_error", json);
        Assert.Contains("traceId", json);
    }

    private sealed record Values(decimal Amount, long Revision, BigInteger Numerator);
}
