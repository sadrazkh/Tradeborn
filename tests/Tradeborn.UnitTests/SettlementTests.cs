using Tradeborn.Domain.Buildings;
using Tradeborn.Domain.Cities;
using Tradeborn.Domain.Economy;
using Tradeborn.Domain.Production;

namespace Tradeborn.UnitTests;

public class SettlementTests
{
    private static readonly ResourceId Wood = ResourceId.From("wood");
    private static readonly ResourceId Grain = ResourceId.From("grain");
    private static readonly ResourceId Planks = ResourceId.From("planks");
    private static readonly ResourceId Flour = ResourceId.From("flour");
    private static readonly ResourceId Bread = ResourceId.From("bread");

    // -----------------------------------------------------------------------------------
    // Published rates — these assert that the code matches docs/economy/ECONOMY_DESIGN.md §4
    // -----------------------------------------------------------------------------------

    [Fact]
    public void Lumber_camp_produces_the_documented_120_wood_per_hour()
    {
        var city = SliceEconomy.WithBuildings((SliceEconomy.LumberCamp, 1));

        SettlementEngine.Settle(city, SliceEconomy.Epoch.AddHours(1));

        Assert.Equal(120, city.Inventory.Get(Wood));
    }

    [Theory]
    [InlineData(1, 120)] // base
    [InlineData(2, 192)] // 120 x 1.6
    [InlineData(3, 307)] // 120 x 1.6^2, floored
    public void Upgrades_produce_the_documented_rates_without_rounding_loss(int level, long expectedPerHour)
    {
        // Guards BALANCE_ASSUMPTIONS A-10: scaling the cycle time rather than the output
        // quantity is what keeps a 1-unit-per-cycle building from losing ~25% at level 3.
        var city = SliceEconomy.WithBuildings((SliceEconomy.LumberCamp, level));

        SettlementEngine.Settle(city, SliceEconomy.Epoch.AddHours(1));

        Assert.Equal(expectedPerHour, city.Inventory.Get(Wood));
    }

    [Fact]
    public void One_lumber_camp_exactly_feeds_one_sawmill_in_steady_state()
    {
        // The documented 1:1 ratio: 120 wood produced per hour, 120 consumed, 60 planks out.
        // Measured over the SECOND hour — see Chains_take_one_cycle_to_spin_up below for why
        // the first hour is one cycle short.
        var city = SliceEconomy.WithBuildings(
            (SliceEconomy.LumberCamp, 1),
            (SliceEconomy.Sawmill, 1));

        SettlementEngine.Settle(city, SliceEconomy.Epoch.AddHours(1));
        var afterHourOne = city.Inventory.Get(Planks);

        SettlementEngine.Settle(city, SliceEconomy.Epoch.AddHours(2));

        Assert.Equal(60, city.Inventory.Get(Planks) - afterHourOne);

        // Wood does not accumulate: the camp's output is fully consumed by the sawmill.
        Assert.InRange(city.Inventory.Get(Wood), 0, 2);
    }

    [Fact]
    public void The_full_bread_chain_yields_30_bread_and_30_surplus_planks_per_hour()
    {
        // This is THE design decision from ECONOMY_DESIGN.md §3: the sawmill overshoots the
        // bakery by exactly 2x, and that surplus is what the player must decide about.
        var city = SliceEconomy.WithBuildings(
            (SliceEconomy.LumberCamp, 1),
            (SliceEconomy.Sawmill, 1),
            (SliceEconomy.Farm, 1),
            (SliceEconomy.Mill, 1),
            (SliceEconomy.Bakery, 1));

        // Settle past spin-up, then measure one steady-state hour.
        SettlementEngine.Settle(city, SliceEconomy.Epoch.AddHours(1));
        var breadBefore = city.Inventory.Get(Bread);
        var planksBefore = city.Inventory.Get(Planks);

        SettlementEngine.Settle(city, SliceEconomy.Epoch.AddHours(2));

        Assert.Equal(30, city.Inventory.Get(Bread) - breadBefore);
        Assert.Equal(30, city.Inventory.Get(Planks) - planksBefore);

        // Intermediates are consumed as fast as they are made, not stockpiled.
        Assert.InRange(city.Inventory.Get(Grain), 0, 2);
        Assert.InRange(city.Inventory.Get(Flour), 0, 2);
    }

    [Fact]
    public void Chains_take_one_cycle_to_spin_up()
    {
        // A consequence of deferred commit: a processor cannot consume output its producer
        // made in the same step, so a cold chain is exactly one cycle behind for its first
        // hour (59 planks, not 60). This is realistic pipeline-fill latency and it is
        // asserted here so it stays a known property rather than resurfacing as a bug.
        var city = SliceEconomy.WithBuildings(
            (SliceEconomy.LumberCamp, 1),
            (SliceEconomy.Sawmill, 1));

        SettlementEngine.Settle(city, SliceEconomy.Epoch.AddHours(1));

        Assert.Equal(59, city.Inventory.Get(Planks));
    }

    // -----------------------------------------------------------------------------------
    // The determinism invariant — the foundation of the whole time model (ADR-003)
    // -----------------------------------------------------------------------------------

    [Fact]
    public void Settling_in_one_jump_equals_settling_in_many_steps()
    {
        var chain = new (BuildingDefinition, int)[]
        {
            (SliceEconomy.LumberCamp, 1),
            (SliceEconomy.Sawmill, 1),
            (SliceEconomy.Farm, 1),
            (SliceEconomy.Mill, 1),
            (SliceEconomy.Bakery, 1),
        };

        var oneJump = SliceEconomy.WithBuildings(chain);
        SettlementEngine.Settle(oneJump, SliceEconomy.Epoch.AddHours(8));

        var manySteps = SliceEconomy.WithBuildings(chain);
        for (var minute = 1; minute <= 480; minute++)
        {
            SettlementEngine.Settle(manySteps, SliceEconomy.Epoch.AddMinutes(minute));
        }

        AssertSameInventory(oneJump, manySteps);

        foreach (var (a, b) in oneJump.Buildings.Zip(manySteps.Buildings))
        {
            Assert.Equal(a.ProgressMilliseconds, b.ProgressMilliseconds);
            Assert.Equal(a.State, b.State);
            Assert.Equal(a.HaltReason, b.HaltReason);
        }
    }

    [Fact]
    public void Settlement_is_deterministic_across_irregular_intervals()
    {
        var chain = new (BuildingDefinition, int)[] { (SliceEconomy.LumberCamp, 1), (SliceEconomy.Sawmill, 1) };

        var oneJump = SliceEconomy.WithBuildings(chain);
        SettlementEngine.Settle(oneJump, SliceEconomy.Epoch.AddMinutes(60));

        // Ragged, player-like access pattern rather than a neat cadence.
        var irregular = SliceEconomy.WithBuildings(chain);
        foreach (var minute in new[] { 1, 7, 8, 23, 24, 30, 44, 59, 60 })
        {
            SettlementEngine.Settle(irregular, SliceEconomy.Epoch.AddMinutes(minute));
        }

        AssertSameInventory(oneJump, irregular);
    }

    // -----------------------------------------------------------------------------------
    // The three limits that a naive elapsed x rate calculation gets wrong
    // -----------------------------------------------------------------------------------

    [Fact]
    public void A_processor_without_inputs_produces_nothing_and_reports_why()
    {
        var city = SliceEconomy.WithBuildings((SliceEconomy.Sawmill, 1));

        SettlementEngine.Settle(city, SliceEconomy.Epoch.AddHours(1));

        Assert.Equal(0, city.Inventory.Get(Planks));

        var sawmill = city.Buildings.Single(b => b.Definition.Id == "sawmill");
        Assert.Equal(HaltReason.NoInput, sawmill.HaltReason);
        Assert.Equal(BuildingState.Halted, sawmill.State);
    }

    [Fact]
    public void Production_halts_at_storage_capacity_without_destroying_goods()
    {
        var city = SliceEconomy.WithCapacity(50, (SliceEconomy.LumberCamp, 1));

        SettlementEngine.Settle(city, SliceEconomy.Epoch.AddHours(1));

        // 120/hour would be produced; capacity binds at 50 and nothing overflows.
        Assert.Equal(50, city.Inventory.Get(Wood));

        var camp = city.Buildings.Single(b => b.Definition.Id == "lumber_camp");
        Assert.Equal(HaltReason.NoCapacity, camp.HaltReason);
    }

    [Fact]
    public void A_halted_building_does_not_bank_time_while_starved()
    {
        // Without the progress cap, a sawmill starved for hours would dump a huge batch the
        // instant one log arrived — an exploit and an economy bug.
        var city = SliceEconomy.WithBuildings((SliceEconomy.Sawmill, 1));

        SettlementEngine.Settle(city, SliceEconomy.Epoch.AddHours(8));
        city.Inventory.Add(Wood, 100);
        SettlementEngine.Settle(city, SliceEconomy.Epoch.AddHours(8).AddMinutes(1));

        // One minute at a 60 s cycle: one cycle from the banked near-complete progress plus
        // one from the elapsed minute. Certainly not the 480 that 8 idle hours would allow.
        Assert.InRange(city.Inventory.Get(Planks), 1, 2);
    }

    [Fact]
    public void Partial_cycle_progress_survives_settlement()
    {
        // A building 29 s into a 30 s cycle must keep those 29 s; discarding them would
        // destroy production every time the player refreshed the page.
        var city = SliceEconomy.WithBuildings((SliceEconomy.LumberCamp, 1));

        SettlementEngine.Settle(city, SliceEconomy.Epoch.AddSeconds(29));
        Assert.Equal(0, city.Inventory.Get(Wood));

        SettlementEngine.Settle(city, SliceEconomy.Epoch.AddSeconds(31));
        Assert.Equal(1, city.Inventory.Get(Wood));
    }

    // -----------------------------------------------------------------------------------
    // Safety properties
    // -----------------------------------------------------------------------------------

    [Fact]
    public void Settling_backwards_in_time_changes_nothing()
    {
        var city = SliceEconomy.WithBuildings((SliceEconomy.LumberCamp, 1));
        SettlementEngine.Settle(city, SliceEconomy.Epoch.AddHours(1));
        var afterFirstPass = city.Inventory.Get(Wood);

        // Clock skew must never rewind a city or re-run production.
        SettlementEngine.Settle(city, SliceEconomy.Epoch.AddMinutes(30));

        Assert.Equal(afterFirstPass, city.Inventory.Get(Wood));
        Assert.Equal(SliceEconomy.Epoch.AddHours(1), city.LastSettledAt);
    }

    [Fact]
    public void Settling_twice_at_the_same_instant_is_idempotent()
    {
        var city = SliceEconomy.WithBuildings((SliceEconomy.LumberCamp, 1));
        var at = SliceEconomy.Epoch.AddHours(1);

        SettlementEngine.Settle(city, at);
        var once = city.Inventory.Get(Wood);
        SettlementEngine.Settle(city, at);

        Assert.Equal(once, city.Inventory.Get(Wood));
    }

    [Fact]
    public void A_long_absence_settles_quickly_via_the_fixed_point_exit()
    {
        // 30 days at a 30 s grid is 86 400 steps if walked naively. Once storage fills,
        // nothing can change, so settlement stops there.
        var city = SliceEconomy.WithCapacity(200, (SliceEconomy.LumberCamp, 1));

        var result = SettlementEngine.Settle(city, SliceEconomy.Epoch.AddDays(30));

        Assert.Equal(200, city.Inventory.Get(Wood));
        Assert.True(result.StepsRun < 500, $"Expected an early fixed-point exit, ran {result.StepsRun} steps.");
    }

    [Fact]
    public void Settlement_reports_what_was_produced_for_the_offline_recap()
    {
        var city = SliceEconomy.WithBuildings((SliceEconomy.LumberCamp, 1), (SliceEconomy.Sawmill, 1));

        var result = SettlementEngine.Settle(city, SliceEconomy.Epoch.AddHours(1));

        Assert.True(result.ProducedAnything);
        Assert.Equal(120, result.Produced[Wood]);  // gross production, before the sawmill consumed it
        Assert.Equal(59, result.Produced[Planks]); // one cycle of spin-up, see Chains_take_one_cycle_to_spin_up
    }

    [Fact]
    public void A_consumer_cannot_use_output_its_producer_has_not_yet_made()
    {
        // Deferred commit: within one step the sawmill must not consume wood the lumber
        // camp produced in that same step. Over a single 30 s step the camp makes 1 wood,
        // and the sawmill needs 2, so it must produce nothing at all.
        var city = SliceEconomy.WithBuildings((SliceEconomy.LumberCamp, 1), (SliceEconomy.Sawmill, 1));

        SettlementEngine.Settle(city, SliceEconomy.Epoch.AddSeconds(30));

        Assert.Equal(1, city.Inventory.Get(Wood));
        Assert.Equal(0, city.Inventory.Get(Planks));
    }

    private static void AssertSameInventory(City expected, City actual)
    {
        foreach (var resource in new[] { Wood, Grain, Planks, Flour, Bread })
        {
            Assert.Equal(expected.Inventory.Get(resource), actual.Inventory.Get(resource));
        }
    }
}
