using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tradeborn.Infrastructure.Persistence;

namespace Tradeborn.Infrastructure.Seed;

/// <summary>
/// Seeds the game catalog from docs/economy/RESOURCE_GRAPH.md §4.
/// </summary>
/// <remarks>
/// <para>
/// <b>Idempotent by construction.</b> Every row is upserted by primary key and no row is
/// keyed on anything generated, so running the seeder twice produces byte-identical state.
/// An integration test asserts exactly that (TEST_STRATEGY.md §3) — "probably idempotent"
/// is how seeds quietly duplicate production data.
/// </para>
/// <para>
/// These numbers exist in exactly two places: the design document and here. They must never
/// be duplicated into domain or UI code (ECONOMY_DESIGN.md §1).
/// </para>
/// </remarks>
public sealed class GameCatalogSeeder(TradebornDbContext db, ILogger<GameCatalogSeeder> logger)
{
    private static readonly (string Id, string Tier, long Price, long Depth)[] Resources =
    [
        ("wood", "raw", 2, 500),
        ("grain", "raw", 2, 500),
        ("planks", "processed", 10, 300),
        ("flour", "processed", 10, 300),
        ("bread", "finished", 60, 150),
    ];

    private static readonly (string Id, long CycleMs, (string Resource, long Qty)[] In, (string Resource, long Qty)[] Out)[] Recipes =
    [
        ("extract_wood", 30_000, [], [("wood", 1)]),
        ("extract_grain", 30_000, [], [("grain", 1)]),
        ("saw_planks", 60_000, [("wood", 2)], [("planks", 1)]),
        ("mill_flour", 60_000, [("grain", 2)], [("flour", 1)]),
        ("bake_bread", 120_000, [("flour", 2), ("planks", 1)], [("bread", 1)]),
    ];

    private static readonly (
        string Id,
        string? RecipeId,
        long Storage,
        long CostCoins,
        (string Resource, long Qty)[] CostMaterials,
        long BuildSeconds,
        int Unlock,
        bool PrePlaced,
        bool IsCityCentre)[] Buildings =
    [
        ("town_hall",   null,            100, 0,   [],                            0,   1, true,  true),
        ("market",      null,            0,   0,   [],                            0,   1, true,  false),
        ("lumber_camp", "extract_wood",  0,   150, [("wood", 20)],                30,  1, false, false),
        ("farm",        "extract_grain", 0,   150, [("wood", 20)],                30,  1, false, false),
        ("warehouse",   null,            200, 250, [("wood", 40)],                60,  1, false, false),
        ("sawmill",     "saw_planks",    0,   400, [("wood", 60)],                120, 2, false, false),
        ("mill",        "mill_flour",    0,   400, [("wood", 60)],                120, 2, false, false),
        ("bakery",      "bake_bread",    0,   900, [("wood", 40), ("planks", 30)], 300, 3, false, false),
    ];

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedResourcesAsync(cancellationToken);
        await SeedRecipesAsync(cancellationToken);
        await SeedBuildingsAsync(cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Game catalog seeded: {Resources} resources, {Recipes} recipes, {Buildings} buildings.",
            Resources.Length, Recipes.Length, Buildings.Length);
    }

    private async Task SeedResourcesAsync(CancellationToken cancellationToken)
    {
        var existing = await db.ResourceDefinitions.ToDictionaryAsync(r => r.Id, cancellationToken);

        foreach (var (id, tier, price, depth) in Resources)
        {
            if (!existing.TryGetValue(id, out var entity))
            {
                entity = new ResourceDefinitionEntity { Id = id };
                db.ResourceDefinitions.Add(entity);
            }

            entity.Tier = tier;
            entity.BasePriceCoins = price;
            entity.MarketDepth = depth;
        }
    }

    private async Task SeedRecipesAsync(CancellationToken cancellationToken)
    {
        var ranks = ComputeTopologicalRanks();
        var existing = await db.Recipes.Include(r => r.Ingredients).ToDictionaryAsync(r => r.Id, cancellationToken);

        foreach (var (id, cycleMs, inputs, outputs) in Recipes)
        {
            if (!existing.TryGetValue(id, out var entity))
            {
                entity = new RecipeEntity { Id = id };
                db.Recipes.Add(entity);
            }

            entity.CycleMilliseconds = cycleMs;
            entity.TopologicalRank = ranks[id];

            UpsertIngredients(entity, inputs, isOutput: false);
            UpsertIngredients(entity, outputs, isOutput: true);
        }
    }

    private static void UpsertIngredients(
        RecipeEntity recipe,
        (string Resource, long Qty)[] ingredients,
        bool isOutput)
    {
        foreach (var (resource, quantity) in ingredients)
        {
            var existing = recipe.Ingredients
                .FirstOrDefault(i => i.ResourceId == resource && i.IsOutput == isOutput);

            if (existing is null)
            {
                recipe.Ingredients.Add(new RecipeIngredientEntity
                {
                    RecipeId = recipe.Id,
                    ResourceId = resource,
                    Quantity = quantity,
                    IsOutput = isOutput,
                });
            }
            else
            {
                existing.Quantity = quantity;
            }
        }
    }

    private async Task SeedBuildingsAsync(CancellationToken cancellationToken)
    {
        var existing = await db.BuildingDefinitions
            .Include(b => b.Costs)
            .ToDictionaryAsync(b => b.Id, cancellationToken);

        foreach (var (id, recipeId, storage, cost, materials, seconds, unlock, prePlaced, isCentre) in Buildings)
        {
            if (!existing.TryGetValue(id, out var entity))
            {
                entity = new BuildingDefinitionEntity { Id = id };
                db.BuildingDefinitions.Add(entity);
            }

            entity.RecipeId = recipeId;
            entity.StoragePerResource = storage;
            entity.BuildCostCoins = cost;
            entity.BuildSeconds = seconds;
            entity.UnlockCityLevel = unlock;
            entity.PrePlaced = prePlaced;
            entity.IsCityCentre = isCentre;

            UpsertCosts(entity, materials);
        }
    }

    /// <summary>
    /// Upserts material costs by resource, and removes any that the design no longer lists.
    /// </summary>
    /// <remarks>
    /// Removal matters for idempotency: without it, deleting a material from the design above
    /// would leave the old row behind and every subsequent build would keep charging for it.
    /// </remarks>
    private static void UpsertCosts(BuildingDefinitionEntity building, (string Resource, long Qty)[] materials)
    {
        foreach (var (resource, quantity) in materials)
        {
            var existing = building.Costs.FirstOrDefault(c => c.ResourceId == resource);
            if (existing is null)
            {
                building.Costs.Add(new BuildingCostEntity
                {
                    BuildingId = building.Id,
                    ResourceId = resource,
                    Quantity = quantity,
                });
            }
            else
            {
                existing.Quantity = quantity;
            }
        }

        var keep = materials.Select(m => m.Resource).ToHashSet(StringComparer.Ordinal);
        building.Costs.RemoveAll(c => !keep.Contains(c.ResourceId));
    }

    /// <summary>
    /// Ranks recipes so producers resolve before consumers during settlement.
    /// </summary>
    /// <remarks>
    /// Extractors are rank 0; every other recipe is one past the highest-ranked recipe that
    /// produces any of its inputs. The loop terminates only because the recipe graph is
    /// acyclic (RESOURCE_GRAPH.md §2) — the iteration guard turns a violation of that
    /// invariant into a loud failure at startup instead of a hang.
    /// </remarks>
    private static Dictionary<string, int> ComputeTopologicalRanks()
    {
        var producedBy = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, _, _, outputs) in Recipes)
        {
            foreach (var (resource, _) in outputs)
            {
                producedBy[resource] = id;
            }
        }

        var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
        var pending = Recipes.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

        for (var guard = 0; pending.Count > 0; guard++)
        {
            if (guard > Recipes.Length)
            {
                throw new InvalidOperationException(
                    "Recipe graph contains a cycle; settlement would not terminate. " +
                    "See docs/economy/RESOURCE_GRAPH.md §2.");
            }

            foreach (var (id, _, inputs, _) in Recipes.Where(r => pending.Contains(r.Id)).ToArray())
            {
                var upstream = inputs
                    .Select(i => producedBy.GetValueOrDefault(i.Resource))
                    .Where(r => r is not null)
                    .ToArray();

                if (upstream.Any(r => !ranks.ContainsKey(r!)))
                {
                    continue; // an input's producer is not ranked yet
                }

                ranks[id] = upstream.Length == 0 ? 0 : upstream.Max(r => ranks[r!]) + 1;
                pending.Remove(id);
            }
        }

        return ranks;
    }
}
