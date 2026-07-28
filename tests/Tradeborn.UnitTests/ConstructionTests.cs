using Tradeborn.Domain.Buildings;
using Tradeborn.Domain.Cities;
using Tradeborn.Domain.Common;
using Tradeborn.Domain.Construction;
using Tradeborn.Domain.Economy;
using Tradeborn.Domain.Production;

namespace Tradeborn.UnitTests;

public class ConstructionTests
{
    private static readonly ResourceId Wood = ResourceId.From("wood");

    // -----------------------------------------------------------------------------------
    // Placement rules — every refusal is a distinct, actionable answer
    // -----------------------------------------------------------------------------------

    [Fact]
    public void A_new_player_can_afford_their_first_lumber_camp()
    {
        // Guards the opening position in ECONOMY_DESIGN.md §10: if this ever fails, the
        // tutorial's first step becomes impossible.
        var city = SliceEconomy.NewPlayerCity();

        var check = ConstructionRules.CanPlace(city, SliceEconomy.LumberCamp, 1, 2);

        Assert.True(check.IsAllowed, $"Expected the first build to be allowed, got {check.Refusal}.");
    }

    [Fact]
    public void Building_on_a_locked_plot_is_refused()
    {
        var city = SliceEconomy.NewPlayerCity();

        var check = ConstructionRules.CanPlace(city, SliceEconomy.LumberCamp, 7, 7);

        Assert.Equal(ConstructionRefusal.PlotLocked, check.Refusal);
    }

    [Fact]
    public void Building_on_a_plot_outside_the_grid_is_refused()
    {
        var city = SliceEconomy.NewPlayerCity();

        var check = ConstructionRules.CanPlace(city, SliceEconomy.LumberCamp, 99, 99);

        Assert.Equal(ConstructionRefusal.UnknownPlot, check.Refusal);
    }

    [Fact]
    public void Building_on_an_occupied_plot_is_refused()
    {
        var city = SliceEconomy.NewPlayerCity();
        city.Add(new BuildingInstance("existing", SliceEconomy.LumberCamp, 1, 2));

        var check = ConstructionRules.CanPlace(city, SliceEconomy.LumberCamp, 1, 2);

        Assert.Equal(ConstructionRefusal.PlotOccupied, check.Refusal);
    }

    [Fact]
    public void A_building_above_the_city_level_is_refused()
    {
        // The Sawmill needs city level 2; a new city is level 1.
        var city = SliceEconomy.NewPlayerCity();
        city.Credit(Money.FromCoins(10_000));
        city.Inventory.Add(Wood, 500);

        var check = ConstructionRules.CanPlace(city, SliceEconomy.Sawmill, 1, 2);

        Assert.Equal(ConstructionRefusal.NotUnlocked, check.Refusal);
    }

    [Fact]
    public void Pre_placed_buildings_cannot_be_built_by_the_player()
    {
        // A second Town Hall would break the city-centre cap that bounds city level.
        var city = SliceEconomy.NewPlayerCity();

        var check = ConstructionRules.CanPlace(city, SliceEconomy.TownHall, 1, 2);

        Assert.Equal(ConstructionRefusal.CannotBeBuilt, check.Refusal);
    }

    [Fact]
    public void Insufficient_coins_are_refused()
    {
        var city = SliceEconomy.NewPlayerCity();
        city.Debit(Money.FromCoins(800));

        var check = ConstructionRules.CanPlace(city, SliceEconomy.LumberCamp, 1, 2);

        Assert.Equal(ConstructionRefusal.InsufficientFunds, check.Refusal);
    }

    [Fact]
    public void Insufficient_materials_are_refused_even_with_plenty_of_coins()
    {
        // The Lumber Camp costs 150 coins AND 20 wood. Money alone is not enough.
        var city = SliceEconomy.NewPlayerCity();
        city.Inventory.Remove(Wood, 80);
        city.Credit(Money.FromCoins(100_000));

        var check = ConstructionRules.CanPlace(city, SliceEconomy.LumberCamp, 1, 2);

        Assert.Equal(ConstructionRefusal.InsufficientFunds, check.Refusal);
    }

    [Fact]
    public void A_second_build_is_refused_while_one_is_already_in_flight()
    {
        var city = SliceEconomy.NewPlayerCity();
        city.Add(BuildingInstance.PlaceNew("b1", SliceEconomy.LumberCamp, 1, 2, SliceEconomy.Epoch));

        var check = ConstructionRules.CanPlace(city, SliceEconomy.Farm, 1, 3);

        Assert.Equal(ConstructionRefusal.QueueFull, check.Refusal);
    }

    // -----------------------------------------------------------------------------------
    // Spending is all-or-nothing
    // -----------------------------------------------------------------------------------

    [Fact]
    public void Spending_leaves_nothing_deducted_when_materials_are_short()
    {
        // The worst failure mode in an economy game is taking the coins and then discovering
        // the materials are short, leaving the player poorer with nothing to show for it.
        var city = SliceEconomy.NewPlayerCity();
        city.Inventory.Remove(Wood, 75); // 5 wood left, 20 needed
        var coinsBefore = city.Balance;

        Assert.Throws<InvalidOperationException>(() => city.Spend(SliceEconomy.LumberCamp.CostAtLevel(1)));

        Assert.Equal(coinsBefore, city.Balance);
        Assert.Equal(5, city.Inventory.Get(Wood));
    }

    [Fact]
    public void Spending_deducts_coins_and_materials_together()
    {
        var city = SliceEconomy.NewPlayerCity();

        city.Spend(SliceEconomy.LumberCamp.CostAtLevel(1));

        Assert.Equal(650, city.Balance.Coins);
        Assert.Equal(60, city.Inventory.Get(Wood));
    }

    // -----------------------------------------------------------------------------------
    // Upgrade costs follow the published curve
    // -----------------------------------------------------------------------------------

    [Theory]
    [InlineData(1, 150, 20)]  // base
    [InlineData(2, 375, 50)]  // x2.5
    [InlineData(3, 937, 125)] // x6.25
    public void Upgrade_costs_follow_the_documented_curve(int level, long coins, long wood)
    {
        var cost = SliceEconomy.LumberCamp.CostAtLevel(level);

        Assert.Equal(coins, cost.Coins.Coins);
        Assert.Equal(wood, cost.Resources.Single(r => r.Resource == Wood).Quantity);
    }

    [Fact]
    public void A_building_at_max_level_cannot_be_upgraded()
    {
        var city = SliceEconomy.NewPlayerCity();
        city.Credit(Money.FromCoins(100_000));
        city.Inventory.Add(Wood, 1_000);
        city.Add(new BuildingInstance("maxed", SliceEconomy.LumberCamp, 1, 2, UpgradeCurve.MaxLevel));

        var check = ConstructionRules.CanUpgrade(city, "maxed");

        Assert.Equal(ConstructionRefusal.MaxLevelReached, check.Refusal);
    }

    [Fact]
    public void An_upgrade_does_not_grant_the_new_level_until_it_completes()
    {
        // Awarding the level early would let a player buy output they have not waited for.
        var city = SliceEconomy.NewPlayerCity();
        city.Credit(Money.FromCoins(10_000));
        city.Inventory.Add(Wood, 500);
        var building = new BuildingInstance("camp", SliceEconomy.LumberCamp, 1, 2);
        city.Add(building);

        building.BeginUpgrade(SliceEconomy.Epoch);

        Assert.Equal(1, building.Level);
        Assert.Equal(2, building.PendingLevel);
        Assert.True(building.IsUpgrading);
        Assert.Equal(BuildingState.UnderConstruction, building.State);
    }

    // -----------------------------------------------------------------------------------
    // Completion is driven by settlement, not by a background job
    // -----------------------------------------------------------------------------------

    [Fact]
    public void A_building_completes_during_settlement_and_starts_producing()
    {
        var city = SliceEconomy.WithCapacity(10_000);
        city.Add(BuildingInstance.PlaceNew("camp", SliceEconomy.LumberCamp, 1, 2, SliceEconomy.Epoch));

        // 30 s build, then the rest of the hour producing.
        SettlementEngine.Settle(city, SliceEconomy.Epoch.AddHours(1));

        var camp = city.BuildingById("camp")!;
        Assert.Equal(BuildingState.Producing, camp.State);
        Assert.Null(camp.CompletesAtUtc);
        Assert.True(city.Inventory.Get(Wood) > 0, "The camp should have produced wood after completing.");
    }

    [Fact]
    public void A_new_players_first_camp_fills_the_town_hall_and_halts_on_capacity()
    {
        // Not a defect — this is the designed signal from ECONOMY_DESIGN.md §8. A new city
        // holds 100 per resource and starts with 80 wood, so the first Lumber Camp fills it
        // within the hour. A full warehouse is what makes the player want a warehouse.
        var city = SliceEconomy.NewPlayerCity();
        city.Add(BuildingInstance.PlaceNew("camp", SliceEconomy.LumberCamp, 1, 2, SliceEconomy.Epoch));

        SettlementEngine.Settle(city, SliceEconomy.Epoch.AddHours(1));

        var camp = city.BuildingById("camp")!;
        Assert.Equal(100, city.Inventory.Get(Wood));   // capped, nothing destroyed
        Assert.Equal(BuildingState.Halted, camp.State);
        Assert.Equal(HaltReason.NoCapacity, camp.HaltReason);
    }

    [Fact]
    public void A_building_produces_nothing_while_still_under_construction()
    {
        var city = SliceEconomy.NewPlayerCity();
        city.Add(BuildingInstance.PlaceNew("camp", SliceEconomy.LumberCamp, 1, 2, SliceEconomy.Epoch));

        // The build takes 30 s; settle to 20 s.
        SettlementEngine.Settle(city, SliceEconomy.Epoch.AddSeconds(20));

        Assert.Equal(BuildingState.UnderConstruction, city.BuildingById("camp")!.State);
        Assert.Equal(80, city.Inventory.Get(Wood)); // unchanged from the starting stock
    }

    [Fact]
    public void Settlement_reports_completed_buildings()
    {
        var city = SliceEconomy.NewPlayerCity();
        city.Add(BuildingInstance.PlaceNew("camp", SliceEconomy.LumberCamp, 1, 2, SliceEconomy.Epoch));

        var result = SettlementEngine.Settle(city, SliceEconomy.Epoch.AddMinutes(5));

        Assert.Contains("camp", result.CompletedBuildings);
    }

    [Fact]
    public void A_completed_warehouse_raises_capacity_before_production_is_capped()
    {
        // Ordering matters: if capacity were recomputed after the production pass, a city
        // would spend a step wrongly reporting NoCapacity on the very step its warehouse
        // finished.
        var city = new City("c", SliceEconomy.Epoch);
        city.SetPlots(SliceEconomy.DefaultPlots());
        city.Add(new BuildingInstance("hall", SliceEconomy.TownHall, 2, 3));
        city.Add(BuildingInstance.PlaceNew("wh", SliceEconomy.Warehouse, 1, 2, SliceEconomy.Epoch));

        Assert.Equal(100, city.Inventory.CapacityPerResource); // town hall only

        SettlementEngine.Settle(city, SliceEconomy.Epoch.AddMinutes(5));

        Assert.Equal(300, city.Inventory.CapacityPerResource); // town hall 100 + warehouse 200
    }

    [Fact]
    public void An_upgrade_completes_and_raises_the_production_rate()
    {
        var city = SliceEconomy.WithBuildings((SliceEconomy.LumberCamp, 1));
        var camp = city.Buildings.Single(b => b.Definition.Id == "lumber_camp");

        camp.BeginUpgrade(SliceEconomy.Epoch);
        // Level-2 build takes 30 s x 3 = 90 s. Settle past it, then measure a steady hour.
        SettlementEngine.Settle(city, SliceEconomy.Epoch.AddMinutes(5));
        var baseline = city.Inventory.Get(Wood);

        SettlementEngine.Settle(city, SliceEconomy.Epoch.AddMinutes(65));

        Assert.Equal(2, camp.Level);
        Assert.Equal(192, city.Inventory.Get(Wood) - baseline); // 120 x 1.6
    }

    [Fact]
    public void Construction_progress_is_reported_for_the_staged_visuals()
    {
        var camp = BuildingInstance.PlaceNew("camp", SliceEconomy.LumberCamp, 1, 2, SliceEconomy.Epoch);

        Assert.Equal(0, camp.ConstructionProgress(SliceEconomy.Epoch));
        Assert.Equal(0.5, camp.ConstructionProgress(SliceEconomy.Epoch.AddSeconds(15)), precision: 3);
        Assert.Equal(1, camp.ConstructionProgress(SliceEconomy.Epoch.AddSeconds(30)));
    }

    [Fact]
    public void Completion_is_still_deterministic_across_settlement_boundaries()
    {
        // Construction must not weaken the determinism invariant that the whole time model
        // rests on (ADR-003).
        var oneJump = SliceEconomy.NewPlayerCity();
        oneJump.Add(BuildingInstance.PlaceNew("camp", SliceEconomy.LumberCamp, 1, 2, SliceEconomy.Epoch));
        SettlementEngine.Settle(oneJump, SliceEconomy.Epoch.AddMinutes(30));

        var manySteps = SliceEconomy.NewPlayerCity();
        manySteps.Add(BuildingInstance.PlaceNew("camp", SliceEconomy.LumberCamp, 1, 2, SliceEconomy.Epoch));
        for (var minute = 1; minute <= 30; minute++)
        {
            SettlementEngine.Settle(manySteps, SliceEconomy.Epoch.AddMinutes(minute));
        }

        Assert.Equal(oneJump.Inventory.Get(Wood), manySteps.Inventory.Get(Wood));
        Assert.Equal(oneJump.BuildingById("camp")!.State, manySteps.BuildingById("camp")!.State);
    }
}
