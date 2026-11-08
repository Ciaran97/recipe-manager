using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Modules.Recipes;
using RecipeManager.Core.Modules.Recipes.Contracts;
using RecipeManager.Infrastructure.Persistence;
using SkiaSharp;

namespace RecipeManager.Infrastructure.Files;

public sealed record RecipeImageRead(Guid Id, string MediaType, long Size);
public sealed record RecipeImageFile(Stream Content, string MediaType, string ETag);
public sealed class RecipeImageException(int status, string code) : Exception
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public sealed class RecipeImageDirectory(string root, bool ephemeral = false) : IDisposable
{
    public string Root { get; } = Path.GetFullPath(root);
    public string FilePath(Guid householdId, string key)
    {
        if (!Guid.TryParseExact(key, "N", out _) || key != key.ToLowerInvariant()) throw new InvalidOperationException("Invalid stored image key.");
        return Path.Combine(Root, householdId.ToString("N"), key);
    }
    public void Prepare(Guid householdId)
    {
        Directory.CreateDirectory(Root);
        var directory = Path.Combine(Root, householdId.ToString("N"));
        Directory.CreateDirectory(directory);
        foreach (var path in new[] { Root, directory })
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Image storage must not be a symbolic link.");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
    public void Dispose()
    {
        if (ephemeral && Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
    }
}

public sealed class RecipeImages(RecipeManagerDbContext database, TransactionBoundary transactions, CreationRetryStore retries,
    RecipeImageDirectory directory, IEnumerable<IImageReferences> references, ILogger<RecipeImages> logger)
{
    public const int MaximumUploadBytes = 10 * 1024 * 1024;
    public const long MaximumPixels = 20_000_000;
    public async Task<RecipeImageRead> UploadAsync(RequestActor actor, Stream source, string mediaType, Guid? retryKey, CancellationToken cancellationToken)
    {
        if (mediaType is not ("image/jpeg" or "image/png" or "image/webp")) throw new RecipeImageException(415, "unsupported_image");
        using var input = new MemoryStream();
        var buffer = new byte[65536];
        while (true)
        {
            var count = await source.ReadAsync(buffer, cancellationToken);
            if (count == 0) break;
            if (input.Length + count > MaximumUploadBytes) throw new RecipeImageException(413, "image_too_large");
            await input.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
        var bytes = input.ToArray();
        var encoded = Sanitize(bytes, mediaType);
        cancellationToken.ThrowIfCancellationRequested();
        var asset = new ImageAsset(Guid.NewGuid(), actor.HouseholdId, Guid.NewGuid().ToString("N"), mediaType, encoded.Length);
        directory.Prepare(actor.HouseholdId);
        var path = directory.FilePath(actor.HouseholdId, asset.StorageKey);
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.Asynchronous | FileOptions.WriteThrough };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using (var file = new FileStream(path, options))
        {
            await file.WriteAsync(encoded, cancellationToken); await file.FlushAsync(cancellationToken); file.Flush(flushToDisk: true);
        }
        // Bytes are durable before metadata. On an ambiguous DB commit failure retain the private
        // file: deleting it could corrupt a successfully committed asset. Operator repair can
        // remove aged files verified to have no metadata; no cross-resource transaction is invented.
        var payload = SHA256.HashData([.. Encoding.UTF8.GetBytes(mediaType + "\0"), .. bytes]);
        var result = await transactions.ExecuteAsync(actor, false, [], async ct =>
        {
            Task<RecipeImageRead> Create(CancellationToken _)
            {
                database.ImageAssets.Add(asset);
                return Task.FromResult(new RecipeImageRead(asset.Id, asset.MediaType, asset.Size));
            }
            if (retryKey is null) return await Create(ct);
            var outcome = await retries.ExecuteAsync(actor.HouseholdId, actor.UserId, "/api/v1/images", retryKey.Value, payload,
                async cancellation => new CreationOutcome(201, JsonSerializer.Serialize(await Create(cancellation)), null, null), ct);
            return JsonSerializer.Deserialize<RecipeImageRead>(outcome.Body) ?? throw new InvalidOperationException("Image retry result unavailable.");
        }, cancellationToken);
        if (result.Id != asset.Id) DeleteFile(path); // A replay never inserted this newly prepared candidate.
        return result;
    }
    public async Task<RecipeImageFile> OpenAsync(RequestActor actor, Guid id, CancellationToken cancellationToken)
    {
        var asset = await transactions.ExecuteAsync(actor, false, [], async ct => await database.ImageAssets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new ResourceMissingException(), cancellationToken);
        try
        {
            return new RecipeImageFile(new FileStream(directory.FilePath(actor.HouseholdId, asset.StorageKey), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                bufferSize: 65536, options: FileOptions.Asynchronous | FileOptions.SequentialScan), asset.MediaType, $"\"image:{asset.Id:N}:{asset.StorageKey}\"");
        }
        catch (FileNotFoundException) { throw new ResourceMissingException(); }
        catch (DirectoryNotFoundException) { throw new ResourceMissingException(); }
    }
    public async Task DeleteAsync(RequestActor actor, Guid id, CancellationToken cancellationToken)
    {
        var asset = await transactions.ExecuteAsync(actor, false, [], async ct =>
        {
            var current = await database.ImageAssets.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ResourceMissingException();
            foreach (var reference in references)
                if (await reference.UsesImageAsync(actor.HouseholdId, id, ct)) throw new RecipeConflictException("image_referenced");
            database.ImageAssets.Remove(current); return current;
        }, cancellationToken);
        DeleteFile(directory.FilePath(actor.HouseholdId, asset.StorageKey));
    }
    private void DeleteFile(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { logger.LogWarning("Private image file cleanup pending."); }
        catch (UnauthorizedAccessException) { logger.LogWarning("Private image file cleanup pending."); }
    }
    private static byte[] Sanitize(byte[] bytes, string mediaType)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null) throw new RecipeImageException(422, "invalid_image");
        var format = mediaType switch { "image/jpeg" => SKEncodedImageFormat.Jpeg, "image/png" => SKEncodedImageFormat.Png, _ => SKEncodedImageFormat.Webp };
        if (codec.EncodedFormat != format) throw new RecipeImageException(415, "unsupported_image");
        var width = codec.Info.Width; var height = codec.Info.Height;
        if (width <= 0 || height <= 0 || (long)width * height > MaximumPixels) throw new RecipeImageException(422, "image_dimensions");
        using var colour = SKColorSpace.CreateSrgb();
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, colour);
        using var decoded = new SKBitmap(info);
        if (codec.GetPixels(info, decoded.GetPixels()) != SKCodecResult.Success) throw new RecipeImageException(422, "invalid_image");
        var rotated = codec.EncodedOrigin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        using var output = new SKBitmap(new SKImageInfo(rotated ? height : width, rotated ? width : height, SKColorType.Rgba8888, SKAlphaType.Premul, colour));
        using (var canvas = new SKCanvas(output))
        {
            switch (codec.EncodedOrigin)
            {
                case SKEncodedOrigin.TopRight: canvas.Translate(width, 0); canvas.Scale(-1, 1); break;
                case SKEncodedOrigin.BottomRight: canvas.Translate(width, height); canvas.RotateDegrees(180); break;
                case SKEncodedOrigin.BottomLeft: canvas.Translate(0, height); canvas.Scale(1, -1); break;
                case SKEncodedOrigin.LeftTop: canvas.RotateDegrees(90); canvas.Scale(1, -1); break;
                case SKEncodedOrigin.RightTop: canvas.Translate(height, 0); canvas.RotateDegrees(90); break;
                case SKEncodedOrigin.RightBottom: canvas.Translate(height, width); canvas.RotateDegrees(90); canvas.Scale(-1, 1); break;
                case SKEncodedOrigin.LeftBottom: canvas.Translate(0, width); canvas.RotateDegrees(270); break;
            }
            canvas.DrawBitmap(decoded, 0, 0, SKSamplingOptions.Default);
        }
        using var image = SKImage.FromBitmap(output);
        using var encoded = image.Encode(format, 90);
        return encoded?.ToArray() ?? throw new RecipeImageException(422, "invalid_image");
    }
}
