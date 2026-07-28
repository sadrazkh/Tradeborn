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

/// <summary>A building type, loaded from seed data.</summary>
public sealed class BuildingDefinition
{
    public BuildingDefinition(string id, Recipe? recipe, long storagePerResource = 0)
    {
        Id = id;
        Recipe = recipe;
        StoragePerResource = storagePerResource;
    }

    public string Id { get; }

    /// <summary>Null for buildings that do not produce (Town Hall, Market, Warehouse).</summary>
    public Recipe? Recipe { get; }

    /// <summary>Storage contributed per resource at level 1. Zero for non-storage buildings.</summary>
    public long StoragePerResource { get; }

    public long StorageAtLevel(int level) =>
        StoragePerResource == 0
            ? 0
            : (long)(StoragePerResource * Math.Pow(UpgradeCurve.OutputFactor, level - 1));
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
        State = definition.Recipe is null ? BuildingState.Idle : BuildingState.Producing;
    }

    /// <summary>
    /// Rebuilds a building from stored state.
    /// </summary>
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
        long progressMilliseconds) =>
        new(id, definition, col, row, level)
        {
            State = state,
            HaltReason = haltReason,
            ProgressMilliseconds = progressMilliseconds,
        };

    public string Id { get; }
    public BuildingDefinition Definition { get; }
    public int Col { get; }
    public int Row { get; }
    public int Level { get; private set; }

    public BuildingState State { get; internal set; }
    public HaltReason HaltReason { get; internal set; } = HaltReason.None;

    /// <summary>
    /// Milliseconds accumulated toward the next cycle.
    /// </summary>
    /// <remarks>
    /// Preserved across settlements so a building 29 s into a 30 s cycle keeps those 29 s.
    /// Discarding it would silently destroy production every time the player refreshed.
    /// </remarks>
    public long ProgressMilliseconds { get; internal set; }

    public long CycleMilliseconds =>
        Definition.Recipe?.CycleMillisecondsAtLevel(Level) ?? 0;

    public long StorageContribution => Definition.StorageAtLevel(Level);

    public void Upgrade()
    {
        if (Level >= UpgradeCurve.MaxLevel)
        {
            throw new InvalidOperationException($"Building '{Id}' is already at max level.");
        }

        Level++;

        // Progress is measured in milliseconds of a cycle whose length just changed.
        // Clamping keeps it meaningful instead of letting an upgrade hand out a free cycle.
        ProgressMilliseconds = Math.Min(ProgressMilliseconds, Math.Max(0, CycleMilliseconds - 1));
    }
}
