using System.Numerics;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using RecipeManager.Api.Security;
using RecipeManager.Core.Modules.Households;

namespace RecipeManager.Api.Contracts;

public sealed record RetryableCreationContract;
public sealed record AggregateContract(bool RequiresIfMatch = false, bool ReturnsETag = false, bool ConditionalGet = false);

public static class OpenApiConfiguration
{
    public static IServiceCollection AddContractOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes["sessionCookie"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Cookie,
                    Name = IdentityConfiguration.CookieName,
                    Description = "First-party HTTPS session cookie. Never store this credential in browser JavaScript or Share Extension storage."
                };
                document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
                document.Components.Schemas["ApiProblem"] = Problem();
                // The pinned Swift generator drops oneOf(null, $ref) object properties. Inline the
                // referenced object with a nullable type; required/properties apply only to objects,
                // so JSON Schema null semantics remain correct (unlike allOf with a non-null ref).
                var normalized = new HashSet<OpenApiSchema>();
                foreach (var schema in document.Components.Schemas.Values.OfType<OpenApiSchema>()) NormalizeNullableProperties(schema, normalized);
                return Task.CompletedTask;
            });
            options.AddSchemaTransformer((schema, context, _) =>
            {
                var underlying = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type);
                var type = underlying ?? context.JsonTypeInfo.Type;
                if (type == typeof(long) || type == typeof(decimal) || type == typeof(BigInteger))
                {
                    schema.Type = JsonSchemaType.String | (underlying is not null ? JsonSchemaType.Null : (JsonSchemaType)0);
                    schema.Format = null;
                    schema.Pattern = type == typeof(long) ? "^[0-9]+$" : type == typeof(BigInteger) ? "^-?[0-9]+$" : @"^-?[0-9]+(\.[0-9]+)?$";
                }
                if (type.IsEnum) { schema.Type = JsonSchemaType.String | (underlying is not null ? JsonSchemaType.Null : (JsonSchemaType)0); schema.Format = null; }
                if (type == typeof(JsonElement)) { schema.Type = JsonSchemaType.Object; schema.AdditionalPropertiesAllowed = true; }
                if (context.JsonTypeInfo.Type == typeof(AiSettingsWrite) && schema.Properties?.TryGetValue("apiKey", out var key) == true
                    && key is OpenApiSchema actual) actual.WriteOnly = true;
                return Task.CompletedTask;
            });
            options.AddOperationTransformer((operation, context, _) =>
            {
                // Optional JSON bodies are omitted when absent. Avoid oneOf(null, ref) here:
                // the pinned Swift generator otherwise drops the entire typed request body.
                if (operation.RequestBody is OpenApiRequestBody body && body.Content is not null)
                {
                    foreach (var media in body.Content.Values)
                    {
                        if (media.Schema is OpenApiSchema { OneOf.Count: 2 } nullableBody
                            && nullableBody.OneOf.Any(x => x.Type == JsonSchemaType.Null))
                        {
                            media.Schema = nullableBody.OneOf.Single(x => x.Type != JsonSchemaType.Null);
                            body.Required = false;
                        }
                    }
                }
                var metadata = context.Description.ActionDescriptor.EndpointMetadata;
                if (!metadata.OfType<IAllowAnonymous>().Any())
                {
                    operation.Security ??= [];
                    operation.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("sessionCookie", context.Document)] = [] });
                }
                operation.Parameters ??= [];
                if (context.Description.HttpMethod is not ("GET" or "HEAD" or "OPTIONS"))
                    operation.Parameters.Add(Header("X-XSRF-TOKEN", true, "Obtain a fresh token from GET /api/v1/auth/csrf for this session."));
                if (metadata.OfType<RetryableCreationContract>().Any())
                    operation.Parameters.Add(Header("Idempotency-Key", false, "Optional UUID creation retry key, scoped to user/household/route for 24 hours."));
                var aggregate = metadata.OfType<AggregateContract>().LastOrDefault();
                if (aggregate?.RequiresIfMatch == true) operation.Parameters.Add(Header("If-Match", true, "The current strong aggregate ETag. Missing: 428; stale: 412."));
                if (aggregate?.ConditionalGet == true) operation.Parameters.Add(Header("If-None-Match", false, "A current user-bound validator permits 304."));
                if (aggregate?.ReturnsETag == true && operation.Responses is { } responses)
                {
                    foreach (var response in responses.Where(x => x.Key is "200" or "201" or "304").Select(x => x.Value))
                    {
                        if (response is not OpenApiResponse actual) continue;
                        actual.Headers ??= new Dictionary<string, IOpenApiHeader>();
                        actual.Headers["ETag"] = new OpenApiHeader { Description = "Strong resource revision validator; preserve verbatim.", Schema = new OpenApiSchema { Type = JsonSchemaType.String } };
                    }
                }
                operation.Responses ??= new OpenApiResponses();
                foreach (var status in new[] { "400", "401", "403", "404", "409", "410", "412", "422", "428", "429", "500", "502", "504" })
                    operation.Responses.TryAdd(status, new OpenApiResponse
                    {
                        Description = "Request rejected; inspect code, traceId and field/conflict details.",
                        Content = new Dictionary<string, OpenApiMediaType>
                        {
                            ["application/problem+json"] = new() { Schema = new OpenApiSchemaReference("ApiProblem", context.Document) }
                        }
                    });
                return Task.CompletedTask;
            });
        });
        return services;
    }

    private static void NormalizeNullableProperties(OpenApiSchema schema, HashSet<OpenApiSchema> normalized)
    {
        if (!normalized.Add(schema)) return;
        if (schema.Properties is not null)
        {
            foreach (var (name, property) in schema.Properties.ToArray())
            {
                if (property is not OpenApiSchema { OneOf.Count: 2 } nullable || !nullable.OneOf.Any(x => x.Type == JsonSchemaType.Null)) continue;
                var target = nullable.OneOf.OfType<OpenApiSchemaReference>().SingleOrDefault()?.Target;
                if (target is not OpenApiSchema concrete || (concrete.Type != JsonSchemaType.Object && concrete.Type != JsonSchemaType.String)) continue;
                // Normalize targets before copying: otherwise nested nullable fields retain the
                // original union and disappear from the native client's copied object schema.
                NormalizeNullableProperties(concrete, normalized);
                var copy = (OpenApiSchema)concrete.CreateShallowCopy();
                copy.Type = concrete.Type | JsonSchemaType.Null;
                if (concrete.Enum is { Count: > 0 }) copy.Enum = [.. concrete.Enum, null!];
                schema.Properties[name] = copy;
            }
            foreach (var nested in schema.Properties.Values.OfType<OpenApiSchema>()) NormalizeNullableProperties(nested, normalized);
        }
        if (schema.Items is OpenApiSchema item) NormalizeNullableProperties(item, normalized);
    }

    private static OpenApiParameter Header(string name, bool required, string description)
        => new() { Name = name, In = ParameterLocation.Header, Required = required, Description = description, Schema = new OpenApiSchema { Type = JsonSchemaType.String } };
    private static OpenApiSchema Problem() => new()
    {
        Type = JsonSchemaType.Object,
        Required = new HashSet<string> { "type", "title", "status", "code", "traceId", "errors", "conflicts" },
        Properties = new Dictionary<string, IOpenApiSchema>
        {
            ["type"] = new OpenApiSchema { Type = JsonSchemaType.String },
            ["title"] = new OpenApiSchema { Type = JsonSchemaType.String },
            ["status"] = new OpenApiSchema { Type = JsonSchemaType.Integer },
            ["detail"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
            ["instance"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
            ["code"] = new OpenApiSchema { Type = JsonSchemaType.String },
            ["commitResult"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Required = new HashSet<string> { "recipeId", "committedRevision", "committedAt" },
                Properties = new Dictionary<string, IOpenApiSchema>
                {
                    ["recipeId"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" },
                    ["committedRevision"] = new OpenApiSchema { Type = JsonSchemaType.String, Pattern = "^[0-9]+$" },
                    ["committedAt"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "date-time" }
                }
            },
            ["traceId"] = new OpenApiSchema { Type = JsonSchemaType.String },
            ["errors"] = new OpenApiSchema { Type = JsonSchemaType.Array, Items = new OpenApiSchema { Type = JsonSchemaType.Object } },
            ["conflicts"] = new OpenApiSchema { Type = JsonSchemaType.Array, Items = new OpenApiSchema { Type = JsonSchemaType.Object } }
        }
    };
}
