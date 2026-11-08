using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RecipeManager.Core.Modules.Households;
using RecipeManager.Core.Modules.Imports;
using RecipeManager.Core.Modules.Ingredients;
using RecipeManager.Core.Modules.Leftovers;
using RecipeManager.Core.Modules.MealPlanning;
using RecipeManager.Core.Modules.Recipes;
using RecipeManager.Core.Modules.Shopping;
using RecipeManager.Infrastructure.Persistence.Imports;
using RecipeManager.Infrastructure.Persistence.Ingredients;
using RecipeManager.Infrastructure.Persistence.MealPlanning;
using RecipeManager.Infrastructure.Persistence.Recipes;
using RecipeManager.Infrastructure.Persistence.Shopping;
using RecipeManager.Infrastructure.Security;

namespace RecipeManager.Infrastructure.Persistence;

public sealed class RecipeManagerDbContext(DbContextOptions<RecipeManagerDbContext> options)
    : IdentityUserContext<ApplicationUser, Guid>(options)
{
    public Guid? CurrentHouseholdId { get; private set; }
    public void BindHouseholdScope(Guid householdId)
        => CurrentHouseholdId = Core.Shared.DomainRules.NonEmptyId(householdId, "householdId");
    public void ClearHouseholdScope() => CurrentHouseholdId = null;

    public DbSet<ImportDraft> ImportDrafts => Set<ImportDraft>();
    public DbSet<ImportHandoff> ImportHandoffs => Set<ImportHandoff>();

    public DbSet<MealSlot> MealSlots => Set<MealSlot>();
    public DbSet<ScheduledMeal> ScheduledMeals => Set<ScheduledMeal>();
    public DbSet<CookingEvent> CookingEvents => Set<CookingEvent>();
    public DbSet<Allocation> Allocations => Set<Allocation>();
    public DbSet<ShoppingList> ShoppingLists => Set<ShoppingList>();
    public DbSet<ShoppingItem> ShoppingItems => Set<ShoppingItem>();

    public DbSet<Collection> Collections => Set<Collection>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<RecipeCollection> RecipeCollections => Set<RecipeCollection>();
    public DbSet<RecipeTag> RecipeTags => Set<RecipeTag>();

    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<IngredientGroup> IngredientGroups => Set<IngredientGroup>();
    public DbSet<RecipeIngredient> RecipeIngredients => Set<RecipeIngredient>();
    public DbSet<Instruction> Instructions => Set<Instruction>();
    public DbSet<Favourite> Favourites => Set<Favourite>();
    public DbSet<ImageAsset> ImageAssets => Set<ImageAsset>();

    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<IngredientAlias> IngredientAliases => Set<IngredientAlias>();
    public DbSet<IngredientName> IngredientNames => Set<IngredientName>();
    public DbSet<IngredientCategory> IngredientCategories => Set<IngredientCategory>();

    public DbSet<CreationRetryRecord> CreationRetries => Set<CreationRetryRecord>();

    public DbSet<Household> Households => Set<Household>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<Installation> Installations => Set<Installation>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfiguration(new ImportDraftConfiguration());
        builder.ApplyConfiguration(new ImportHandoffConfiguration());
        builder.Entity<ImportDraft>().HasQueryFilter(x => CurrentHouseholdId != null && x.HouseholdId == CurrentHouseholdId);
        builder.Entity<ImportHandoff>().HasQueryFilter(x => CurrentHouseholdId != null && x.HouseholdId == CurrentHouseholdId);
        builder.ApplyConfiguration(new ShoppingListConfiguration());
        builder.ApplyConfiguration(new ShoppingItemConfiguration());
        builder.Entity<ShoppingList>().HasQueryFilter(x => CurrentHouseholdId != null && x.HouseholdId == CurrentHouseholdId);
        builder.Entity<ShoppingItem>().HasQueryFilter(x => CurrentHouseholdId != null && x.HouseholdId == CurrentHouseholdId);
        builder.ApplyConfiguration(new MealSlotConfiguration());
        builder.ApplyConfiguration(new ScheduledMealConfiguration());
        builder.ApplyConfiguration(new CookingEventConfiguration());
        builder.ApplyConfiguration(new AllocationConfiguration());
        builder.Entity<MealSlot>().HasQueryFilter(x => CurrentHouseholdId != null && x.HouseholdId == CurrentHouseholdId);
        builder.Entity<ScheduledMeal>().HasQueryFilter(x => CurrentHouseholdId != null && x.HouseholdId == CurrentHouseholdId);
        builder.Entity<CookingEvent>().HasQueryFilter(x => CurrentHouseholdId != null && x.HouseholdId == CurrentHouseholdId);
        builder.Entity<Allocation>().HasQueryFilter(x => CurrentHouseholdId != null && x.HouseholdId == CurrentHouseholdId);
        builder.ApplyConfiguration(new CollectionConfiguration());
        builder.ApplyConfiguration(new TagConfiguration());
        builder.ApplyConfiguration(new RecipeCollectionConfiguration());
        builder.ApplyConfiguration(new RecipeTagConfiguration());
        builder.Entity<Collection>().HasQueryFilter(x => CurrentHouseholdId != null && x.HouseholdId == CurrentHouseholdId);
        builder.Entity<Tag>().HasQueryFilter(x => CurrentHouseholdId != null && x.HouseholdId == CurrentHouseholdId);
        builder.Entity<RecipeCollection>().HasQueryFilter(x => CurrentHouseholdId != null && x.HouseholdId == CurrentHouseholdId);
        builder.Entity<RecipeTag>().HasQueryFilter(x => CurrentHouseholdId != null && x.HouseholdId == CurrentHouseholdId);
        builder.ApplyConfiguration(new RecipeConfiguration());
        builder.ApplyConfiguration(new IngredientGroupConfiguration());
        builder.ApplyConfiguration(new RecipeIngredientConfiguration());
        builder.ApplyConfiguration(new InstructionConfiguration());
        builder.ApplyConfiguration(new FavouriteConfiguration());
        builder.ApplyConfiguration(new ImageAssetConfiguration());
        builder.Entity<Recipe>().HasQueryFilter(x => CurrentHouseholdId != null && x.HouseholdId == CurrentHouseholdId);
        builder.Entity<IngredientGroup>().HasQueryFilter(x => CurrentHouseholdId != null && x.HouseholdId == CurrentHouseholdId);
        builder.Entity<RecipeIngredient>().HasQueryFilter(x => CurrentHouseholdId != null && x.HouseholdId == CurrentHouseholdId);
        builder.Entity<Instruction>().HasQueryFilter(x => CurrentHouseholdId != null && x.HouseholdId == CurrentHouseholdId);
        builder.Entity<Favourite>().HasQueryFilter(x => CurrentHouseholdId != null && x.HouseholdId == CurrentHouseholdId);
        builder.Entity<ImageAsset>().HasQueryFilter(x => CurrentHouseholdId != null && x.HouseholdId == CurrentHouseholdId);
        builder.ApplyConfiguration(new IngredientConfiguration());
        builder.ApplyConfiguration(new IngredientAliasConfiguration());
        builder.ApplyConfiguration(new IngredientNameConfiguration());
        builder.ApplyConfiguration(new IngredientCategoryConfiguration());
        builder.Entity<Ingredient>().HasQueryFilter(x => CurrentHouseholdId != null && x.HouseholdId == CurrentHouseholdId);
        builder.Entity<IngredientAlias>().HasQueryFilter(x => CurrentHouseholdId != null && x.HouseholdId == CurrentHouseholdId);
        builder.Entity<IngredientName>().HasQueryFilter(x => CurrentHouseholdId != null && x.HouseholdId == CurrentHouseholdId);
        builder.Entity<IngredientCategory>().HasQueryFilter(x => CurrentHouseholdId != null && x.HouseholdId == CurrentHouseholdId);
        builder.Entity<CreationRetryRecord>(retry =>
        {
            retry.ToTable("CreationRetries", "households");
            retry.HasKey(x => new { x.HouseholdId, x.UserId, x.Route, x.Key });
            retry.HasQueryFilter(x => CurrentHouseholdId != null && x.HouseholdId == CurrentHouseholdId);
            retry.Property(x => x.Route).HasMaxLength(200);
            retry.Property(x => x.PayloadHash).HasMaxLength(64);
            retry.Property(x => x.Body).HasColumnType("text");
            retry.Property(x => x.Location).HasMaxLength(2048);
            retry.Property(x => x.ETag).HasMaxLength(200);
            retry.HasIndex(x => x.ExpiresAt);
            retry.HasOne<Membership>().WithMany().HasForeignKey(x => new { x.HouseholdId, x.UserId })
                .HasPrincipalKey(x => new { x.HouseholdId, x.UserId }).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ApplicationUser>(user =>
        {
            user.ToTable("Users", "identity", table => table.HasCheckConstraint("CK_Users_PreferencesObject", "jsonb_typeof(\"PreferencesJson\") = 'object'"));
            user.HasIndex(x => x.NormalizedEmail).IsUnique();
            user.Property(x => x.IsActive).HasDefaultValue(true);
            user.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            user.Property(x => x.PreferencesJson).HasColumnType("jsonb").IsRequired();
            user.Property(x => x.PreferencesRevision).IsConcurrencyToken();
        });
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims", "identity");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins", "identity");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens", "identity");
        builder.Entity<Household>(household =>
        {
            household.ToTable("Households", "households", table => table.HasCheckConstraint("CK_Households_Revision", "\"Revision\" >= 0"));
            household.HasKey(x => x.Id);
            household.HasQueryFilter(x => CurrentHouseholdId != null && x.Id == CurrentHouseholdId);
            household.Property(x => x.Name).HasMaxLength(200).IsRequired();
            household.Property(x => x.TimeZone).HasMaxLength(200).IsRequired();
            household.Property(x => x.Revision).IsConcurrencyToken();
        });
        builder.Entity<Installation>(installation =>
        {
            installation.ToTable("Installation", "households");
            installation.HasKey(x => x.Id);
            installation.Property(x => x.Revision).IsConcurrencyToken();
            installation.HasData(new { Id = Installation.SingletonId, SetupCompletedAt = (DateTimeOffset?)null, Revision = 1L });
        });
        builder.Entity<Membership>(membership =>
        {
            membership.ToTable("Memberships", "households", table =>
            {
                table.HasCheckConstraint("CK_Memberships_Role", "\"Role\" IN ('Administrator', 'Member')");
                table.HasCheckConstraint("CK_Memberships_Revision", "\"Revision\" >= 0");
            });
            membership.HasKey(x => new { x.HouseholdId, x.Id });
            membership.HasQueryFilter(x => CurrentHouseholdId != null && x.HouseholdId == CurrentHouseholdId);
            membership.HasAlternateKey(x => new { x.HouseholdId, x.UserId });
            membership.HasIndex(x => x.UserId).IsUnique();
            membership.Property(x => x.Role).HasConversion<string>().HasMaxLength(16).IsRequired();
            membership.Property(x => x.IsActive).HasDefaultValue(true);
            membership.Property(x => x.Revision).IsConcurrencyToken();
            membership.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict);
            membership.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<Invitation>(invitation =>
        {
            invitation.ToTable("Invitations", "households", table => table.HasCheckConstraint("CK_Invitations_TokenHash", "length(\"TokenHash\") = 64"));
            invitation.HasKey(x => new { x.HouseholdId, x.Id });
            invitation.HasQueryFilter(x => CurrentHouseholdId != null && x.HouseholdId == CurrentHouseholdId);
            invitation.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            invitation.HasIndex(x => x.TokenHash).IsUnique();
            invitation.Property(x => x.Revision).IsConcurrencyToken();
            invitation.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict);
            invitation.HasOne<Membership>().WithMany().HasForeignKey(x => new { x.HouseholdId, x.CreatedBy })
                .HasPrincipalKey(x => new { x.HouseholdId, x.UserId }).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
