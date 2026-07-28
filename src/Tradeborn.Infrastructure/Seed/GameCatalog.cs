using Microsoft.EntityFrameworkCore;
using Tradeborn.Application.Abstractions;
using Tradeborn.Domain.Buildings;
using Tradeborn.Domain.Economy;
using Tradeborn.Domain.Production;
using Tradeborn.Infrastructure.Persistence;

namespace Tradeborn.Infrastructure.Seed;

/// <summary>
/// In-memory view of the seeded catalog, materialised as domain objects at startup.
/// </summary>
/// <remarks>
/// Registered as a singleton and loaded once. Definitions are read on every request and
/// change only on deploy, so querying them per request would add joins to the hot path for
/// data that never varies.
/// </remarks>
public sealed class GameCatalog : IGameCatalog
{
    private readonly Dictionary<string, BuildingDefinition> buildingsById;

    private GameCatalog(IReadOnlyList<ResourceDefinition> resources, IReadOnlyList<BuildingDefinition> buildings)
    {
        Resources = resources;
        Buildings = buildings;
        buildingsById = buildings.ToDictionary(b => b.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<ResourceDefinition> Resources { get; }
    public IReadOnlyList<BuildingDefinition> Buildings { get; }

    public BuildingDefinition GetBuilding(string definitionId) =>
        buildingsById.TryGetValue(definitionId, out var definition)
            ? definition
            : throw new KeyNotFoundException($"Unknown building definition '{definitionId}'.");

    public bool TryGetBuilding(string definitionId, out BuildingDefinition definition) =>
        buildingsById.TryGetValue(definitionId, out definition!);

    public static async Task<GameCatalog> LoadAsync(
        TradebornDbContext db,
        CancellationToken cancellationToken = default)
    {
        var resourceRows = await db.ResourceDefinitions.AsNoTracking().ToListAsync(cancellationToken);
        var recipeRows = await db.Recipes.AsNoTracking().Include(r => r.Ingredients).ToListAsync(cancellationToken);
        var buildingRows = await db.BuildingDefinitions.AsNoTracking().ToListAsync(cancellationToken);

        var resources = resourceRows
            .Select(r => new ResourceDefinition(ResourceId.From(r.Id), r.Tier, r.BasePriceCoins, r.MarketDepth))
            .ToArray();

        var recipes = recipeRows.ToDictionary(
            r => r.Id,
            r => new Recipe(
                r.Id,
                r.CycleMilliseconds,
                r.Ingredients.Where(i => !i.IsOutput)
                    .OrderBy(i => i.ResourceId, StringComparer.Ordinal)
                    .Select(i => ResourceAmount.Of(i.ResourceId, i.Quantity)).ToArray(),
                r.Ingredients.Where(i => i.IsOutput)
                    .OrderBy(i => i.ResourceId, StringComparer.Ordinal)
                    .Select(i => ResourceAmount.Of(i.ResourceId, i.Quantity)).ToArray(),
                r.TopologicalRank),
            StringComparer.Ordinal);

        var buildings = buildingRows
            .Select(b => new BuildingDefinition(
                b.Id,
                b.RecipeId is null ? null : recipes[b.RecipeId],
                b.StoragePerResource))
            .ToArray();

        return new GameCatalog(resources, buildings);
    }
}
