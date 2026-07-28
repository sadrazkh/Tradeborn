namespace Tradeborn.Infrastructure.Persistence;

/// <summary>
/// Persistence models.
/// </summary>
/// <remarks>
/// <para>
/// These are deliberately separate from the domain model. <c>Tradeborn.Domain</c> has zero
/// external dependencies (ADR-002) and is asserted to have none, so it cannot carry EF
/// attributes or navigation properties. Mapping lives in <see cref="CityStore"/>.
/// </para>
/// <para>
/// The cost is a mapper; the benefit is that the database schema and the economy model can
/// evolve independently, and the domain stays trivially unit-testable with no I/O.
/// </para>
/// </remarks>
public sealed class PlayerEntity
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int Level { get; set; } = 1;
    public long Xp { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }

    public CityEntity? City { get; set; }
}

public sealed class CityEntity
{
    public Guid Id { get; set; }
    public Guid PlayerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int GridSize { get; set; }
    public long BalanceCent { get; set; }
    public DateTimeOffset LastSettledAtUtc { get; set; }

    /// <summary>PostgreSQL system column, mapped as an EF concurrency token (SECURITY_MODEL.md T4).</summary>
    public uint Version { get; set; }

    public PlayerEntity? Player { get; set; }
    public List<CityBuildingEntity> Buildings { get; set; } = [];
    public List<CityInventoryEntity> Inventory { get; set; } = [];
    public List<CityPlotEntity> Plots { get; set; } = [];
}

public sealed class CityBuildingEntity
{
    public Guid Id { get; set; }
    public Guid CityId { get; set; }
    public string DefinitionId { get; set; } = string.Empty;
    public int Col { get; set; }
    public int Row { get; set; }
    public int Level { get; set; }
    public string State { get; set; } = string.Empty;
    public string HaltReason { get; set; } = string.Empty;
    public long ProgressMilliseconds { get; set; }

    /// <summary>When an in-flight build or upgrade finishes. Null when nothing is in flight.</summary>
    public DateTimeOffset? CompletesAtUtc { get; set; }

    /// <summary>Level once the in-flight work lands. Equal to <see cref="Level"/> when idle.</summary>
    public int PendingLevel { get; set; }
}

public sealed class CityInventoryEntity
{
    public Guid CityId { get; set; }
    public string ResourceId { get; set; } = string.Empty;
    public long Quantity { get; set; }
}

public sealed class CityPlotEntity
{
    public Guid CityId { get; set; }
    public int Col { get; set; }
    public int Row { get; set; }
    public string Terrain { get; set; } = string.Empty;
    public bool Unlocked { get; set; }
}

// ---------------------------------------------------------------------------------------
// Catalog (seed data). Read-only at runtime; written only by the seeder.
// ---------------------------------------------------------------------------------------

public sealed class ResourceDefinitionEntity
{
    public string Id { get; set; } = string.Empty;
    public string Tier { get; set; } = string.Empty;
    public long BasePriceCoins { get; set; }
    public long MarketDepth { get; set; }
}

public sealed class BuildingDefinitionEntity
{
    public string Id { get; set; } = string.Empty;
    public string? RecipeId { get; set; }
    public long StoragePerResource { get; set; }
    public long BuildCostCoins { get; set; }
    public long BuildSeconds { get; set; }
    public int UnlockCityLevel { get; set; }
    public bool PrePlaced { get; set; }

    /// <summary>The Town Hall. Its level caps city level, so breadth alone cannot unlock tiers.</summary>
    public bool IsCityCentre { get; set; }

    /// <summary>Materials required in addition to coins. Empty for buildings that cost only money.</summary>
    public List<BuildingCostEntity> Costs { get; set; } = [];
}

/// <summary>A material component of a building's level-1 build cost.</summary>
public sealed class BuildingCostEntity
{
    public int Id { get; set; }
    public string BuildingId { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public long Quantity { get; set; }
}

public sealed class RecipeEntity
{
    public string Id { get; set; } = string.Empty;
    public long CycleMilliseconds { get; set; }

    /// <summary>Producers resolve before consumers within a settlement step. Computed by the seeder.</summary>
    public int TopologicalRank { get; set; }

    public List<RecipeIngredientEntity> Ingredients { get; set; } = [];
}

public sealed class RecipeIngredientEntity
{
    public int Id { get; set; }
    public string RecipeId { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public long Quantity { get; set; }

    /// <summary>False for inputs, true for outputs.</summary>
    public bool IsOutput { get; set; }
}

/// <summary>
/// A recorded command response, keyed by the client's Idempotency-Key.
/// </summary>
/// <remarks>
/// Written inside the same transaction as the command it describes (SECURITY_MODEL.md T3).
/// That is what makes a crash between "apply" and "record" impossible: either both land or
/// neither does. A retried request returns the stored response without re-executing, so a
/// flaky network can never charge a player twice.
/// </remarks>
public sealed class IdempotencyKeyEntity
{
    public Guid PlayerId { get; set; }
    public string Key { get; set; } = string.Empty;

    /// <summary>Distinguishes the same key replayed against a different command.</summary>
    public string Operation { get; set; } = string.Empty;

    public int StatusCode { get; set; }
    public string ResponseBody { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}

/// <summary>
/// Append-only record of every economic mutation.
/// </summary>
/// <remarks>
/// This is what lets Tradeborn reconstruct and audit balances without event sourcing
/// (ADR-004). <see cref="BalanceAfterCent"/> makes reconciliation cheap: a test sums the
/// deltas and asserts they equal the stored balance, catching any mutation that bypassed
/// the ledger.
/// </remarks>
public sealed class AuditLedgerEntity
{
    public long Id { get; set; }
    public Guid PlayerId { get; set; }
    public Guid CityId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }

    /// <summary>e.g. <c>construction.started</c>, <c>upgrade.started</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    public long MoneyDeltaCent { get; set; }
    public long BalanceAfterCent { get; set; }

    /// <summary>Resource deltas as JSON; negative values are spends.</summary>
    public string ResourceDeltas { get; set; } = "{}";

    public string? CorrelationId { get; set; }
    public string? IdempotencyKey { get; set; }
    public string Metadata { get; set; } = "{}";
}

public sealed class RefreshTokenEntity
{
    public Guid Id { get; set; }
    public Guid PlayerId { get; set; }

    /// <summary>SHA-256 of the token. The raw value is never stored.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Groups rotated tokens so that replay can revoke the whole lineage (ADR-007).</summary>
    public Guid FamilyId { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public bool Used { get; set; }
}
