using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Infrastructure.Files;
using RecipeManager.Infrastructure.Persistence;
using RecipeManager.Infrastructure.Security;
using RecipeManager.IntegrationTests.Fixtures;
using RecipeManager.IntegrationTests.Households;
using RecipeManager.IntegrationTests.Recipes;
using SkiaSharp;

namespace RecipeManager.IntegrationTests.Images;

public sealed class ImageContractTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private async Task<ApiFixture> Fresh()
    {
        await using var db = new RecipeManagerDbContext(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(postgres.ConnectionString).Options);
        await db.Database.EnsureDeletedAsync(); await db.Database.MigrateAsync();
        return new ApiFixture(postgres.ConnectionString, new Dictionary<string, string?> { ["Setup:Token"] = "test-only-operator-secret-which-is-never-a-production-default" });
    }
    private static HttpClient Client(ApiFixture host) => host.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    private static async Task Setup(HttpClient client) => Assert.Equal(HttpStatusCode.Created,
        (await HouseholdContractTests.Write(client, HttpMethod.Post, "/api/v1/setup", HouseholdContractTests.SetupBody())).StatusCode);
    private static async Task<HttpResponseMessage> Upload(HttpClient client, byte[] bytes, string mediaType = "image/png", Guid? key = null)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        using var body = new MultipartFormDataContent(); var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new(mediaType); body.Add(content, "file", "../../ignored-filename.png");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/images") { Content = body };
        request.Headers.Add("X-XSRF-TOKEN", csrf.GetProperty("token").GetString());
        if (key is { } retry) request.Headers.Add("Idempotency-Key", retry.ToString());
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Uploaded_images_are_reencoded_private_idempotent_and_protected_while_referenced()
    {
        await using var host = await Fresh(); using var client = Client(host); await Setup(client);
        var source = Png(2, 3, "private-location-fixture"); var key = Guid.NewGuid();
        var uploaded = await Upload(client, source, key: key); Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        var replay = await Upload(client, source, key: key); Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(await uploaded.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        var imageId = (await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var path = "/api/v1/images/" + imageId;
        using var image = await client.GetAsync(path); Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType!.MediaType); Assert.True(image.Headers.CacheControl!.Private);
        Assert.DoesNotContain("private-location-fixture", Encoding.Latin1.GetString(await image.Content.ReadAsByteArrayAsync()));
        using var cached = new HttpRequestMessage(HttpMethod.Get, path); cached.Headers.IfNoneMatch.Add(image.Headers.ETag!);
        Assert.Equal(HttpStatusCode.NotModified, (await client.SendAsync(cached)).StatusCode);
        using var anonymous = Client(host); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
        var ingredient = await RecipeContractTests.Ingredient(client);
        var request = JsonSerializer.SerializeToNode(RecipeContractTests.RecipeWrite(ingredient))!; request["imageAssetId"] = imageId;
        var recipe = await HouseholdContractTests.Write(client, HttpMethod.Post, "/api/v1/recipes", request);
        Assert.Equal(HttpStatusCode.Created, recipe.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await HouseholdContractTests.Write(client, HttpMethod.Delete, path, etag: image.Headers.ETag!.ToString())).StatusCode);
        var recipePath = "/api/v1/recipes/" + (await recipe.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await HouseholdContractTests.Write(client, HttpMethod.Delete, recipePath, etag: recipe.Headers.ETag!.ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await HouseholdContractTests.Write(client, HttpMethod.Delete, path, etag: image.Headers.ETag!.ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Upload_limits_and_decoded_media_types_are_checked_before_persistence()
    {
        await using var host = await Fresh(); using var client = Client(host); await Setup(client);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await Upload(client, new byte[RecipeImages.MaximumUploadBytes + 1])).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Upload(client, Png(7000, 3000))).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await Upload(client, Png(1, 1), "image/jpeg")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Upload(client, "not an image"u8.ToArray())).StatusCode);
        await using var db = new RecipeManagerDbContext(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(postgres.ConnectionString).Options);
        Assert.Empty(await db.ImageAssets.IgnoreQueryFilters().ToListAsync());
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    public async Task Supported_formats_round_trip_through_the_native_codec(string mediaType)
    {
        await using var host = await Fresh(); using var client = Client(host); await Setup(client);
        var format = mediaType switch { "image/jpeg" => SKEncodedImageFormat.Jpeg, "image/png" => SKEncodedImageFormat.Png, _ => SKEncodedImageFormat.Webp };
        using var bitmap = new SKBitmap(2, 2); bitmap.Erase(SKColors.Green);
        using var image = SKImage.FromBitmap(bitmap); using var encoded = image.Encode(format, 90);
        var upload = await Upload(client, encoded.ToArray(), mediaType); Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var id = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var response = await client.GetAsync("/api/v1/images/" + id); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mediaType, response.Content.Headers.ContentType!.MediaType);
        using var data = SKData.CreateCopy(await response.Content.ReadAsByteArrayAsync()); using var codec = SKCodec.Create(data);
        Assert.Equal(format, codec.EncodedFormat); Assert.Equal(2, codec.Info.Width); Assert.Equal(2, codec.Info.Height);
    }

    [Fact]
    public async Task Phone_orientation_is_applied_before_private_metadata_is_removed()
    {
        await using var host = await Fresh(); using var client = Client(host); await Setup(client);
        using var bitmap = new SKBitmap(20, 30);
        for (var y = 0; y < 30; y++) for (var x = 0; x < 20; x++) bitmap.SetPixel(x, y, y < 15 ? SKColors.Red : SKColors.Blue);
        using var sourceImage = SKImage.FromBitmap(bitmap); using var encoded = sourceImage.Encode(SKEncodedImageFormat.Jpeg, 100);
        var jpeg = encoded.ToArray();
        byte[] exif = [69, 120, 105, 102, 0, 0, 73, 73, 42, 0, 8, 0, 0, 0, 1, 0, 18, 1, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0];
        byte[] rotated = [255, 216, 255, 225, 0, 34, .. exif, .. jpeg.AsSpan(2)];
        using (var data = SKData.CreateCopy(rotated)) using (var codec = SKCodec.Create(data)) Assert.Equal(SKEncodedOrigin.RightTop, codec.EncodedOrigin);
        var uploaded = await Upload(client, rotated, "image/jpeg"); Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        var id = (await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var bytes = await client.GetByteArrayAsync("/api/v1/images/" + id);
        using var cleanData = SKData.CreateCopy(bytes); using var cleanCodec = SKCodec.Create(cleanData);
        Assert.Equal(30, cleanCodec.Info.Width); Assert.Equal(20, cleanCodec.Info.Height); Assert.Equal(SKEncodedOrigin.TopLeft, cleanCodec.EncodedOrigin);
        using var cleanBitmap = SKBitmap.Decode(bytes); var corner = cleanBitmap.GetPixel(2, 2);
        Assert.True(corner.Blue > corner.Red + 50, "The original blue bottom must rotate to the left.");
    }

    [Fact]
    public async Task Images_are_shared_with_members_and_hidden_from_another_authenticated_household()
    {
        await using var host = await Fresh(); using var admin = Client(host); await Setup(admin);
        var uploaded = await Upload(admin, Png(1, 1)); Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        var id = (await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); var path = "/api/v1/images/" + id;
        var invitation = await HouseholdContractTests.Write(admin, HttpMethod.Post, "/api/v1/household/invitations", new { });
        var token = (await invitation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        using var member = Client(host);
        Assert.Equal(HttpStatusCode.Created, (await HouseholdContractTests.Write(member, HttpMethod.Post, "/api/v1/invitations/accept", new { token, email = "member@example.test", password = "member-test-passphrase", name = "Member" })).StatusCode);
        using var shared = await member.GetAsync(path); Assert.Equal(HttpStatusCode.OK, shared.StatusCode);
        await using (var db = new RecipeManagerDbContext(new DbContextOptionsBuilder<RecipeManagerDbContext>().UseNpgsql(postgres.ConnectionString).Options))
        {
            var user = new ApplicationUser(Guid.NewGuid(), "outside@example.test");
            user.PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<ApplicationUser>().HashPassword(user, "outside-test-passphrase");
            var household = new Household(Guid.NewGuid(), "Other", "UTC");
            db.AddRange(user, household, new Membership(Guid.NewGuid(), household.Id, user.Id, HouseholdRole.Administrator)); await db.SaveChangesAsync();
        }
        using var outsider = Client(host);
        Assert.Equal(HttpStatusCode.OK, (await HouseholdContractTests.Write(outsider, HttpMethod.Post, "/api/v1/auth/login", new { email = "outside@example.test", password = "outside-test-passphrase" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await HouseholdContractTests.Write(outsider, HttpMethod.Delete, path, etag: shared.Headers.ETag!.ToString())).StatusCode);
        var ownIngredient = await RecipeContractTests.Ingredient(outsider);
        var recipe = JsonSerializer.SerializeToNode(RecipeContractTests.RecipeWrite(ownIngredient))!; recipe["imageAssetId"] = id;
        Assert.Equal(HttpStatusCode.NotFound, (await HouseholdContractTests.Write(outsider, HttpMethod.Post, "/api/v1/recipes", recipe)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await HouseholdContractTests.Write(member, HttpMethod.Delete, path, etag: shared.Headers.ETag!.ToString())).StatusCode);
    }

    // Standard PNG fixtures use bounded row compression; the oversized-dimension test need not
    // allocate an 84 MB pixel bitmap, and does not depend on the production decoder/encoder.
    private static byte[] Png(int width, int height, string? comment = null)
    {
        using var output = new MemoryStream(); output.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header, width); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; header[9] = 6; Chunk(output, "IHDR", header);
        if (comment is not null) Chunk(output, "tEXt", Encoding.UTF8.GetBytes("Comment\0" + comment));
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = new byte[checked(width * 4 + 1)];
            for (var i = 0; i < height; i++) zlib.Write(row);
        }
        Chunk(output, "IDAT", compressed.ToArray()); Chunk(output, "IEND", []); return output.ToArray();
    }
    private static void Chunk(Stream output, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(number, data.Length); output.Write(number);
        var tag = Encoding.ASCII.GetBytes(type); output.Write(tag); output.Write(data);
        uint crc = uint.MaxValue;
        foreach (var value in tag.Concat(data))
        {
            crc ^= value; for (var i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0u);
        }
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); output.Write(number);
    }
}
