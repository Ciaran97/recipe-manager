using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;
using RecipeManager.Api.Contracts;
using RecipeManager.Api.Security;
using RecipeManager.Infrastructure.Files;
using RecipeManager.Infrastructure.Persistence;

namespace RecipeManager.Api.Endpoints;

public static class ImageEndpoints
{
    public static void AddRecipeImages(this WebApplicationBuilder builder, bool temporary)
    {
        var configured = builder.Configuration["Images:Directory"];
        if (!temporary && string.IsNullOrWhiteSpace(configured)) throw new InvalidOperationException("Configure a persistent Images:Directory outside the web root.");
        var root = Path.GetFullPath(configured ?? Path.Combine(Path.GetTempPath(), "recipe-manager-images-" + Guid.NewGuid().ToString("N")));
        var webRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(builder.Environment.WebRootPath ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot")));
        if (root == webRoot || root.StartsWith(webRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Private images must be stored outside the web root.");
        builder.Services.AddSingleton(new RecipeImageDirectory(root, ephemeral: temporary && configured is null));
        builder.Services.AddScoped<RecipeImages>();
        builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = 11 * 1024 * 1024);
        builder.Services.AddRateLimiter(options => options.AddPolicy("image-uploads", context => RateLimitPartition.GetConcurrencyLimiter(
            context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous", _ => new ConcurrencyLimiterOptions { PermitLimit = 1, QueueLimit = 0 })));
    }
    public static void UseImageRequestLimits(this WebApplication app)
        => app.Use(async (context, next) =>
        {
            if (HttpMethods.IsPost(context.Request.Method) && context.Request.Path == "/api/v1/images")
            {
                const int limit = 11 * 1024 * 1024;
                if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } feature) feature.MaxRequestBodySize = limit;
                if (context.Request.ContentLength > limit) throw new RecipeImageException(413, "image_too_large");
            }
            await next(context);
        });
    public static void MapImageEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/images").WithTags("Images").RequireAuthorization(IdentityConfiguration.MemberPolicy);
        api.MapPost("/", async (IFormFile file, HttpContext context, CurrentMembership membership, RecipeImages images, CancellationToken ct) =>
        {
            var actor = await membership.ResolveActorAsync(ct) ?? throw new TransactionAccessException();
            if (file.Length > RecipeImages.MaximumUploadBytes) throw new RecipeImageException(413, "image_too_large");
            Guid? key = null;
            if (context.Request.Headers.TryGetValue("Idempotency-Key", out var supplied))
            {
                if (!Guid.TryParseExact(supplied, "D", out var value) || value == Guid.Empty) throw new ApiContractException(422, "invalid_idempotency_key");
                key = value;
            }
            await using var stream = file.OpenReadStream();
            var result = await images.UploadAsync(actor, stream, file.ContentType, key, ct);
            return TypedResults.Created("/api/v1/images/" + result.Id, result);
        }).RequireRateLimiting("image-uploads").WithName("UploadRecipeImage").WithMetadata(new RetryableCreationContract());
        api.MapGet("/{id:guid}", async (Guid id, HttpContext context, CurrentMembership membership, RecipeImages images, CancellationToken ct) =>
        {
            var actor = await membership.ResolveActorAsync(ct) ?? throw new TransactionAccessException();
            var file = await images.OpenAsync(actor, id, ct);
            context.Response.Headers.CacheControl = "private, no-cache";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            return TypedResults.File(file.Content, file.MediaType, entityTag: new EntityTagHeaderValue(file.ETag));
        }).WithName("GetRecipeImage").WithMetadata(new AggregateContract(ReturnsETag: true, ConditionalGet: true));
        api.MapDelete("/{id:guid}", async (Guid id, HttpContext context, CurrentMembership membership, RecipeImages images, CancellationToken ct) =>
        {
            var actor = await membership.ResolveActorAsync(ct) ?? throw new TransactionAccessException();
            var current = await images.OpenAsync(actor, id, ct);
            await current.Content.DisposeAsync();
            TransportConventions.RequireMatch(context.Request.Headers.IfMatch, current.ETag);
            await images.DeleteAsync(actor, id, ct); return TypedResults.NoContent();
        }).WithName("DeleteRecipeImage").WithMetadata(new AggregateContract(RequiresIfMatch: true));
    }
}
