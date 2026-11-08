using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Modules.Ingredients.Contracts;
using RecipeManager.Core.Modules.Recipes;
using RecipeManager.Core.Modules.Recipes.Contracts;
using RecipeManager.Core.Shared;
using RecipeManager.Infrastructure.Persistence;

namespace RecipeManager.Infrastructure.Recipes;

public sealed class RecipeStore(RecipeManagerDbContext database, TransactionBoundary transactions, RevisionCoordinator revisions,
    CreationRetryStore retries, IEnumerable<IRecipeReferences> references, IEnumerable<IRecipeOrganisers> organiserOwners)
    : IRecipeStore, IIngredientReferences, IImageReferences, IImportedRecipeWriter
{
    private IRecipeOrganisers? Organisers => organiserOwners.SingleOrDefault();
    public Task<RecipeListResult> ListAsync(RequestActor actor, RecipeQuery query, CancellationToken cancellationToken)
        => transactions.ExecuteAsync(actor, false, [], async ct =>
        {
            var recipes = database.Recipes.AsNoTracking().Where(x => query.Cursor == null || x.Id.CompareTo(query.Cursor.Value) > 0);
            if (query.Archived != "include") recipes = recipes.Where(x => x.Archived == (query.Archived == "only"));
            if (query.FavouriteOnly) recipes = recipes.Where(x => database.Favourites.Any(f => f.UserId == actor.UserId && f.RecipeId == x.Id));
            if (query.CollectionIds.Count > 0 || query.TagIds.Count > 0)
            {
                var filtered = Organisers is { } owner ? await owner.FilterAsync(actor.HouseholdId, query.CollectionIds, query.TagIds, ct) : [];
                recipes = recipes.Where(x => filtered.Contains(x.Id));
            }
            if (!string.IsNullOrWhiteSpace(query.Query))
            {
                var pattern = Like(query.Query.Trim());
                var catalogPattern = Like(Core.Modules.Ingredients.CatalogueNames.Normalize(query.Query));
                var tagged = Organisers is { } owner ? await owner.SearchTagsAsync(actor.HouseholdId, query.Query, ct) : [];
                recipes = recipes.Where(x => EF.Functions.ILike(x.Title, pattern, "\\") || tagged.Contains(x.Id)
                    || database.RecipeIngredients.Any(row => row.RecipeId == x.Id && database.IngredientNames.Any(name => name.IngredientId == row.IngredientId && EF.Functions.ILike(name.NormalizedName, catalogPattern, "\\"))));
            }
            var rows = await recipes.OrderBy(x => x.Id).Take(query.Limit + 1).ToListAsync(ct);
            var page = (await ReadResults(actor, rows.Take(query.Limit).ToArray(), ct)).Select(x => x.Recipe).ToArray();
            return new RecipeListResult(new RecipePage(page, rows.Count > query.Limit ? page[^1].Id : null),
                (await database.Households.SingleAsync(x => x.Id == actor.HouseholdId, ct)).RecipesRevision,
                (await database.Users.SingleAsync(x => x.Id == actor.UserId, ct)).PreferencesRevision);
        }, cancellationToken);
    public Task<RecipeResult> ReadAsync(RequestActor actor, Guid id, CancellationToken cancellationToken)
        => transactions.ExecuteAsync(actor, false, [], async ct => await ReadResult(actor, await Required(id, ct), ct), cancellationToken);
    public Task<RecipeResult> CreateAsync(RequestActor actor, RecipeWrite request, Guid? retryKey, CancellationToken cancellationToken)
        => transactions.ExecuteAsync(actor, false, [], ct => Retry(actor, "/api/v1/recipes", retryKey, request,
            cancellation => CreateWithinTransaction(actor, request, cancellation), ct), cancellationToken);
    public Task<RecipeResult> UpdateAsync(RequestActor actor, Guid id, RecipeVersion expected, RecipeWrite request, CancellationToken cancellationToken)
        => transactions.ExecuteAsync(actor, false, [RecipeLock(actor, id)], async ct =>
        {
            var recipe = await Required(id, ct); await Match(actor, recipe, expected, ct);
            await ValidateReferences(actor.HouseholdId, id, request, ct);
            recipe.Replace(request);
            // Reinsert owned rows with their stable IDs. This permits atomic position swaps under
            // unique ordering indexes without temporary persisted positions or partial updates.
            await database.RecipeIngredients.Where(x => x.RecipeId == id).ExecuteDeleteAsync(ct);
            await database.Instructions.Where(x => x.RecipeId == id).ExecuteDeleteAsync(ct);
            await database.IngredientGroups.Where(x => x.RecipeId == id).ExecuteDeleteAsync(ct);
            AddContent(actor.HouseholdId, id, request);
            if (Organisers is { } owner) await owner.ReplaceAsync(actor.HouseholdId, id, request.CollectionIds, request.TagIds, ct);
            await Changed(actor, ct); await database.SaveChangesAsync(ct);
            return await ReadResult(actor, recipe, ct);
        }, cancellationToken);
    public Task<RecipeResult> DuplicateAsync(RequestActor actor, Guid id, RecipeVersion expected, Guid? retryKey, CancellationToken cancellationToken)
        => transactions.ExecuteAsync(actor, false, [RecipeLock(actor, id)], ct => Retry(actor, $"/api/v1/recipes/{id}/duplicate", retryKey,
            new { expected }, async cancellation =>
            {
                var source = await Required(id, cancellation); await Match(actor, source, expected, cancellation);
                var read = (await ReadResult(actor, source, cancellation)).Recipe;
                var groups = read.Groups.ToDictionary(x => x.Id, _ => Guid.NewGuid());
                var content = new RecipeWrite(read.Title, read.DefaultServings,
                    read.Groups.Select(x => new RecipeGroupWrite(x.Title, x.Position, groups[x.Id])).ToArray(),
                    read.Ingredients.Select(x => new RecipeIngredientWrite(x.Position, x.IngredientId, x.QuantityText, x.Kind, x.Unit,
                        GroupId: x.GroupId is { } group ? groups[group] : null, Preparation: x.Preparation, Uncertain: x.Uncertain)).ToArray(),
                    read.Instructions.Select(x => new RecipeInstructionWrite(x.Position, x.Text)).ToArray(), read.CollectionIds, read.TagIds,
                    read.Description, read.ImageAssetId, read.PrepMinutes, read.CookMinutes, read.Notes, read.SourceUrl, read.MeasurementStandard,
                    AcknowledgeUncertainIngredients: true);
                return await CreateWithinTransaction(actor, content, cancellation);
            }, ct), cancellationToken);
    public Task<RecipeResult> ArchiveAsync(RequestActor actor, Guid id, RecipeVersion expected, bool archived, CancellationToken cancellationToken)
        => transactions.ExecuteAsync(actor, false, [RecipeLock(actor, id)], async ct =>
        {
            var recipe = await Required(id, ct); await Match(actor, recipe, expected, ct);
            recipe.SetArchived(archived); await Changed(actor, ct); await database.SaveChangesAsync(ct);
            return await ReadResult(actor, recipe, ct);
        }, cancellationToken);
    public Task<RecipeResult> FavouriteAsync(RequestActor actor, Guid id, RecipeVersion expected, bool favourite, CancellationToken cancellationToken)
        => transactions.ExecuteAsync(actor, false, [RecipeLock(actor, id)], async ct =>
        {
            var recipe = await Required(id, ct); await Match(actor, recipe, expected, ct);
            var existing = await database.Favourites.SingleOrDefaultAsync(x => x.UserId == actor.UserId && x.RecipeId == id, ct);
            if (favourite && existing is null) database.Favourites.Add(new Favourite(actor.HouseholdId, actor.UserId, id));
            else if (!favourite && existing is not null) database.Favourites.Remove(existing);
            if (favourite != (existing is not null)) await revisions.PersonalAsync(actor.HouseholdId, actor.UserId, ct);
            await database.SaveChangesAsync(ct);
            return await ReadResult(actor, recipe, ct);
        }, cancellationToken);
    public Task DeleteAsync(RequestActor actor, Guid id, RecipeVersion expected, CancellationToken cancellationToken)
        => transactions.ExecuteAsync(actor, false, [RecipeLock(actor, id)], async ct =>
        {
            var recipe = await Required(id, ct); await Match(actor, recipe, expected, ct);
            foreach (var reference in references)
                if (await reference.UsesRecipeAsync(actor.HouseholdId, id, ct)) throw new RecipeConflictException("recipe_referenced");
            database.Recipes.Remove(recipe); await Changed(actor, ct); return true;
        }, cancellationToken);
    public Task<RecipeServingPreview> PreviewAsync(RequestActor actor, Guid id, decimal targetServings, CancellationToken cancellationToken)
        => transactions.ExecuteAsync(actor, false, [], async ct =>
        {
            var recipe = await Required(id, ct); var read = (await ReadResult(actor, recipe, ct)).Recipe;
            IngredientCalculator.ValidateBatchSize(read.Ingredients.Count);
            var lines = read.Ingredients.Select(row =>
            {
                var quantity = row.Kind == QuantityKind.Unknown ? Quantity.Unknown(row.QuantityText) : Quantity.Parse(row.QuantityText);
                var scaled = IngredientCalculator.Scale(quantity, recipe.DefaultServings, targetServings);
                var display = scaled is not null ? IngredientCalculator.OriginalUnitDisplay(scaled, row.Unit) : null;
                return new RecipeIngredientPreview(row.Id, row.IngredientId, row.IngredientName, row.QuantityText, row.Unit,
                    scaled is not null ? ExactQuantity.From(scaled) : null, display?.Text ?? row.QuantityText, display?.IsApproximate ?? false, row.Uncertain);
            }).ToArray();
            return new RecipeServingPreview(id, recipe.Revision, recipe.DefaultServings, targetServings, lines);
        }, cancellationToken);

    public RecipeWrite Validate(RecipeWrite recipe) => RecipeRules.Validate(recipe);
    public async Task<ImportedRecipeIdentity> CreateWithinTransactionAsync(RequestActor actor, RecipeWrite request, CancellationToken ct)
    {
        if (database.Database.CurrentTransaction is null || database.CurrentHouseholdId != actor.HouseholdId)
            throw new InvalidOperationException("Imported recipes require the household transaction boundary.");
        var validated = RecipeRules.Validate(request);
        var result = await CreateWithinTransaction(actor, validated, ct);
        return new(result.Recipe.Id, result.Recipe.Revision);
    }
    private async Task<RecipeResult> CreateWithinTransaction(RequestActor actor, RecipeWrite request, CancellationToken ct)
    {
        var id = Guid.NewGuid(); await ValidateReferences(actor.HouseholdId, id, request, ct);
        var recipe = new Recipe(id, actor.HouseholdId, request); database.Recipes.Add(recipe); AddContent(actor.HouseholdId, id, request);
        if (Organisers is { } owner) await owner.ReplaceAsync(actor.HouseholdId, id, request.CollectionIds, request.TagIds, ct);
        await Changed(actor, ct); await database.SaveChangesAsync(ct);
        return await ReadResult(actor, recipe, ct);
    }
    private async Task ValidateReferences(Guid householdId, Guid recipeId, RecipeWrite request, CancellationToken ct)
    {
        var ingredients = request.Ingredients.Select(x => x.IngredientId).Distinct().ToArray();
        if (await database.Ingredients.CountAsync(x => ingredients.Contains(x.Id), ct) != ingredients.Length) throw new ResourceMissingException();
        if (request.ImageAssetId is { } image && !await database.ImageAssets.AnyAsync(x => x.Id == image, ct)) throw new ResourceMissingException();
        var groupIds = request.Groups.Where(x => x.Id != null).Select(x => x.Id!.Value).ToArray();
        var rowIds = request.Ingredients.Where(x => x.Id != null).Select(x => x.Id!.Value).ToArray();
        var instructionIds = request.Instructions.Where(x => x.Id != null).Select(x => x.Id!.Value).ToArray();
        if (await database.IngredientGroups.AnyAsync(x => groupIds.Contains(x.Id) && x.RecipeId != recipeId, ct)
            || await database.RecipeIngredients.AnyAsync(x => rowIds.Contains(x.Id) && x.RecipeId != recipeId, ct)
            || await database.Instructions.AnyAsync(x => instructionIds.Contains(x.Id) && x.RecipeId != recipeId, ct))
            throw new DomainValidationException("rows.id", "foreign_row", "Recipe row IDs must belong to this recipe or be new IDs.");
        if (Organisers is { } owner) await owner.ValidateAsync(householdId, request.CollectionIds, request.TagIds, ct);
        else if (request.CollectionIds.Count > 0 || request.TagIds.Count > 0) throw new ResourceMissingException();
    }
    private void AddContent(Guid householdId, Guid recipeId, RecipeWrite request)
    {
        database.IngredientGroups.AddRange(request.Groups.Select(x => new IngredientGroup(x.Id ?? Guid.NewGuid(), householdId, recipeId, x.Title, x.Position)));
        database.RecipeIngredients.AddRange(request.Ingredients.Select(x => new RecipeIngredient(x.Id ?? Guid.NewGuid(), householdId, recipeId, x)));
        database.Instructions.AddRange(request.Instructions.Select(x => new Instruction(x.Id ?? Guid.NewGuid(), householdId, recipeId, x.Position, x.Text)));
    }
    private async Task<RecipeResult> ReadResult(RequestActor actor, Recipe recipe, CancellationToken ct)
        => (await ReadResults(actor, [recipe], ct)).Single();
    private async Task<IReadOnlyList<RecipeResult>> ReadResults(RequestActor actor, IReadOnlyList<Recipe> recipes, CancellationToken ct)
    {
        if (recipes.Count == 0) return [];
        var recipeIds = recipes.Select(x => x.Id).ToArray();
        var projected = await RecipeProjection.ReadAsync(database, actor.HouseholdId, recipes, ct);
        var favourites = (await database.Favourites.Where(x => recipeIds.Contains(x.RecipeId) && x.UserId == actor.UserId).Select(x => x.RecipeId).ToArrayAsync(ct)).ToHashSet();
        var links = Organisers is { } owner ? await owner.ReadManyAsync(actor.HouseholdId, recipeIds, ct) : new Dictionary<Guid, RecipeOrganiserIds>();
        var personal = (await database.Users.SingleAsync(x => x.Id == actor.UserId, ct)).PreferencesRevision;
        return projected.Select(recipe =>
        {
            var organisers = links.GetValueOrDefault(recipe.Id) ?? new RecipeOrganiserIds([], []);
            return new RecipeResult(recipe with { Favourite = favourites.Contains(recipe.Id), CollectionIds = organisers.CollectionIds, TagIds = organisers.TagIds }, personal);
        }).ToArray();
    }
    public Task<bool> UsesIngredientAsync(Guid householdId, Guid ingredientId, CancellationToken cancellationToken)
        => database.RecipeIngredients.AnyAsync(x => x.HouseholdId == householdId && x.IngredientId == ingredientId, cancellationToken);
    public async Task IngredientChangedAsync(Guid householdId, Guid ingredientId, CancellationToken cancellationToken)
    {
        // Live recipe representations change, while cooking snapshots remain owned and untouched.
        var ids = database.RecipeIngredients.Where(x => x.IngredientId == ingredientId).Select(x => x.RecipeId);
        foreach (var recipe in await database.Recipes.Where(x => x.HouseholdId == householdId && ids.Contains(x.Id)).OrderBy(x => x.Id).ToListAsync(cancellationToken)) recipe.Advance();
    }
    public Task<bool> UsesImageAsync(Guid householdId, Guid imageAssetId, CancellationToken cancellationToken)
        => database.Recipes.AnyAsync(x => x.HouseholdId == householdId && x.ImageAssetId == imageAssetId, cancellationToken);
    private async Task<Recipe> Required(Guid id, CancellationToken ct) => await database.Recipes.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ResourceMissingException();
    private async Task Match(RequestActor actor, Recipe recipe, RecipeVersion expected, CancellationToken ct)
    {
        if (expected.UserId != actor.UserId || recipe.Revision != expected.Revision || (await database.Users.SingleAsync(x => x.Id == actor.UserId, ct)).PreferencesRevision != expected.PersonalRevision) throw new RevisionConflictException();
    }
    private Task Changed(RequestActor actor, CancellationToken ct) => revisions.SharedAsync(actor.HouseholdId, ChangedDomain.Recipes, true, ct);
    private AggregateLock RecipeLock(RequestActor actor, Guid id) => new("recipe", id, async ct =>
    {
        _ = await database.Recipes.FromSqlInterpolated($"SELECT * FROM recipes.\"Recipes\" WHERE \"HouseholdId\" = {actor.HouseholdId} AND \"Id\" = {id} FOR UPDATE").AsNoTracking().ToListAsync(ct);
    });
    private async Task<RecipeResult> Retry(RequestActor actor, string route, Guid? key, object payload, Func<CancellationToken, Task<RecipeResult>> create, CancellationToken ct)
    {
        if (key is null) return await create(ct);
        var result = await retries.ExecuteAsync(actor.HouseholdId, actor.UserId, route, key.Value, JsonSerializer.SerializeToUtf8Bytes(payload), async cancellation =>
            new CreationOutcome(201, JsonSerializer.Serialize(await create(cancellation)), null, null), ct);
        return JsonSerializer.Deserialize<RecipeResult>(result.Body) ?? throw new InvalidOperationException("Retry result unavailable.");
    }
    private static string Like(string value) => "%" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
}
