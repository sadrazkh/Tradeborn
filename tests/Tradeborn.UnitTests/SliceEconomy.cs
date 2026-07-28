using Tradeborn.Domain.Buildings;
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

    public static BuildingDefinition LumberCamp { get; } = new("lumber_camp", ExtractWood);
    public static BuildingDefinition Farm { get; } = new("farm", ExtractGrain);
    public static BuildingDefinition Sawmill { get; } = new("sawmill", SawPlanks);
    public static BuildingDefinition Mill { get; } = new("mill", MillFlour);
    public static BuildingDefinition Bakery { get; } = new("bakery", BakeBread);

    public static BuildingDefinition TownHall { get; } = new("town_hall", recipe: null, storagePerResource: 100);
    public static BuildingDefinition Warehouse { get; } = new("warehouse", recipe: null, storagePerResource: 200);

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
}
