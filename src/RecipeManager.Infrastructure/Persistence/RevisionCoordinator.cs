using Microsoft.EntityFrameworkCore;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Households.Contracts;

namespace RecipeManager.Infrastructure.Persistence;

[Flags]
public enum ChangedDomain { None = 0, Recipes = 1, Planner = 2, Shopping = 4, Settings = 8, Membership = 16 }
public sealed record RevisionInvalidation(Guid HouseholdId, Guid? UserId, long Revision, ChangedDomain Domains,
    Guid? ListId = null, long? ListRevision = null, bool ListDeleted = false);

// Request-scoped recorder, not an outbox. Lost delivery is recovered by conditional sync.
public sealed class RevisionCoordinator(RecipeManagerDbContext database)
{
    private RequestActor? _actor;
    private ChangedDomain _domains;
    private bool _sourcesChanged;
    private bool _personalChanged;
    private readonly List<RevisionInvalidation> _committed = [];
    private readonly Dictionary<Guid, (long Revision, bool Deleted)> _listChanges = [];

    internal void Begin(RequestActor actor)
    {
        Discard();
        _actor = actor;
    }

    internal void Discard()
    {
        _actor = null;
        _domains = ChangedDomain.None;
        _sourcesChanged = false;
        _personalChanged = false;
        _listChanges.Clear();
    }

    private void RequireTransaction(Guid householdId)
    {
        if (_actor is null || database.Database.CurrentTransaction is null
            || _actor.HouseholdId != householdId || database.CurrentHouseholdId != householdId)
        {
            throw new InvalidOperationException("Revision recording requires the household transaction boundary.");
        }
    }

    public async Task SharedAsync(Guid householdId, ChangedDomain domains, bool shoppingSourcesChanged,
        CancellationToken cancellationToken = default)
    {
        RequireTransaction(householdId);
        const ChangedDomain all = ChangedDomain.Recipes | ChangedDomain.Planner | ChangedDomain.Shopping | ChangedDomain.Settings | ChangedDomain.Membership;
        if (domains == ChangedDomain.None || (domains & ~all) != 0
            || (shoppingSourcesChanged && (domains & (ChangedDomain.Recipes | ChangedDomain.Planner | ChangedDomain.Settings)) == 0))
        {
            throw new ArgumentException("Invalid revision domains/source change.", nameof(domains));
        }
        var household = await database.Households.SingleAsync(x => x.Id == householdId, cancellationToken);
        if (_domains == ChangedDomain.None) Increment(household, nameof(Household.ContentRevision));
        var added = domains & ~_domains;
        foreach (var (domain, property) in DomainProperties)
        {
            if ((added & domain) != 0) Increment(household, property);
        }
        if (shoppingSourcesChanged && !_sourcesChanged) Increment(household, nameof(Household.ShoppingSourcesRevision));
        _domains |= domains;
        _sourcesChanged |= shoppingSourcesChanged;
    }

    public async Task PersonalAsync(Guid householdId, Guid userId, CancellationToken cancellationToken = default)
    {
        RequireTransaction(householdId);
        if (userId != _actor!.UserId) throw new TransactionAccessException();
        if (!_personalChanged)
        {
            var user = await database.Users.SingleAsync(x => x.Id == userId, cancellationToken);
            Increment(user, nameof(user.PreferencesRevision));
            _personalChanged = true;
        }
    }

    public void ShoppingListChanged(Guid householdId, Guid listId, long revision, bool deleted)
    {
        RequireTransaction(householdId);
        if ((_domains & ChangedDomain.Shopping) == 0 || listId == Guid.Empty || revision < 1)
            throw new InvalidOperationException("List invalidations require the shared shopping revision in the same transaction.");
        _listChanges[listId] = (revision, deleted);
    }

    private void Increment<TEntity>(TEntity entity, string property) where TEntity : class
    {
        var value = database.Entry(entity).Property<long>(property);
        value.CurrentValue = checked(value.CurrentValue + 1);
    }

    internal void Committed()
    {
        if (_actor is null) return;
        if (_domains != ChangedDomain.None)
        {
            var household = database.Households.Local.Single(x => x.Id == _actor.HouseholdId);
            _committed.Add(new(_actor.HouseholdId, null, household.ContentRevision, _domains));
        }
        if (_personalChanged)
        {
            var user = database.Users.Local.Single(x => x.Id == _actor.UserId);
            _committed.Add(new(_actor.HouseholdId, user.Id, user.PreferencesRevision, ChangedDomain.None));
        }
        foreach (var (listId, change) in _listChanges)
            _committed.Add(new(_actor.HouseholdId, null, change.Revision, ChangedDomain.Shopping, listId, change.Revision, change.Deleted));
        Discard();
    }

    public IReadOnlyList<RevisionInvalidation> DrainCommitted()
    {
        var result = _committed.ToArray();
        _committed.Clear();
        return result;
    }

    private static readonly (ChangedDomain Domain, string Property)[] DomainProperties =
    [
        (ChangedDomain.Recipes, nameof(Household.RecipesRevision)),
        (ChangedDomain.Planner, nameof(Household.PlannerRevision)),
        (ChangedDomain.Shopping, nameof(Household.ShoppingRevision)),
        (ChangedDomain.Settings, nameof(Household.SettingsRevision)),
        (ChangedDomain.Membership, nameof(Household.MembershipRevision))
    ];
}
