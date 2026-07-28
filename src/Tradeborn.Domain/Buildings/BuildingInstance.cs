using Tradeborn.Domain.Common;
using Tradeborn.Domain.Economy;
using Tradeborn.Domain.Production;

namespace Tradeborn.Domain.Buildings;

public enum BuildingState
{
    UnderConstruction,
    Idle,
    Producing,
    Halted,
}

/// <summary>Why a producing building made no progress. Surfaced to the player as a warning mote.</summary>
public enum HaltReason
{
    None,
    NoInput,
    NoCapacity,
}

/// <summary>The coins and materials a build or upgrade costs.</summary>
public sealed record BuildCost(Money Coins, IReadOnlyList<ResourceAmount> Resources)
{
    public static BuildCost Free { get; } = new(Money.Zero, []);
}

/// <summary>A building type, loaded from seed data.</summary>
public sealed class BuildingDefinition
{
    public BuildingDefinition(
        string id,
        Recipe? recipe,
        long storagePerResource = 0,
        Money? buildCostCoins = null,
        IReadOnlyList<ResourceAmount>? buildCostResources = null,
        long buildMilliseconds = 0,
        int unlockCityLevel = 1,
        bool prePlaced = false,
        bool isCityCentre = false)
    {
        Id = id;
        Recipe = recipe;
        StoragePerResource = storagePerResource;
        BaseBuildCost = new BuildCost(buildCostCoins ?? Money.Zero, buildCostResources ?? []);
        BaseBuildMilliseconds = buildMilliseconds;
        UnlockCityLevel = unlockCityLevel;
        PrePlaced = prePlaced;
        IsCityCentre = isCityCentre;
    }

    public string Id { get; }

    /// <summary>Null for buildings that do not produce (Town Hall, Market, Warehouse).</summary>
    public Recipe? Recipe { get; }

    /// <summary>Storage contributed per resource at level 1. Zero for non-storage buildings.</summary>
    public long StoragePerResource { get; }

    public BuildCost BaseBuildCost { get; }
    public long BaseBuildMilliseconds { get; }
    public int UnlockCityLevel { get; }

    /// <summary>Pre-placed buildings exist from city creation and are never built by the player.</summary>
    public bool PrePlaced { get; }

    /// <summary>
    /// The Town Hall. Its level caps the city level, so breadth alone cannot unlock high tiers.
    /// </summary>
    public bool IsCityCentre { get; }

    public long StorageAtLevel(int level) =>
        StoragePerResource == 0
            ? 0
            : (long)(StoragePerResource * UpgradeCurve.PowForLevel(UpgradeCurve.OutputFactor, level));

    /// <summary>
    /// What it costs to reach <paramref name="targetLevel"/>. Level 1 is the initial build.
    /// </summary>
    /// <remarks>
    /// Cost grows by 2.5x per level while output grows by only 1.6x
    /// (docs/economy/ECONOMY_DESIGN.md §5). That gap is deliberate: it stops upgrading from
    /// being automatically correct, so it has to compete with building a second structure.
    /// Without it, plots would be worthless and the city would never grow visually.
    /// </remarks>
    public BuildCost CostAtLevel(int targetLevel)
    {
        var factor = UpgradeCurve.PowForLevel(UpgradeCurve.CostFactor, targetLevel);

        // An explicit loop rather than LINQ: a lambda would capture `factor` into a
        // compiler-generated field, which the architecture tests correctly reject as
        // numeric state in the economy domain.
        var resources = new ResourceAmount[BaseBuildCost.Resources.Count];
        for (var i = 0; i < resources.Length; i++)
        {
            var resource = BaseBuildCost.Resources[i];
            resources[i] = resource with { Quantity = (long)(resource.Quantity * factor) };
        }

        return new BuildCost(Money.FromCent((long)(BaseBuildCost.Coins.Cent * factor)), resources);
    }

    public long DurationMillisecondsAtLevel(int targetLevel) =>
        (long)(BaseBuildMilliseconds * UpgradeCurve.PowForLevel(UpgradeCurve.TimeFactor, targetLevel));
}

/// <summary>A placed building in a player's city.</summary>
public sealed class BuildingInstance
{
    public BuildingInstance(string id, BuildingDefinition definition, int col, int row, int level = 1)
    {
        Id = id;
        Definition = definition;
        Col = col;
        Row = row;
        Level = level;
        PendingLevel = level;
        State = definition.Recipe is null ? BuildingState.Idle : BuildingState.Producing;
    }

    /// <summary>
    /// Starts a brand-new building on a plot. It exists immediately but is
    /// <see cref="BuildingState.UnderConstruction"/> until <see cref="CompletesAtUtc"/>.
    /// </summary>
    /// <remarks>
    /// The row is written at command time rather than on completion, which is what makes the
    /// plot genuinely reserved: a second concurrent build on the same plot collides with the
    /// unique index instead of racing (SECURITY_MODEL.md T4).
    /// </remarks>
    public static BuildingInstance PlaceNew(
        string id,
        BuildingDefinition definition,
        int col,
        int row,
        DateTimeOffset now) =>
        new(id, definition, col, row, level: 1)
        {
            State = BuildingState.UnderConstruction,
            PendingLevel = 1,
            CompletesAtUtc = now.AddMilliseconds(definition.DurationMillisecondsAtLevel(1)),
            ProgressMilliseconds = 0,
        };

    /// <summary>Rebuilds a building from stored state.</summary>
    /// <remarks>
    /// An explicit factory rather than <c>InternalsVisibleTo</c> or public setters. Loading
    /// from the database is a genuinely different operation from placing a new building —
    /// it must restore progress and halt state verbatim, and it must not be reachable from
    /// gameplay code. Naming it makes that distinction visible at the call site.
    /// </remarks>
    public static BuildingInstance Rehydrate(
        string id,
        BuildingDefinition definition,
        int col,
        int row,
        int level,
        BuildingState state,
        HaltReason haltReason,
        long progressMilliseconds,
        DateTimeOffset? completesAtUtc = null,
        int? pendingLevel = null) =>
        new(id, definition, col, row, level)
        {
            State = state,
            HaltReason = haltReason,
            ProgressMilliseconds = progressMilliseconds,
            CompletesAtUtc = completesAtUtc,
            PendingLevel = pendingLevel ?? level,
        };

    public string Id { get; }
    public BuildingDefinition Definition { get; }
    public int Col { get; }
    public int Row { get; }
    public int Level { get; private set; }

    public BuildingState State { get; internal set; }
    public HaltReason HaltReason { get; internal set; } = HaltReason.None;

    /// <summary>
    /// Milliseconds accumulated toward the next production cycle.
    /// </summary>
    /// <remarks>
    /// Preserved across settlements so a building 29 s into a 30 s cycle keeps those 29 s.
    /// Discarding it would silently destroy production every time the player refreshed.
    /// </remarks>
    public long ProgressMilliseconds { get; internal set; }

    /// <summary>When the in-flight build or upgrade finishes. Null when nothing is in flight.</summary>
    public DateTimeOffset? CompletesAtUtc { get; private set; }

    /// <summary>
    /// The level this building will be once the in-flight work finishes.
    /// </summary>
    /// <remarks>
    /// Equal to <see cref="Level"/> when idle. During an upgrade it is <c>Level + 1</c>, so
    /// the building keeps its *current* level — and therefore its current output and storage
    /// contribution — until the upgrade actually lands. Awarding the new level early would
    /// let a player buy output they have not waited for.
    /// </remarks>
    public int PendingLevel { get; private set; }

    public bool IsUnderConstruction => State == BuildingState.UnderConstruction;

    public bool IsUpgrading => IsUnderConstruction && PendingLevel > Level;

    public long CycleMilliseconds => Definition.Recipe?.CycleMillisecondsAtLevel(Level) ?? 0;

    /// <summary>Storage contributed. Zero while under construction — a half-built shed holds nothing.</summary>
    public long StorageContribution =>
        IsUnderConstruction && PendingLevel == Level && Level == 1
            ? 0
            : Definition.StorageAtLevel(Level);

    public bool CanUpgrade => !IsUnderConstruction && Level < UpgradeCurve.MaxLevel;

    /// <summary>Cost of the next upgrade, or null if this building cannot be upgraded right now.</summary>
    public BuildCost? NextUpgradeCost => CanUpgrade ? Definition.CostAtLevel(Level + 1) : null;

    public void BeginUpgrade(DateTimeOffset now)
    {
        if (!CanUpgrade)
        {
            throw new InvalidOperationException(
                $"Building '{Id}' cannot be upgraded (level {Level}, state {State}).");
        }

        PendingLevel = Level + 1;
        State = BuildingState.UnderConstruction;
        HaltReason = HaltReason.None;
        CompletesAtUtc = now.AddMilliseconds(Definition.DurationMillisecondsAtLevel(PendingLevel));

        // Production stops during an upgrade, and banked cycle progress is discarded rather
        // than carried into a cycle of a different length.
        ProgressMilliseconds = 0;
    }

    /// <summary>
    /// Completes an in-flight build or upgrade if its deadline has passed.
    /// </summary>
    /// <remarks>
    /// Driven by settlement rather than by a scheduled job. Completion is a purely
    /// time-driven state transition, which is exactly what Deterministic Lazy Settlement
    /// already handles — so a construction finishes correctly whether or not any background
    /// worker is running (ADR-008).
    /// </remarks>
    public bool TryComplete(DateTimeOffset now)
    {
        if (!IsUnderConstruction || CompletesAtUtc is null || CompletesAtUtc > now)
        {
            return false;
        }

        var wasUpgrade = PendingLevel > Level;

        Level = PendingLevel;
        CompletesAtUtc = null;
        HaltReason = HaltReason.None;

        // A finished *upgrade* resumes on its own — the player already had it running and
        // stopping it would be a chore, not a decision. A brand-new building waits to be
        // started, which is slice step 7 and the beat PLAYER_JOURNEY.md builds the tutorial
        // around. Storage buildings have no recipe and are simply Idle either way.
        State = Definition.Recipe is not null && wasUpgrade
            ? BuildingState.Producing
            : BuildingState.Idle;

        return true;
    }

    /// <summary>Whether the player can currently switch this building on.</summary>
    public bool CanStartProduction =>
        Definition.Recipe is not null && !IsUnderConstruction && State == BuildingState.Idle;

    /// <summary>Whether the player can currently switch this building off.</summary>
    public bool CanStopProduction =>
        Definition.Recipe is not null &&
        !IsUnderConstruction &&
        State is BuildingState.Producing or BuildingState.Halted;

    public void StartProduction()
    {
        if (!CanStartProduction)
        {
            throw new InvalidOperationException($"Building '{Id}' cannot start producing (state {State}).");
        }

        State = BuildingState.Producing;
        HaltReason = HaltReason.None;
    }

    /// <summary>
    /// Pauses production.
    /// </summary>
    /// <remarks>
    /// A real economic lever, not just a convenience: stopping the sawmill banks wood for a
    /// bakery instead of turning it into planks. That is the surplus decision from
    /// ECONOMY_DESIGN.md §3 expressed as a control the player can actually pull.
    ///
    /// Banked cycle progress is kept, so pausing briefly costs nothing — pausing must never
    /// feel like a punishment.
    /// </remarks>
    public void StopProduction()
    {
        if (!CanStopProduction)
        {
            throw new InvalidOperationException($"Building '{Id}' is not producing (state {State}).");
        }

        State = BuildingState.Idle;
        HaltReason = HaltReason.None;
    }

    /// <summary>Build progress in 0..1, for the staged construction visuals.</summary>
    public double ConstructionProgress(DateTimeOffset now)
    {
        if (!IsUnderConstruction || CompletesAtUtc is null)
        {
            return 1;
        }

        var total = Definition.DurationMillisecondsAtLevel(PendingLevel);
        if (total <= 0)
        {
            return 1;
        }

        var remaining = (CompletesAtUtc.Value - now).TotalMilliseconds;
        return Math.Clamp(1 - (remaining / total), 0, 1);
    }
}
