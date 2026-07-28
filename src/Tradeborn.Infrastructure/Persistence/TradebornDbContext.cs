using Microsoft.EntityFrameworkCore;

namespace Tradeborn.Infrastructure.Persistence;

public sealed class TradebornDbContext(DbContextOptions<TradebornDbContext> options) : DbContext(options)
{
    public DbSet<PlayerEntity> Players => Set<PlayerEntity>();
    public DbSet<CityEntity> Cities => Set<CityEntity>();
    public DbSet<CityBuildingEntity> CityBuildings => Set<CityBuildingEntity>();
    public DbSet<CityInventoryEntity> CityInventory => Set<CityInventoryEntity>();
    public DbSet<CityPlotEntity> CityPlots => Set<CityPlotEntity>();
    public DbSet<ResourceDefinitionEntity> ResourceDefinitions => Set<ResourceDefinitionEntity>();
    public DbSet<BuildingDefinitionEntity> BuildingDefinitions => Set<BuildingDefinitionEntity>();
    public DbSet<RecipeEntity> Recipes => Set<RecipeEntity>();
    public DbSet<RecipeIngredientEntity> RecipeIngredients => Set<RecipeIngredientEntity>();
    public DbSet<BuildingCostEntity> BuildingCosts => Set<BuildingCostEntity>();
    public DbSet<RefreshTokenEntity> RefreshTokens => Set<RefreshTokenEntity>();
    public DbSet<IdempotencyKeyEntity> IdempotencyKeys => Set<IdempotencyKeyEntity>();
    public DbSet<AuditLedgerEntity> AuditLedger => Set<AuditLedgerEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var builder = modelBuilder;

        builder.Entity<PlayerEntity>(entity =>
        {
            entity.ToTable("players");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Email).HasMaxLength(256).IsRequired();
            entity.HasIndex(e => e.Email).IsUnique();
            entity.Property(e => e.PasswordHash).HasMaxLength(256).IsRequired();
            entity.Property(e => e.DisplayName).HasMaxLength(64).IsRequired();
            entity.HasOne(e => e.City)
                  .WithOne(c => c.Player)
                  .HasForeignKey<CityEntity>(c => c.PlayerId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<CityEntity>(entity =>
        {
            entity.ToTable("cities");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(64).IsRequired();
            entity.HasIndex(e => e.PlayerId).IsUnique();

            // PostgreSQL's xmin gives an optimistic concurrency token for free — no extra
            // column, no manual bumping. Defence in depth behind the row lock in CityStore.
            entity.Property(e => e.Version).IsRowVersion();

            entity.HasMany(e => e.Buildings).WithOne().HasForeignKey(b => b.CityId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(e => e.Inventory).WithOne().HasForeignKey(i => i.CityId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(e => e.Plots).WithOne().HasForeignKey(p => p.CityId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<CityBuildingEntity>(entity =>
        {
            entity.ToTable("city_buildings");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DefinitionId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.State).HasMaxLength(32).IsRequired();
            entity.Property(e => e.HaltReason).HasMaxLength(32).IsRequired();

            // One building per plot, enforced by the database rather than by application
            // logic alone — this is what makes the concurrent-build test (T4) pass even if
            // two requests somehow got past the row lock.
            entity.HasIndex(e => new { e.CityId, e.Col, e.Row }).IsUnique();
        });

        builder.Entity<CityInventoryEntity>(entity =>
        {
            entity.ToTable("city_inventory");
            entity.HasKey(e => new { e.CityId, e.ResourceId });
            entity.Property(e => e.ResourceId).HasMaxLength(64);
        });

        builder.Entity<CityPlotEntity>(entity =>
        {
            entity.ToTable("city_plots");
            entity.HasKey(e => new { e.CityId, e.Col, e.Row });
            entity.Property(e => e.Terrain).HasMaxLength(32).IsRequired();
        });

        builder.Entity<ResourceDefinitionEntity>(entity =>
        {
            entity.ToTable("resource_definitions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(64);
            entity.Property(e => e.Tier).HasMaxLength(32).IsRequired();
        });

        builder.Entity<BuildingDefinitionEntity>(entity =>
        {
            entity.ToTable("building_definitions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(64);
            entity.Property(e => e.RecipeId).HasMaxLength(64);
            entity.HasMany(e => e.Costs).WithOne().HasForeignKey(c => c.BuildingId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<BuildingCostEntity>(entity =>
        {
            entity.ToTable("building_costs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.BuildingId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.ResourceId).HasMaxLength(64).IsRequired();
            entity.HasIndex(e => new { e.BuildingId, e.ResourceId }).IsUnique();
        });

        builder.Entity<IdempotencyKeyEntity>(entity =>
        {
            entity.ToTable("idempotency_keys");

            // Composite primary key: the uniqueness constraint IS the mechanism. Two
            // concurrent replays race to insert and exactly one wins (SECURITY_MODEL.md T3).
            entity.HasKey(e => new { e.PlayerId, e.Key });
            entity.Property(e => e.Key).HasMaxLength(128);
            entity.Property(e => e.Operation).HasMaxLength(64).IsRequired();
            entity.HasIndex(e => e.CreatedAtUtc);
        });

        builder.Entity<AuditLedgerEntity>(entity =>
        {
            entity.ToTable("audit_ledger");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Kind).HasMaxLength(64).IsRequired();
            entity.Property(e => e.CorrelationId).HasMaxLength(128);
            entity.Property(e => e.IdempotencyKey).HasMaxLength(128);
            entity.Property(e => e.ResourceDeltas).HasColumnType("jsonb");
            entity.Property(e => e.Metadata).HasColumnType("jsonb");
            entity.HasIndex(e => new { e.PlayerId, e.OccurredAtUtc });
            entity.HasIndex(e => e.OccurredAtUtc);
        });

        builder.Entity<RecipeEntity>(entity =>
        {
            entity.ToTable("recipes");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(64);
            entity.HasMany(e => e.Ingredients).WithOne().HasForeignKey(i => i.RecipeId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<RecipeIngredientEntity>(entity =>
        {
            entity.ToTable("recipe_ingredients");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RecipeId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.ResourceId).HasMaxLength(64).IsRequired();
            entity.HasIndex(e => new { e.RecipeId, e.ResourceId, e.IsOutput }).IsUnique();
        });

        builder.Entity<RefreshTokenEntity>(entity =>
        {
            entity.ToTable("refresh_tokens");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TokenHash).HasMaxLength(128).IsRequired();
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => e.FamilyId);
            entity.HasIndex(e => e.PlayerId);
        });

        base.OnModelCreating(modelBuilder);
    }
}
