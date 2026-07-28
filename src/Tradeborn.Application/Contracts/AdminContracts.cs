namespace Tradeborn.Application.Contracts;

// ---------------------------------------------------------------------------------------
// Inspection
// ---------------------------------------------------------------------------------------

/// <summary>
/// A player as the admin list shows them.
/// </summary>
/// <remarks>
/// Deliberately no email. Support staff need to find and understand an account, not to read
/// personal data — so the identifier they work with is the player id, and contact details stay
/// out of the panel entirely (SECURITY_MODEL.md, privacy).
/// </remarks>
public sealed record AdminPlayerDto(
    Guid Id,
    string DisplayName,
    string Role,
    int Level,
    long Xp,
    DateTimeOffset CreatedAtUtc,
    string? CityName,
    long BalanceCoins,
    int BuildingCount,
    DateTimeOffset? LastSettledAtUtc);

public sealed record AdminPlayerPageDto(
    IReadOnlyList<AdminPlayerDto> Players,
    int Page,
    int PageSize,
    int Total);

/// <summary>A full picture of one city, for answering "why does this look wrong?".</summary>
public sealed record AdminCityDto(
    Guid PlayerId,
    string DisplayName,
    string CityName,
    long BalanceCoins,
    long CapacityPerResource,
    int CityLevel,
    long DeliveriesCompleted,
    long SalesCompleted,
    DateTimeOffset LastSettledAtUtc,
    IReadOnlyList<BuildingDto> Buildings,
    IReadOnlyList<ResourceBalanceDto> Resources,
    IReadOnlyList<TransportDto> Transports,
    IReadOnlyList<string> ClaimedQuests);

public sealed record AuditEntryDto(
    long Id,
    Guid PlayerId,
    Guid? ActorPlayerId,
    DateTimeOffset OccurredAtUtc,
    string Kind,
    long MoneyDeltaCent,
    long BalanceAfterCent,
    string ResourceDeltas,
    string? CorrelationId,
    string Metadata);

public sealed record AuditPageDto(
    IReadOnlyList<AuditEntryDto> Entries,
    int Page,
    int PageSize,
    int Total);

// ---------------------------------------------------------------------------------------
// Economy tuning
// ---------------------------------------------------------------------------------------

/// <summary>
/// The whole tunable economy in one document.
/// </summary>
/// <remarks>
/// Returned and accepted as a single payload rather than field-by-field endpoints. Economy
/// numbers are only meaningful relative to each other — raising bread's price without looking
/// at flour is how a balance pass goes wrong — so the panel edits them together.
/// </remarks>
public sealed record EconomyTuningDto(
    IReadOnlyList<ResourceTuningDto> Resources,
    IReadOnlyList<BuildingTuningDto> Buildings,
    IReadOnlyList<RecipeTuningDto> Recipes);

public sealed record ResourceTuningDto(string Id, string Tier, long BasePriceCoins, long MarketDepth);

public sealed record BuildingTuningDto(
    string Id,
    long StoragePerResource,
    long BuildCostCoins,
    long BuildSeconds,
    int UnlockCityLevel);

public sealed record RecipeTuningDto(string Id, long CycleMilliseconds);

public sealed record ApplyTuningResponse(
    bool Accepted,
    string? Message,
    int ResourcesUpdated,
    int BuildingsUpdated,
    int RecipesUpdated,
    DateTimeOffset AppliedAtUtc);

// ---------------------------------------------------------------------------------------
// Operator actions
// ---------------------------------------------------------------------------------------

/// <summary>Grants coins and materials to a city, for support and testing.</summary>
/// <remarks>
/// Bounded on purpose. An unbounded grant endpoint is a single compromised admin account away
/// from wrecking the economy, and no legitimate support case needs more than this.
/// </remarks>
public sealed record GrantRequest(long Coins, IReadOnlyList<GrantResourceDto> Resources, string Reason);

public sealed record GrantResourceDto(string Resource, long Quantity);

public sealed record AdminActionResponse(
    bool Accepted,
    string? Message,
    long BalanceCoins,
    IReadOnlyList<ResourceBalanceDto> Resources,
    DateTimeOffset ServerTimeUtc);

// ---------------------------------------------------------------------------------------
// Feature flags
// ---------------------------------------------------------------------------------------

public sealed record FeatureFlagDto(string Key, bool Enabled, string? Description, DateTimeOffset UpdatedAtUtc);

public sealed record SetFeatureFlagRequest(bool Enabled, string? Description);

// ---------------------------------------------------------------------------------------
// System health
// ---------------------------------------------------------------------------------------

public sealed record AdminSystemDto(
    DateTimeOffset ServerTimeUtc,
    int Players,
    int Cities,
    long TransportsInFlight,
    long AuditEntries,
    long MoneySupplyCoins,
    bool RedisConfigured,
    string Environment);
