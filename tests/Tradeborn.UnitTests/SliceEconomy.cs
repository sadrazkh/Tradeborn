using Tradeborn.Domain.Buildings;
using Tradeborn.Domain.Common;
using Tradeborn.Domain.Cities;
using Tradeborn.Domain.Economy;
using Tradeborn.Domain.Production;

namespace Tradeborn.UnitTests;

/// <summary>
/// The vertical-slice economy from docs/economy/RESOURCE_GRAPH.md §4, built in code so the
/// domain can be tested without a database.
/// </summary>
/// <remarks>
/// This mirrors the seed data that Phase 1 will load from PostgreSQL. When the real seed
/// lands, this class is replaced by loading it, and these tests keep passing unchanged —
/// which is the point of keeping the numbers in one documented place.
/// </remarks>
internal static class SliceEconomy
{
    public static readonly DateTimeOffset Epoch =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static Recipe ExtractWood { get; } = new(
        "extract_wood", 30_000, [], [ResourceAmount.Of("wood", 1)], topologicalRank: 0);

    public static Recipe ExtractGrain { get; } = new(
        "extract_grain", 30_000, [], [ResourceAmount.Of("grain", 1)], topologicalRank: 0);

    public static Recipe SawPlanks { get; } = new(
        "saw_planks", 60_000,
        [ResourceAmount.Of("wood", 2)], [ResourceAmount.Of("planks", 1)], topologicalRank: 1);

    public static Recipe MillFlour { get; } = new(
        "mill_flour", 60_000,
        [ResourceAmount.Of("grain", 2)], [ResourceAmount.Of("flour", 1)], topologicalRank: 1);

    public static Recipe BakeBread { get; } = new(
        "bake_bread", 120_000,
        [ResourceAmount.Of("flour", 2), ResourceAmount.Of("planks", 1)],
        [ResourceAmount.Of("bread", 1)], topologicalRank: 2);

    // Costs and durations mirror docs/economy/ECONOMY_DESIGN.md §4, matching what the
    // seeder writes to the database.
    public static BuildingDefinition LumberCamp { get; } = new(
        "lumber_camp", ExtractWood,
        buildCostCoins: Money.FromCoins(150),
        buildCostResources: [ResourceAmount.Of("wood", 20)],
        buildMilliseconds: 30_000);

    public static BuildingDefinition Farm { get; } = new(
        "farm", ExtractGrain,
        buildCostCoins: Money.FromCoins(150),
        buildCostResources: [ResourceAmount.Of("wood", 20)],
        buildMilliseconds: 30_000);

    public static BuildingDefinition Sawmill { get; } = new(
        "sawmill", SawPlanks,
        buildCostCoins: Money.FromCoins(400),
        buildCostResources: [ResourceAmount.Of("wood", 60)],
        buildMilliseconds: 120_000,
        unlockCityLevel: 2);

    public static BuildingDefinition Mill { get; } = new(
        "mill", MillFlour,
        buildCostCoins: Money.FromCoins(400),
        buildCostResources: [ResourceAmount.Of("wood", 60)],
        buildMilliseconds: 120_000,
        unlockCityLevel: 2);

    public static BuildingDefinition Bakery { get; } = new(
        "bakery", BakeBread,
        buildCostCoins: Money.FromCoins(900),
        buildCostResources: [ResourceAmount.Of("wood", 40), ResourceAmount.Of("planks", 30)],
        buildMilliseconds: 300_000,
        unlockCityLevel: 3);

    public static BuildingDefinition TownHall { get; } = new(
        "town_hall", recipe: null, storagePerResource: 100, prePlaced: true, isCityCentre: true);

    public static BuildingDefinition Market { get; } = new(
        "market", recipe: null, prePlaced: true);

    public static BuildingDefinition Warehouse { get; } = new(
        "warehouse", recipe: null, storagePerResource: 200,
        buildCostCoins: Money.FromCoins(250),
        buildCostResources: [ResourceAmount.Of("wood", 40)],
        buildMilliseconds: 60_000);

    /// <summary>Effectively unlimited storage, so capacity never accidentally binds in a test.</summary>
    private const long UnlimitedCapacity = 1_000_000;

    /// <summary>
    /// Builds a city with storage that will not bind.
    /// </summary>
    /// <remarks>
    /// Capacity is deliberately granted through a storage <i>building</i> rather than by
    /// assigning <c>Inventory.CapacityPerResource</c>. Capacity is derived state — City
    /// recomputes it from its buildings — so letting a test set it directly would let the
    /// tests drift from a state the real game can actually reach.
    /// </remarks>
    public static City WithBuildings(params (BuildingDefinition Definition, int Level)[] definitions) =>
        WithCapacity(UnlimitedCapacity, definitions);

    /// <summary>Builds a city whose per-resource storage is exactly <paramref name="capacityPerResource"/>.</summary>
    public static City WithCapacity(
        long capacityPerResource,
        params (BuildingDefinition Definition, int Level)[] definitions)
    {
        var city = new City("city-1", Epoch);
        city.SetPlots(DefaultPlots());

        var store = new BuildingDefinition("test_store", recipe: null, storagePerResource: capacityPerResource);
        city.Add(new BuildingInstance("store", store, 0, 0, level: 1));

        var index = 0;
        foreach (var (definition, level) in definitions)
        {
            index++;
            city.Add(new BuildingInstance($"b{index}-{definition.Id}", definition, index, 1, level));
        }

        return city;
    }

    /// <summary>An 8x8 grid with the middle 4x4 unlocked, mirroring a freshly provisioned city.</summary>
    public static IEnumerable<CityPlot> DefaultPlots()
    {
        for (var row = 0; row < 8; row++)
        {
            for (var col = 0; col < 8; col++)
            {
                var unlocked = col is >= 1 and <= 4 && row is >= 2 and <= 5;
                yield return new CityPlot(col, row, "grass", unlocked);
            }
        }
    }

    /// <summary>
    /// A city as a new player finds it: Town Hall and Market pre-placed, 800 coins, 80 wood.
    /// </summary>
    /// <remarks>
    /// Matches the starting state in ECONOMY_DESIGN.md §10, so tests that assert what a new
    /// player can afford stay honest about the real opening position.
    /// </remarks>
    public static City NewPlayerCity()
    {
        var city = new City("city-new", Epoch);
        city.SetPlots(DefaultPlots());
        city.Add(new BuildingInstance("town-hall", TownHall, 2, 3));
        city.Add(new BuildingInstance("market", Market, 4, 5));
        city.Credit(Money.FromCoins(800));
        city.Inventory.Add(ResourceId.From("wood"), 80);
        return city;
    }
}
