using Tradeborn.Domain.Buildings;
using Tradeborn.Domain.Cities;
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

    Task SaveAsync(CityAggregate aggregate, CancellationToken cancellationToken = default);
}

/// <summary>A loaded city plus the plot layout, which is presentation state rather than economy state.</summary>
public sealed record CityAggregate(City City, string Name, int GridSize, IReadOnlyList<PlotState> Plots);

public sealed record PlotState(int Col, int Row, string Terrain, bool Unlocked);

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
