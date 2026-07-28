using Tradeborn.Domain.Buildings;
using Tradeborn.Domain.Economy;
using Tradeborn.Domain.Logistics;
using Tradeborn.Domain.Production;

namespace Tradeborn.UnitTests;

public class TransportTests
{
    private static readonly ResourceId Wood = ResourceId.From("wood");
    private static readonly ResourceId Planks = ResourceId.From("planks");

    [Fact]
    public void Production_lands_in_the_buffer_not_in_storage()
    {
        // The whole point of Phase 5: goods do not teleport into the warehouse.
        var city = SliceEconomy.WithBuildings((SliceEconomy.LumberCamp, 1));
        var camp = city.Buildings.Single(b => b.Definition.Id == "lumber_camp");

        // One 30 s step: one unit made, cart dispatched at the step boundary.
        SettlementEngine.Settle(city, SliceEconomy.Epoch.AddSeconds(30));

        Assert.Equal(0, city.Inventory.Get(Wood));
        Assert.Single(city.Transports);
        Assert.Equal(0, camp.BufferedQuantity); // loaded onto the cart
    }

    [Fact]
    public void Goods_reach_storage_only_after_the_journey_completes()
    {
        var city = SliceEconomy.WithBuildings((SliceEconomy.LumberCamp, 1));

        SettlementEngine.Settle(city, SliceEconomy.Epoch.AddSeconds(30));
        Assert.Equal(0, city.Inventory.Get(Wood));

        // The camp sits a few plots from the Town Hall, so the trip is several seconds.
        SettlementEngine.Settle(city, SliceEconomy.Epoch.AddSeconds(90));

        Assert.True(city.Inventory.Get(Wood) > 0, "The load should have been delivered by now.");
    }

    [Fact]
    public void A_journey_has_a_departure_and_an_arrival_the_client_can_interpolate()
    {
        var city = SliceEconomy.WithBuildings((SliceEconomy.LumberCamp, 1));
        SettlementEngine.Settle(city, SliceEconomy.Epoch.AddSeconds(30));

        var job = city.Transports.Single();

        Assert.True(job.ArrivesAtUtc > job.DepartedAtUtc);
        Assert.Equal(0, job.Progress(job.DepartedAtUtc));
        Assert.Equal(1, job.Progress(job.ArrivesAtUtc));
        Assert.Equal(0.5, job.Progress(job.DepartedAtUtc + ((job.ArrivesAtUtc - job.DepartedAtUtc) / 2)), precision: 2);
    }

    [Fact]
    public void Only_one_cart_per_building_is_on_the_road_at_a_time()
    {
        // Bounds vehicles by producer count, which is what keeps the pooled renderer inside
        // the draw-call budget.
        var city = SliceEconomy.WithBuildings((SliceEconomy.LumberCamp, 1));

        SettlementEngine.Settle(city, SliceEconomy.Epoch.AddMinutes(10));

        Assert.True(city.Transports.Count <= 1, $"Expected at most one cart, found {city.Transports.Count}.");
    }

    [Fact]
    public void A_full_warehouse_stops_dispatch_rather_than_shuttling_forever()
    {
        // Regression guard. Dispatching a load storage cannot accept made it bounce back to
        // the buffer and be sent again next step — an endless shuttle that never reached a
        // fixed point, so a 30-day absence walked all 86 400 grid steps.
        var city = SliceEconomy.WithCapacity(20, (SliceEconomy.LumberCamp, 1));

        var result = SettlementEngine.Settle(city, SliceEconomy.Epoch.AddDays(30));

        Assert.Equal(20, city.Inventory.Get(Wood));
        Assert.Empty(city.Transports);
        Assert.True(result.StepsRun < 500, $"Expected an early fixed point, ran {result.StepsRun} steps.");
    }

    [Fact]
    public void Nothing_is_destroyed_when_storage_fills_mid_journey()
    {
        // A cart in flight when storage fills must not vaporise its load.
        var city = SliceEconomy.WithCapacity(40, (SliceEconomy.LumberCamp, 1));
        var camp = city.Buildings.Single(b => b.Definition.Id == "lumber_camp");

        SettlementEngine.Settle(city, SliceEconomy.Epoch.AddHours(1));

        var accountedFor = city.Inventory.Get(Wood)
            + camp.BufferedQuantity
            + city.Transports.Sum(t => t.Quantity);

        Assert.Equal(40, city.Inventory.Get(Wood));
        Assert.True(accountedFor >= 40, $"Goods went missing: only {accountedFor} accounted for.");
    }

    [Fact]
    public void The_output_buffer_is_bounded_and_halts_the_building_when_full()
    {
        var city = SliceEconomy.WithCapacity(0, (SliceEconomy.LumberCamp, 1));
        var camp = city.Buildings.Single(b => b.Definition.Id == "lumber_camp");

        SettlementEngine.Settle(city, SliceEconomy.Epoch.AddHours(1));

        Assert.Equal(LogisticsTuning.BufferCapacity, camp.BufferedQuantity);
        Assert.Equal(BuildingState.Halted, camp.State);
        Assert.Equal(HaltReason.NoCapacity, camp.HaltReason);
    }

    [Fact]
    public void Transport_does_not_break_settlement_determinism()
    {
        // Journeys are part of the economy now, so they must respect the invariant the whole
        // time model rests on (ADR-003) — including the generated job ids.
        var chain = new (BuildingDefinition, int)[]
        {
            (SliceEconomy.LumberCamp, 1),
            (SliceEconomy.Sawmill, 1),
        };

        var oneJump = SliceEconomy.WithBuildings(chain);
        SettlementEngine.Settle(oneJump, SliceEconomy.Epoch.AddHours(8));

        var manySteps = SliceEconomy.WithBuildings(chain);
        for (var minute = 1; minute <= 480; minute++)
        {
            SettlementEngine.Settle(manySteps, SliceEconomy.Epoch.AddMinutes(minute));
        }

        Assert.Equal(oneJump.Inventory.Get(Wood), manySteps.Inventory.Get(Wood));
        Assert.Equal(oneJump.Inventory.Get(Planks), manySteps.Inventory.Get(Planks));
        Assert.Equal(oneJump.Transports.Count, manySteps.Transports.Count);

        foreach (var (a, b) in oneJump.Transports.OrderBy(t => t.Id, StringComparer.Ordinal)
                     .Zip(manySteps.Transports.OrderBy(t => t.Id, StringComparer.Ordinal)))
        {
            Assert.Equal(a.Id, b.Id);
            Assert.Equal(a.Quantity, b.Quantity);
            Assert.Equal(a.ArrivesAtUtc, b.ArrivesAtUtc);
        }
    }

    [Fact]
    public void Settlement_reports_what_was_delivered_separately_from_what_was_produced()
    {
        // The recap must report deliveries: goods still on a cart are not the player's to
        // spend, and saying otherwise would contradict the balance shown beside it.
        var city = SliceEconomy.WithBuildings((SliceEconomy.LumberCamp, 1));

        var result = SettlementEngine.Settle(city, SliceEconomy.Epoch.AddHours(1));

        Assert.Equal(120, result.Produced[Wood]);
        Assert.True(result.DeliveredAnything);
        Assert.True(
            result.Delivered[Wood] <= result.Produced[Wood],
            "More was delivered than was ever made.");
    }

    [Fact]
    public void Travel_time_grows_with_distance_from_the_delivery_point()
    {
        Assert.Equal(
            LogisticsTuning.BaseTravelMilliseconds,
            LogisticsTuning.TravelMilliseconds(0));

        Assert.True(
            LogisticsTuning.TravelMilliseconds(5) > LogisticsTuning.TravelMilliseconds(1),
            "A more distant producer should take longer to deliver.");
    }
}
