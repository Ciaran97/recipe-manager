using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using RecipeManager.Core.Shared;

namespace RecipeManager.Api.Contracts;

public static class TransportConventions
{
    public static void Configure(JsonSerializerOptions options)
    {
        options.NumberHandling = JsonNumberHandling.Strict;
        options.Converters.Add(new StringDecimalConverter());
        options.Converters.Add(new StringRevisionConverter());
        options.Converters.Add(new StringIntegerConverter());
        options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
    }

    public static string ETag(Guid id, long revision) => $"\"{id:N}:{DomainRules.Revision(revision)}\"";
    public static void RequireMatch(string? supplied, string current)
    {
        if (string.IsNullOrWhiteSpace(supplied)) throw new ApiContractException(428, "precondition_required");
        if (!string.Equals(supplied, current, StringComparison.Ordinal)) throw new ApiContractException(412, "stale_revision");
    }
    public static long RequiredRevision(string? supplied, Guid resourceId)
    {
        if (string.IsNullOrWhiteSpace(supplied)) throw new ApiContractException(428, "precondition_required");
        var prefix = $"\"{resourceId:N}:";
        if (!supplied.StartsWith(prefix, StringComparison.Ordinal) || !supplied.EndsWith('"')
            || !long.TryParse(supplied.AsSpan(prefix.Length, supplied.Length - prefix.Length - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var revision)
            || supplied != ETag(resourceId, revision)) throw new ApiContractException(412, "stale_revision");
        return revision;
    }

    public static int PageSize(int? limit)
        => limit is null ? 25 : limit is >= 1 and <= 100 ? limit.Value : throw new DomainValidationException("limit", "range", "Limit must be between 1 and 100.");
    public static Guid? ReadCursor(string? cursor)
    {
        if (cursor is null) return null;
        try
        {
            var bytes = Convert.FromBase64String(cursor);
            if (bytes.Length != 16) throw new FormatException();
            return DomainRules.NonEmptyId(new Guid(bytes), "cursor");
        }
        catch (FormatException) { throw new DomainValidationException("cursor", "invalid", "Invalid pagination cursor."); }
    }
    public static string Cursor(Guid id) => Convert.ToBase64String(DomainRules.NonEmptyId(id, "cursor").ToByteArray());
}

public sealed class ApiContractException(int status, string code) : Exception
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public sealed class StringDecimalConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || !decimal.TryParse(reader.GetString(),
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            throw new JsonException("A decimal string is required.");
        return value;
    }
    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString("0.############################", CultureInfo.InvariantCulture));
}

public sealed class StringRevisionConverter : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || !long.TryParse(reader.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            throw new JsonException("A nonnegative revision string is required.");
        return value;
    }
    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
        => writer.WriteStringValue(DomainRules.Revision(value).ToString(CultureInfo.InvariantCulture));
}

public sealed class StringIntegerConverter : JsonConverter<BigInteger>
{
    public override BigInteger Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || reader.GetString() is not { Length: <= 128 } text
            || !BigInteger.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
            throw new JsonException("A bounded integer string is required.");
        return value;
    }
    public override void Write(Utf8JsonWriter writer, BigInteger value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
}
