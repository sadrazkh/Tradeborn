using Tradeborn.Domain.Cities;

namespace Tradeborn.Domain.Production;

/// <summary>Why a production toggle was refused.</summary>
public enum ProductionRefusal
{
    None = 0,
    BuildingNotFound,
    NoRecipe,
    UnderConstruction,
    AlreadyInThatState,
}

public readonly record struct ProductionCheck(ProductionRefusal Refusal)
{
    public static ProductionCheck Allowed => new(ProductionRefusal.None);
    public static ProductionCheck Refused(ProductionRefusal reason) => new(reason);

    public bool IsAllowed => Refusal == ProductionRefusal.None;
}

/// <summary>
/// Whether a building may be switched on or off.
/// </summary>
/// <remarks>
/// Pure functions over already-settled state, matching
/// <see cref="Tradeborn.Domain.Construction.ConstructionRules"/>. The client mirrors these to
/// decide whether to show a Start or Pause control, but this is the authority
/// (SECURITY_MODEL.md §3).
/// </remarks>
public static class ProductionRules
{
    public static ProductionCheck CanSetActive(City city, string buildingId, bool active)
    {
        var building = city.BuildingById(buildingId);
        if (building is null)
        {
            return ProductionCheck.Refused(ProductionRefusal.BuildingNotFound);
        }

        if (building.Definition.Recipe is null)
        {
            // Warehouses and the Market hold or trade goods; there is nothing to switch on.
            return ProductionCheck.Refused(ProductionRefusal.NoRecipe);
        }

        if (building.IsUnderConstruction)
        {
            return ProductionCheck.Refused(ProductionRefusal.UnderConstruction);
        }

        if (active && !building.CanStartProduction)
        {
            return ProductionCheck.Refused(ProductionRefusal.AlreadyInThatState);
        }

        if (!active && !building.CanStopProduction)
        {
            return ProductionCheck.Refused(ProductionRefusal.AlreadyInThatState);
        }

        return ProductionCheck.Allowed;
    }
}
