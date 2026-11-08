using System.Security.Cryptography;
using System.Text;
using RecipeManager.Core.Shared;

namespace RecipeManager.Core.Modules.Imports;

public enum ImportSourceKind { Url, Text }

public static class ImportInput
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public static void Validate(ImportSourceKind kind, string content)
    {
        if (!Enum.IsDefined(kind) || string.IsNullOrWhiteSpace(content))
            throw new DomainValidationException("input", "required", "Choose a source and provide recipe content.");
        try
        {
            if (Utf8.GetByteCount(content) > 100 * 1024)
                throw new DomainValidationException("input", "too_long", "Recipe text must not exceed 100 KiB of UTF-8.");
        }
        catch (EncoderFallbackException)
        {
            throw new DomainValidationException("input", "invalid_unicode", "Recipe content must contain valid Unicode text.");
        }
        if (kind == ImportSourceKind.Url) _ = DomainRules.HttpUrl(content, "input");
    }
    public static string Hash(ImportSourceKind kind, string content)
    {
        Validate(kind, content);
        var prefix = kind == ImportSourceKind.Url ? "url" : "text";
        return Convert.ToHexString(SHA256.HashData(Utf8.GetBytes(prefix + "\0" + content)));
    }
    internal static string Digest(string value, string field)
    {
        if (value is null || value.Length != 64 || !value.All(Uri.IsHexDigit))
            throw new DomainValidationException(field, "invalid_hash", "A SHA-256 digest is required.");
        return value.ToUpperInvariant();
    }
}
