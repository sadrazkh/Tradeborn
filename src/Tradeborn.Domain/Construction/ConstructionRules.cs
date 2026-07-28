using Tradeborn.Domain.Buildings;
using Tradeborn.Domain.Cities;
using Tradeborn.Domain.Production;

namespace Tradeborn.Domain.Construction;

/// <summary>
/// Why a build or upgrade was refused. Maps one-to-one onto a client error code and a
/// specific piece of UI feedback, so the player is never told merely "that didn't work".
/// </summary>
public enum ConstructionRefusal
{
    None = 0,
    UnknownPlot,
    PlotLocked,
    PlotOccupied,
    NotUnlocked,
    CannotBeBuilt,
    InsufficientFunds,
    QueueFull,
    BuildingNotFound,
    AlreadyUnderConstruction,
    MaxLevelReached,
}

public readonly record struct ConstructionCheck(ConstructionRefusal Refusal)
{
    public static ConstructionCheck Allowed => new(ConstructionRefusal.None);
    public static ConstructionCheck Refused(ConstructionRefusal reason) => new(reason);

    public bool IsAllowed => Refusal == ConstructionRefusal.None;
}

/// <summary>
/// The single authority on whether a build or upgrade is legal.
/// </summary>
/// <remarks>
/// <para>
/// Pure functions over already-settled state, in the domain rather than in a handler, so
/// every rule is unit-testable with no I/O and there is exactly one place to look when
/// asking "why was that rejected?".
/// </para>
/// <para>
/// The client runs an equivalent check to grey out invalid plots, but that is a UX
/// affordance only. This is the authority (docs/architecture/SECURITY_MODEL.md §3) — a
/// client that skips its own check gets exactly the same answer from here.
/// </para>
/// </remarks>
public static class ConstructionRules
{
    public static ConstructionCheck CanPlace(City city, BuildingDefinition definition, int col, int row)
    {
        if (definition.PrePlaced)
        {
            // Town Hall and Market come with the city; letting a player build a second one
            // would break the city-centre cap that bounds city level.
            return ConstructionCheck.Refused(ConstructionRefusal.CannotBeBuilt);
        }

        var plot = city.PlotAt(col, row);
        if (plot is null)
        {
            return ConstructionCheck.Refused(ConstructionRefusal.UnknownPlot);
        }

        if (!plot.Unlocked)
        {
            return ConstructionCheck.Refused(ConstructionRefusal.PlotLocked);
        }

        if (city.IsOccupied(col, row))
        {
            return ConstructionCheck.Refused(ConstructionRefusal.PlotOccupied);
        }

        if (city.Level < definition.UnlockCityLevel)
        {
            return ConstructionCheck.Refused(ConstructionRefusal.NotUnlocked);
        }

        if (city.ActiveConstructions >= city.ConstructionSlots)
        {
            return ConstructionCheck.Refused(ConstructionRefusal.QueueFull);
        }

        if (!city.CanAfford(definition.CostAtLevel(1)))
        {
            return ConstructionCheck.Refused(ConstructionRefusal.InsufficientFunds);
        }

        return ConstructionCheck.Allowed;
    }

    public static ConstructionCheck CanUpgrade(City city, string buildingId)
    {
        var building = city.BuildingById(buildingId);
        if (building is null)
        {
            return ConstructionCheck.Refused(ConstructionRefusal.BuildingNotFound);
        }

        if (building.IsUnderConstruction)
        {
            return ConstructionCheck.Refused(ConstructionRefusal.AlreadyUnderConstruction);
        }

        if (building.Level >= UpgradeCurve.MaxLevel)
        {
            return ConstructionCheck.Refused(ConstructionRefusal.MaxLevelReached);
        }

        if (city.ActiveConstructions >= city.ConstructionSlots)
        {
            return ConstructionCheck.Refused(ConstructionRefusal.QueueFull);
        }

        var cost = building.NextUpgradeCost;
        if (cost is null || !city.CanAfford(cost))
        {
            return ConstructionCheck.Refused(ConstructionRefusal.InsufficientFunds);
        }

        return ConstructionCheck.Allowed;
    }
}
