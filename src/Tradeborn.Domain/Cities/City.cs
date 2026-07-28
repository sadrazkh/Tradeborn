using Tradeborn.Domain.Buildings;
using Tradeborn.Domain.Common;
using Tradeborn.Domain.Logistics;
using Tradeborn.Domain.Production;

namespace Tradeborn.Domain.Cities;

/// <summary>A single build plot and whether the player has unlocked it yet.</summary>
public sealed record CityPlot(int Col, int Row, string Terrain, bool Unlocked);

/// <summary>
/// A player's city — the aggregate root and the transactional boundary for every economic
/// command (docs/architecture/ARCHITECTURE.md §5).
/// </summary>
public sealed class City
{
    private readonly List<BuildingInstance> buildings = [];
    private readonly Dictionary<(int Col, int Row), CityPlot> plots = [];
    private readonly List<TransportJob> transports = [];

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

    public IReadOnlyCollection<CityPlot> Plots => plots.Values;

    /// <summary>
    /// The instant this city's state is accurate as of. Everything in Tradeborn is derived
    /// from this plus the server clock — see docs/architecture/REALTIME_AND_TIME_MODEL.md.
    /// </summary>
    public DateTimeOffset LastSettledAt { get; internal set; }

    // ---- Plots -----------------------------------------------------------------------------

    public void SetPlots(IEnumerable<CityPlot> layout)
    {
        plots.Clear();
        foreach (var plot in layout)
        {
            plots[(plot.Col, plot.Row)] = plot;
        }
    }

    public CityPlot? PlotAt(int col, int row) =>
        plots.TryGetValue((col, row), out var plot) ? plot : null;

    public bool IsOccupied(int col, int row) =>
        buildings.Any(b => b.Col == col && b.Row == row);

    // ---- Buildings -------------------------------------------------------------------------

    public void Add(BuildingInstance building)
    {
        if (IsOccupied(building.Col, building.Row))
        {
            throw new InvalidOperationException($"Plot ({building.Col},{building.Row}) is occupied.");
        }

        buildings.Add(building);
        RecomputeCapacity();
    }

    public BuildingInstance? BuildingById(string id) =>
        buildings.FirstOrDefault(b => string.Equals(b.Id, id, StringComparison.Ordinal));

    // ---- Logistics -------------------------------------------------------------------------

    /// <summary>Loads currently on the road.</summary>
    public IReadOnlyList<TransportJob> Transports => transports;

    /// <summary>
    /// How many cart-loads have ever been delivered, and how many sales made.
    /// </summary>
    /// <remarks>
    /// Two tutorial quests ask "has a delivery happened?" and "has a sale happened?". Both are
    /// one-way facts about the past that cannot be derived from current state — a player who
    /// sells everything they were delivered looks, by inventory alone, exactly like a player
    /// who never received anything. Counters are the honest way to answer.
    /// </remarks>
    public long DeliveriesCompleted { get; private set; }

    public long SalesCompleted { get; private set; }

    internal void RecordDelivery() => DeliveriesCompleted++;

    public void RecordSale() => SalesCompleted++;

    /// <summary>Restores lifetime counters loaded from the database.</summary>
    public void RestoreCounters(long deliveries, long sales)
    {
        DeliveriesCompleted = deliveries;
        SalesCompleted = sales;
    }

    public bool HasTransportFrom(string buildingId) =>
        transports.Any(t => string.Equals(t.FromBuildingId, buildingId, StringComparison.Ordinal));

    internal void AddTransport(TransportJob job) => transports.Add(job);

    internal void RemoveTransport(TransportJob job) => transports.Remove(job);

    /// <summary>Restores in-flight jobs loaded from the database.</summary>
    public void RestoreTransports(IEnumerable<TransportJob> jobs)
    {
        transports.Clear();
        transports.AddRange(jobs);
    }

    /// <summary>
    /// Where deliveries go, in plot coordinates.
    /// </summary>
    /// <remarks>
    /// The city centre. Warehouses raise capacity rather than acting as separate destinations,
    /// so there is exactly one delivery point and travel time is a pure function of where the
    /// producer stands — which keeps journey durations deterministic.
    /// </remarks>
    public (int Col, int Row) DeliveryPoint
    {
        get
        {
            var centre = buildings.FirstOrDefault(b => b.Definition.IsCityCentre);
            return centre is null ? (0, 0) : (centre.Col, centre.Row);
        }
    }

    /// <summary>Builds and upgrades currently in flight. Bounded by <see cref="ConstructionSlots"/>.</summary>
    public int ActiveConstructions => buildings.Count(b => b.IsUnderConstruction);

    /// <summary>
    /// How many builds may run at once.
    /// </summary>
    /// <remarks>
    /// One slot in the vertical slice, a second from city level 3. The queue is a pacing
    /// device, not a monetisation hook: extra slots are earned by progression and are never
    /// sold (docs/vision/GAME_VISION.md §8).
    /// </remarks>
    public int ConstructionSlots => Level >= 3 ? 2 : 1;

    /// <summary>
    /// City level, from docs/economy/ECONOMY_DESIGN.md §9.
    /// </summary>
    /// <remarks>
    /// The city-centre cap stops a player unlocking high tiers by spamming cheap buildings:
    /// breadth alone does not advance the city, the centre has to keep up.
    /// </remarks>
    public int Level
    {
        get
        {
            var totalLevels = buildings.Where(b => !b.IsUnderConstruction).Sum(b => b.Level);
            var centre = buildings.FirstOrDefault(b => b.Definition.IsCityCentre);
            var cap = (centre?.Level ?? 1) * 2;
            return Math.Clamp(totalLevels / 4, 1, cap);
        }
    }

    // ---- Money and materials ---------------------------------------------------------------

    public void Credit(Money amount) => Balance += amount;

    public void Debit(Money amount) => Balance = Balance.Debit(amount);

    public bool CanAfford(BuildCost cost) =>
        Balance.CanAfford(cost.Coins) &&
        cost.Resources.All(r => Inventory.Get(r.Resource) >= r.Quantity);

    /// <summary>
    /// Deducts a cost in full, or throws without changing anything.
    /// </summary>
    /// <remarks>
    /// Affordability is checked for <b>every</b> component before anything is deducted. A
    /// partial deduction — coins taken, materials short — would leave the player poorer with
    /// nothing to show for it, which is the worst possible failure mode in an economy game.
    /// </remarks>
    public void Spend(BuildCost cost)
    {
        if (!CanAfford(cost))
        {
            throw new InvalidOperationException("Cannot afford this cost.");
        }

        Balance = Balance.Debit(cost.Coins);
        foreach (var resource in cost.Resources)
        {
            Inventory.Remove(resource.Resource, resource.Quantity);
        }
    }

    /// <summary>
    /// Capacity is derived from buildings, never stored independently, so it can never drift
    /// out of sync with the buildings that grant it.
    /// </summary>
    public void RecomputeCapacity() =>
        Inventory.CapacityPerResource = buildings.Sum(b => b.StorageContribution);
}
