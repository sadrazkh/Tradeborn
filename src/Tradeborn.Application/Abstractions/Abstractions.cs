using Tradeborn.Application.Contracts;
using Tradeborn.Domain.Buildings;
using Tradeborn.Domain.Cities;
using Tradeborn.Domain.Market;
using Tradeborn.Domain.Progression;
using Tradeborn.Domain.Economy;

namespace Tradeborn.Application.Abstractions;

/// <summary>
/// The static game definitions — resources, buildings, recipes — loaded once from seed data.
/// </summary>
/// <remarks>
/// Definitions are read constantly and change only on deploy, so they are loaded into memory
/// at startup rather than queried per request. This is what keeps the city read path to a
/// single round trip (PERFORMANCE_BUDGET.md §6).
/// </remarks>
public interface IGameCatalog
{
    IReadOnlyList<ResourceDefinition> Resources { get; }
    IReadOnlyList<BuildingDefinition> Buildings { get; }

    BuildingDefinition GetBuilding(string definitionId);
    bool TryGetBuilding(string definitionId, out BuildingDefinition definition);
}

public sealed record ResourceDefinition(ResourceId Id, string Tier, long BasePriceCoins, long MarketDepth);

/// <summary>
/// Loads and persists a player's city aggregate.
/// </summary>
/// <remarks>
/// The City is the transactional boundary (ARCHITECTURE.md §5). Loading takes a row lock so
/// that concurrent commands for the same city serialise — see SECURITY_MODEL.md T4.
/// </remarks>
public interface ICityStore
{
    /// <summary>Loads a city with its buildings, inventory and plots. Null if the player has none.</summary>
    Task<CityAggregate?> LoadAsync(Guid playerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a city while holding a row lock on it for the rest of the transaction.
    /// </summary>
    /// <remarks>
    /// <c>SELECT … FOR UPDATE</c>. Every economic command takes this lock, which serialises
    /// commands for one city and is what makes the double-spend test (SECURITY_MODEL.md T4)
    /// pass. Contention is naturally near zero — one player per city — so this costs nothing
    /// in practice while removing an entire class of race condition.
    /// </remarks>
    Task<CityAggregate?> LoadForUpdateAsync(Guid playerId, CancellationToken cancellationToken = default);

    Task SaveAsync(CityAggregate aggregate, CancellationToken cancellationToken = default);
}

/// <summary>A loaded city plus display metadata. Plots live on the <see cref="City"/> itself,
/// because placement validity is a domain rule rather than a presentation concern.</summary>
public sealed record CityAggregate(Guid Id, City City, string Name, int GridSize);

/// <summary>
/// Transaction boundary for economic commands.
/// </summary>
/// <remarks>
/// Every write in ARCHITECTURE.md §6 runs inside one of these: lock, settle, validate, apply,
/// audit, record idempotency, commit. There is deliberately no second way to change a balance.
/// </remarks>
public interface IUnitOfWork
{
    Task<IAsyncDisposable> BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task CommitAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Stores command responses by Idempotency-Key so a retry replays instead of re-executing.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>The stored response for this key, or null if this is the first attempt.</summary>
    Task<string?> TryGetResponseAsync(
        Guid playerId, string key, string operation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the response. Must run inside the command's own transaction.
    /// </summary>
    /// <returns>
    /// False if the key was already recorded — meaning a concurrent duplicate won the race and
    /// this attempt must be rolled back rather than applied a second time.
    /// </returns>
    Task<bool> TryRecordAsync(
        Guid playerId,
        string key,
        string operation,
        string responseBody,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The NPC market's prices — global state shared by every player.
/// </summary>
/// <remarks>
/// Unlike a city, this row is contended: two players selling wood at the same instant both
/// move the same price. Commands therefore lock the price row, and always <b>after</b> the
/// city row — a consistent lock order across every handler is what keeps two concurrent
/// sellers from deadlocking each other.
/// </remarks>
public interface IMarketStore
{
    /// <summary>Every tradable resource's current price.</summary>
    Task<IReadOnlyList<MarketPrice>> LoadAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Loads one price and holds a row lock on it for the rest of the transaction.</summary>
    Task<MarketPrice?> LoadForUpdateAsync(ResourceId resource, CancellationToken cancellationToken = default);

    Task SaveAsync(MarketPrice price, CancellationToken cancellationToken = default);

    /// <summary>Appends a price point for the sparkline.</summary>
    Task RecordHistoryAsync(MarketPrice price, DateTimeOffset atUtc, CancellationToken cancellationToken = default);

    /// <summary>Recent price points per resource, oldest first.</summary>
    Task<IReadOnlyDictionary<ResourceId, IReadOnlyList<PricePoint>>> LoadHistoryAsync(
        int pointsPerResource, CancellationToken cancellationToken = default);
}

public sealed record PricePoint(DateTimeOffset AtUtc, long PriceCent);

/// <summary>
/// Which tutorial rewards a player has already collected.
/// </summary>
/// <remarks>
/// The only quest state that is stored. Completion is derived from the city every time it is
/// asked; what must be durable is the fact a reward was <i>paid</i>, because that is real money.
/// </remarks>
public interface IQuestStore
{
    Task<IReadOnlySet<string>> LoadClaimedAsync(Guid playerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a claim.
    /// </summary>
    /// <returns>
    /// False if this player had already claimed it — the primary key collided, which is the
    /// mechanism rather than an error (SECURITY_MODEL.md T5).
    /// </returns>
    Task<bool> TryRecordClaimAsync(
        Guid playerId, string questId, DateTimeOffset atUtc, CancellationToken cancellationToken = default);
}


/// <summary>A player's level and experience, stored beside their account.</summary>
public interface IPlayerStore
{
    Task<PlayerProgress?> LoadProgressAsync(Guid playerId, CancellationToken cancellationToken = default);

    Task SaveProgressAsync(Guid playerId, PlayerProgress progress, CancellationToken cancellationToken = default);
}

/// <summary>Append-only record of economic mutations (ADR-004).</summary>
public interface IAuditLog
{
    Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}

public sealed record AuditEntry(
    Guid PlayerId,
    Guid CityId,
    string Kind,
    long MoneyDeltaCent,
    long BalanceAfterCent,
    IReadOnlyDictionary<string, long> ResourceDeltas,
    string? CorrelationId,
    string? IdempotencyKey,
    IReadOnlyDictionary<string, string>? Metadata = null,
    /// <summary>Set only when an operator acted on someone else's city.</summary>
    Guid? ActorPlayerId = null);

/// <summary>
/// Cache abstraction. Backed by Redis in production; an in-memory implementation is used when
/// Redis is unreachable so the app still boots (DECISIONS_REQUIRED.md A-01).
/// </summary>
public interface ICacheStore
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task SetAsync(string key, string value, TimeSpan ttl, CancellationToken cancellationToken = default);
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>
/// AI extension point (ARCHITECTURE.md §10). A no-op implementation ships in the slice and
/// nothing in the critical path calls it — no paid AI call will ever sit in a gameplay request.
/// </summary>
public interface IAdvisorService
{
    Task<string?> ExplainAsync(Guid playerId, string question, CancellationToken cancellationToken = default);
}

/// <summary>
/// Read and tuning access for the admin panel.
/// </summary>
/// <remarks>
/// Kept separate from the gameplay stores on purpose. Admin queries are wide, unbounded and
/// cross-player — exactly the shape that must never leak into a request path a player can
/// reach — so they live behind their own interface guarded by its own policy.
/// </remarks>
public interface IAdminStore
{
    Task<AdminPlayerPageDto> ListPlayersAsync(
        int page, int pageSize, string? search, CancellationToken cancellationToken = default);

    Task<AdminCityDto?> InspectCityAsync(Guid playerId, CancellationToken cancellationToken = default);

    Task<AuditPageDto> ReadAuditAsync(
        Guid? playerId, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<AdminSystemDto> ReadSystemAsync(CancellationToken cancellationToken = default);

    Task<EconomyTuningDto> ReadTuningAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes tuning values and reloads the in-memory catalog.
    /// </summary>
    /// <remarks>
    /// The reload is the point. Without it the rows would change while every running request
    /// kept using the catalog loaded at startup — the panel would appear to work and change
    /// nothing, which is the worst possible outcome for a tuning tool.
    /// </remarks>
    Task<ApplyTuningResponse> ApplyTuningAsync(
        EconomyTuningDto tuning, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FeatureFlagDto>> ListFlagsAsync(CancellationToken cancellationToken = default);

    Task<FeatureFlagDto> SetFlagAsync(
        string key, bool enabled, string? description, CancellationToken cancellationToken = default);
}
