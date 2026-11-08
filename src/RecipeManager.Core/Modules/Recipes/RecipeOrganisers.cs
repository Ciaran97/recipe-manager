using RecipeManager.Core.Modules.Households.Contracts;
using RecipeManager.Core.Modules.Ingredients;
using RecipeManager.Core.Shared;

namespace RecipeManager.Core.Modules.Recipes;

public interface IRecipeOrganiser
{
    Guid Id { get; }
    Guid HouseholdId { get; }
    string Name { get; }
    string NormalizedName { get; }
    long Revision { get; }
    void Rename(string name);
}
public sealed class Collection : IRecipeOrganiser
{
    private Collection() { }
    public Collection(Guid id, Guid householdId, string name)
    { Id = DomainRules.NonEmptyId(id, "id"); HouseholdId = DomainRules.NonEmptyId(householdId, "householdId"); Rename(name); }
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public string Name { get; private set; } = null!;
    public string NormalizedName { get; private set; } = null!;
    public long Revision { get; private set; }
    public void Rename(string name) { var value = DomainRules.Name(name); var normalized = CatalogueNames.Normalize(value); Name = value; NormalizedName = normalized; Revision = checked(Revision + 1); }
}
public sealed class Tag : IRecipeOrganiser
{
    private Tag() { }
    public Tag(Guid id, Guid householdId, string name)
    { Id = DomainRules.NonEmptyId(id, "id"); HouseholdId = DomainRules.NonEmptyId(householdId, "householdId"); Rename(name); }
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public string Name { get; private set; } = null!;
    public string NormalizedName { get; private set; } = null!;
    public long Revision { get; private set; }
    public void Rename(string name) { var value = DomainRules.Name(name); var normalized = CatalogueNames.Normalize(value); Name = value; NormalizedName = normalized; Revision = checked(Revision + 1); }
}
public sealed record RecipeCollection(Guid HouseholdId, Guid RecipeId, Guid CollectionId);
public sealed record RecipeTag(Guid HouseholdId, Guid RecipeId, Guid TagId);
public enum OrganiserKind { Collection, Tag }
public sealed record OrganiserRead(Guid Id, string Name, long Revision);
public sealed record OrganiserPage(IReadOnlyList<OrganiserRead> Items, Guid? NextId);
public interface IRecipeOrganiserStore
{
    Task<OrganiserPage> ListAsync(RequestActor actor, OrganiserKind kind, string query, Guid? cursor, int limit, CancellationToken ct);
    Task<OrganiserRead> ReadAsync(RequestActor actor, OrganiserKind kind, Guid id, CancellationToken ct);
    Task<OrganiserRead> CreateAsync(RequestActor actor, OrganiserKind kind, string name, Guid? retryKey, CancellationToken ct);
    Task<OrganiserRead> UpdateAsync(RequestActor actor, OrganiserKind kind, Guid id, long revision, string name, CancellationToken ct);
    Task DeleteAsync(RequestActor actor, OrganiserKind kind, Guid id, long revision, CancellationToken ct);
}
public sealed class RecipeOrganiserService(IRecipeOrganiserStore store)
{
    public Task<OrganiserPage> ListAsync(RequestActor actor, OrganiserKind kind, string query, Guid? cursor, int limit, CancellationToken ct)
    {
        if (query.Length > 200 || limit is < 1 or > 100) throw new DomainValidationException("query", "invalid", "Use bounded search and pagination.");
        return store.ListAsync(actor, kind, query, cursor, limit, ct);
    }
    public Task<OrganiserRead> ReadAsync(RequestActor actor, OrganiserKind kind, Guid id, CancellationToken ct) => store.ReadAsync(actor, kind, DomainRules.NonEmptyId(id, "id"), ct);
    public Task<OrganiserRead> CreateAsync(RequestActor actor, OrganiserKind kind, string name, Guid? key, CancellationToken ct) => store.CreateAsync(actor, kind, DomainRules.Name(name), key, ct);
    public Task<OrganiserRead> UpdateAsync(RequestActor actor, OrganiserKind kind, Guid id, long revision, string name, CancellationToken ct) => store.UpdateAsync(actor, kind, DomainRules.NonEmptyId(id, "id"), DomainRules.Revision(revision), DomainRules.Name(name), ct);
    public Task DeleteAsync(RequestActor actor, OrganiserKind kind, Guid id, long revision, CancellationToken ct) => store.DeleteAsync(actor, kind, DomainRules.NonEmptyId(id, "id"), DomainRules.Revision(revision), ct);
}
