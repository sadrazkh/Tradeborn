using Tradeborn.Domain.Buildings;
using Tradeborn.Domain.Economy;
using Tradeborn.Domain.Production;

namespace Tradeborn.UnitTests;

public class ProductionControlTests
{
    private static readonly ResourceId Wood = ResourceId.From("wood");
    private static readonly ResourceId Planks = ResourceId.From("planks");

    [Fact]
    public void An_idle_building_produces_nothing()
    {
        var city = SliceEconomy.WithBuildings((SliceEconomy.LumberCamp, 1));
        city.Buildings.Single(b => b.Definition.Id == "lumber_camp").StopProduction();

        SettlementEngine.Settle(city, SliceEconomy.Epoch.AddHours(1));

        Assert.Equal(0, city.Inventory.Get(Wood));
    }

    [Fact]
    public void An_idle_building_reports_no_halt_reason()
    {
        // Idle is a deliberate choice, so it must be silent. Showing a warning mote over a
        // building the player switched off themselves would train them to ignore warnings.
        var city = SliceEconomy.WithBuildings((SliceEconomy.LumberCamp, 1));
        var camp = city.Buildings.Single(b => b.Definition.Id == "lumber_camp");
        camp.StopProduction();

        SettlementEngine.Settle(city, SliceEconomy.Epoch.AddHours(1));

        Assert.Equal(BuildingState.Idle, camp.State);
        Assert.Equal(HaltReason.None, camp.HaltReason);
    }

    [Fact]
    public void Pausing_keeps_banked_cycle_progress()
    {
        // Pausing briefly must cost nothing, or the control becomes a trap rather than a lever.
        var city = SliceEconomy.WithBuildings((SliceEconomy.LumberCamp, 1));
        var camp = city.Buildings.Single(b => b.Definition.Id == "lumber_camp");

        SettlementEngine.Settle(city, SliceEconomy.Epoch.AddSeconds(29));
        var banked = camp.ProgressMilliseconds;
        Assert.True(banked > 0, "Expected partial cycle progress before pausing.");

        camp.StopProduction();

        Assert.Equal(banked, camp.ProgressMilliseconds);
    }

    [Fact]
    public void Resuming_continues_from_where_it_stopped()
    {
        var city = SliceEconomy.WithBuildings((SliceEconomy.LumberCamp, 1));
        var camp = city.Buildings.Single(b => b.Definition.Id == "lumber_camp");

        SettlementEngine.Settle(city, SliceEconomy.Epoch.AddSeconds(29));
        camp.StopProduction();
        SettlementEngine.Settle(city, SliceEconomy.Epoch.AddHours(4)); // paused: nothing happens
        Assert.Equal(0, city.Inventory.Get(Wood));

        camp.StartProduction();
        SettlementEngine.Settle(city, SliceEconomy.Epoch.AddHours(4).AddSeconds(30));

        // The banked 29 s plus a fresh 30 s window completes at least one cycle promptly,
        // and certainly not the four hours' worth that a naive elapsed-time model would grant.
        Assert.InRange(city.Inventory.Get(Wood), 1, 3);
    }

    [Fact]
    public void Pausing_a_sawmill_banks_wood_instead_of_turning_it_into_planks()
    {
        // The surplus decision from ECONOMY_DESIGN.md §3, expressed as a control: stopping the
        // sawmill is how a player saves wood toward a Bakery rather than converting it.
        var running = SliceEconomy.WithBuildings((SliceEconomy.LumberCamp, 1), (SliceEconomy.Sawmill, 1));
        SettlementEngine.Settle(running, SliceEconomy.Epoch.AddHours(1));

        var paused = SliceEconomy.WithBuildings((SliceEconomy.LumberCamp, 1), (SliceEconomy.Sawmill, 1));
        paused.Buildings.Single(b => b.Definition.Id == "sawmill").StopProduction();
        SettlementEngine.Settle(paused, SliceEconomy.Epoch.AddHours(1));

        Assert.True(running.Inventory.Get(Planks) > 0);
        Assert.Equal(0, paused.Inventory.Get(Planks));

        // All 120 wood is retained rather than half of it becoming planks.
        Assert.Equal(120, paused.Inventory.Get(Wood));
        Assert.True(paused.Inventory.Get(Wood) > running.Inventory.Get(Wood));
    }

    [Fact]
    public void An_idle_city_settles_immediately_via_the_fixed_point_exit()
    {
        // A switched-off building cannot change without the player, so a long absence must
        // not walk 86 400 grid steps discovering that nothing happens.
        var city = SliceEconomy.WithBuildings((SliceEconomy.LumberCamp, 1));
        city.Buildings.Single(b => b.Definition.Id == "lumber_camp").StopProduction();

        var result = SettlementEngine.Settle(city, SliceEconomy.Epoch.AddDays(30));

        Assert.True(result.StepsRun < 10, $"Expected an immediate fixed point, ran {result.StepsRun} steps.");
    }

    // -----------------------------------------------------------------------------------
    // Rules
    // -----------------------------------------------------------------------------------

    [Fact]
    public void A_building_with_no_recipe_cannot_be_switched_on()
    {
        var city = SliceEconomy.NewPlayerCity();

        var check = ProductionRules.CanSetActive(city, "town-hall", active: true);

        Assert.Equal(ProductionRefusal.NoRecipe, check.Refusal);
    }

    [Fact]
    public void A_building_under_construction_cannot_be_switched_on()
    {
        var city = SliceEconomy.NewPlayerCity();
        city.Add(BuildingInstance.PlaceNew("camp", SliceEconomy.LumberCamp, 1, 2, SliceEconomy.Epoch));

        var check = ProductionRules.CanSetActive(city, "camp", active: true);

        Assert.Equal(ProductionRefusal.UnderConstruction, check.Refusal);
    }

    [Fact]
    public void An_unknown_building_is_refused()
    {
        var city = SliceEconomy.NewPlayerCity();

        var check = ProductionRules.CanSetActive(city, "does-not-exist", active: true);

        Assert.Equal(ProductionRefusal.BuildingNotFound, check.Refusal);
    }

    [Fact]
    public void Starting_an_already_running_building_is_refused()
    {
        var city = SliceEconomy.WithBuildings((SliceEconomy.LumberCamp, 1));
        var id = city.Buildings.Single(b => b.Definition.Id == "lumber_camp").Id;

        var check = ProductionRules.CanSetActive(city, id, active: true);

        Assert.Equal(ProductionRefusal.AlreadyInThatState, check.Refusal);
    }

    [Fact]
    public void A_halted_building_can_still_be_switched_off()
    {
        // Halted means blocked, not off. The player must be able to stop a blocked building
        // — that is exactly when they want to stop feeding it.
        var city = SliceEconomy.WithCapacity(10, (SliceEconomy.LumberCamp, 1));
        var camp = city.Buildings.Single(b => b.Definition.Id == "lumber_camp");
        SettlementEngine.Settle(city, SliceEconomy.Epoch.AddHours(1));

        Assert.Equal(BuildingState.Halted, camp.State);

        var check = ProductionRules.CanSetActive(city, camp.Id, active: false);
        Assert.True(check.IsAllowed);

        camp.StopProduction();
        Assert.Equal(BuildingState.Idle, camp.State);
        Assert.Equal(HaltReason.None, camp.HaltReason);
    }

    [Fact]
    public void Production_control_does_not_break_settlement_determinism()
    {
        var oneJump = SliceEconomy.WithBuildings((SliceEconomy.LumberCamp, 1), (SliceEconomy.Sawmill, 1));
        oneJump.Buildings.Single(b => b.Definition.Id == "sawmill").StopProduction();
        SettlementEngine.Settle(oneJump, SliceEconomy.Epoch.AddHours(8));

        var manySteps = SliceEconomy.WithBuildings((SliceEconomy.LumberCamp, 1), (SliceEconomy.Sawmill, 1));
        manySteps.Buildings.Single(b => b.Definition.Id == "sawmill").StopProduction();
        for (var minute = 1; minute <= 480; minute++)
        {
            SettlementEngine.Settle(manySteps, SliceEconomy.Epoch.AddMinutes(minute));
        }

        Assert.Equal(oneJump.Inventory.Get(Wood), manySteps.Inventory.Get(Wood));
        Assert.Equal(oneJump.Inventory.Get(Planks), manySteps.Inventory.Get(Planks));
    }
}
