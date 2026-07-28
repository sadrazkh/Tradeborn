using Tradeborn.Domain.Buildings;
using Tradeborn.Domain.Common;

namespace Tradeborn.Domain.Cities;

/// <summary>
/// A player's city — the aggregate root and the transactional boundary for every economic
/// command (docs/architecture/ARCHITECTURE.md §5).
/// </summary>
public sealed class City
{
    private readonly List<BuildingInstance> buildings = [];

    public City(string id, DateTimeOffset createdAt)
    {
        Id = id;
        LastSettledAt = createdAt;
        Inventory = new Inventory();
    }

    public string Id { get; }

    public Money Balance { get; private set; } = Money.Zero;

    public Inventory Inventory { get; }

    public IReadOnlyList<BuildingInstance> Buildings => buildings;

    /// <summary>
    /// The instant this city's state is accurate as of. Everything in Tradeborn is derived
    /// from this plus the server clock — see docs/architecture/REALTIME_AND_TIME_MODEL.md.
    /// </summary>
    public DateTimeOffset LastSettledAt { get; internal set; }

    public void Add(BuildingInstance building)
    {
        if (buildings.Any(b => b.Col == building.Col && b.Row == building.Row))
        {
            throw new InvalidOperationException($"Plot ({building.Col},{building.Row}) is occupied.");
        }

        buildings.Add(building);
        RecomputeCapacity();
    }

    public void Credit(Money amount) => Balance += amount;

    public void Debit(Money amount) => Balance = Balance.Debit(amount);

    /// <summary>
    /// Capacity is derived from buildings, never stored independently, so it can never drift
    /// out of sync with the buildings that grant it.
    /// </summary>
    public void RecomputeCapacity() =>
        Inventory.CapacityPerResource = buildings.Sum(b => b.StorageContribution);
}
