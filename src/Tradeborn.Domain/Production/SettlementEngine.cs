using Tradeborn.Domain.Buildings;
using Tradeborn.Domain.Cities;
using Tradeborn.Domain.Economy;
using Tradeborn.Domain.Logistics;

namespace Tradeborn.Domain.Production;

/// <summary>
/// Deterministic Lazy Settlement — the heart of the time model (ADR-003).
/// </summary>
/// <remarks>
/// <para>
/// Nothing advances until someone looks. Calling <see cref="Settle"/> brings a city from
/// <see cref="City.LastSettledAt"/> up to a given instant: completing builds, landing
/// deliveries, running production, and dispatching carts.
/// </para>
/// <para><b>Why a fixed grid.</b> Sub-steps are aligned to an absolute time grid
/// (multiples of <see cref="StepMilliseconds"/> since the Unix epoch) rather than being
/// derived from the elapsed span. This is what makes the determinism invariant hold
/// exactly: settling once over eight hours walks precisely the same grid cells as settling
/// 480 times over one minute each, so both produce identical state. A step size computed
/// from the elapsed span would produce different cell boundaries and the results would
/// diverge.</para>
/// <para><b>Why goods pass through a buffer.</b> Production fills a building's local output
/// buffer; only a delivered <see cref="TransportJob"/> moves goods into the city's
/// inventory. That is what makes "goods do not teleport" (GDD §3.5) true of the economy
/// rather than merely of the animation.</para>
/// <para><b>Why it is fast.</b> Once every producing building is halted or idle, no cart is
/// on the road, and no construction is pending, the state is a fixed point: no further step
/// can change anything until the player acts. Settlement stops there.</para>
/// </remarks>
public static class SettlementEngine
{
    /// <summary>
    /// Sub-step size. Equal to the shortest recipe cycle in the vertical slice (30 s), so
    /// no cycle can complete entirely inside a single step unnoticed.
    /// </summary>
    public const long StepMilliseconds = 30_000;

    /// <summary>Absences longer than this are clamped. Storage caps mean nothing is actually lost.</summary>
    public static readonly TimeSpan MaxOfflineWindow = TimeSpan.FromDays(30);

    /// <summary>Backstop against an unbounded loop. Never reached in practice — the fixed-point exit fires first.</summary>
    private const int MaxSteps = 200_000;

    public static SettlementResult Settle(City city, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(city);

        if (now <= city.LastSettledAt)
        {
            // Clock skew, or already current. Never rewind a city.
            return SettlementResult.Empty(city.LastSettledAt);
        }

        if (now - city.LastSettledAt > MaxOfflineWindow)
        {
            city.LastSettledAt = now - MaxOfflineWindow;
        }

        var produced = new Dictionary<ResourceId, long>();
        var delivered = new Dictionary<ResourceId, long>();
        var completed = new List<string>();

        var producers = city.Buildings
            .Where(b => b.Definition.Recipe is not null)
            .OrderBy(b => b.Definition.Recipe!.TopologicalRank)
            .ThenBy(b => b.Id, StringComparer.Ordinal)
            .ToArray();

        var cursor = city.LastSettledAt;
        var steps = 0;

        while (cursor < now && steps < MaxSteps)
        {
            var stepEnd = NextGridBoundary(cursor);
            if (stepEnd > now)
            {
                stepEnd = now;
            }

            // Order matters. Completions first, so a building that finishes at this boundary
            // works for the rest of the step. Then arrivals, so goods a cart just brought are
            // available to consumers in the same step. Production last, filling buffers that
            // the dispatch pass then loads onto carts.
            var anyCompleted = CompleteFinishedWork(city, stepEnd, completed);
            var anyDelivery = DeliverArrivals(city, stepEnd, delivered);

            var deltaMs = (long)(stepEnd - cursor).TotalMilliseconds;
            var anyProduction = AdvanceStep(city, producers, deltaMs, produced);

            DispatchTransports(city, producers, stepEnd);

            cursor = stepEnd;
            steps++;

            if (IsFixedPoint(city, producers, anyProduction || anyDelivery, anyCompleted, now))
            {
                break;
            }
        }

        city.LastSettledAt = now;

        return new SettlementResult(now, steps, produced, completed, delivered);
    }

    /// <summary>
    /// Completes builds and upgrades whose deadline has passed.
    /// </summary>
    /// <remarks>
    /// Deliberately not a scheduled job. Completion is a purely time-driven state transition,
    /// so settlement already has everything it needs — which means a construction finishes
    /// correctly whether or not a background worker is running (ADR-008). Completions land on
    /// the 30 s grid, so a build may finish up to one step after its exact deadline.
    /// </remarks>
    private static bool CompleteFinishedWork(City city, DateTimeOffset at, List<string> completed)
    {
        var any = false;

        foreach (var building in city.Buildings)
        {
            if (building.TryComplete(at))
            {
                completed.Add(building.Id);
                any = true;
            }
        }

        if (any)
        {
            // A finished warehouse adds storage; capacity is derived, so it must be recomputed
            // before deliveries decide how much they can unload.
            city.RecomputeCapacity();
        }

        return any;
    }

    /// <summary>
    /// Unloads carts that have arrived.
    /// </summary>
    /// <remarks>
    /// A load that does not fit is <b>not</b> destroyed: the remainder returns to the
    /// producer's buffer, which then fills and halts that building with
    /// <see cref="HaltReason.NoCapacity"/>. Vaporising overflow would be the most infuriating
    /// thing this game could do to a player who came back after a long absence.
    /// </remarks>
    private static bool DeliverArrivals(
        City city,
        DateTimeOffset at,
        Dictionary<ResourceId, long> deliveredTotals)
    {
        var arrived = city.Transports.Where(t => t.HasArrivedBy(at)).ToArray();
        if (arrived.Length == 0)
        {
            return false;
        }

        var any = false;

        foreach (var job in arrived)
        {
            var space = city.Inventory.FreeSpace(job.Resource);
            var accepted = Math.Min(job.Quantity, space);

            if (accepted > 0)
            {
                city.Inventory.Add(job.Resource, accepted);
                deliveredTotals[job.Resource] = deliveredTotals.GetValueOrDefault(job.Resource) + accepted;
                job.Deliver(accepted);
                city.RecordDelivery();
                any = true;
            }

            if (job.Quantity > 0)
            {
                city.BuildingById(job.FromBuildingId)?.AddToBuffer(job.Resource, job.Quantity);
            }

            city.RemoveTransport(job);
        }

        return any;
    }

    /// <summary>
    /// Loads waiting goods onto a cart.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One cart per building at a time. That bounds the vehicles on screen by the number of
    /// producers, which is what keeps the pooled renderer inside the draw-call budget — and it
    /// is the reason the output buffer exists at all.
    /// </para>
    /// <para>
    /// A cart only leaves with what storage can actually accept. Sending a load that cannot be
    /// unloaded would have it bounce straight back to the buffer and be dispatched again on the
    /// next step — an endless shuttle that never terminates and, worse, never reaches a fixed
    /// point, so a month-long absence would walk all 86 400 grid steps.
    /// </para>
    /// </remarks>
    private static void DispatchTransports(
        City city,
        IReadOnlyList<BuildingInstance> producers,
        DateTimeOffset at)
    {
        var destination = city.DeliveryPoint;

        foreach (var building in producers)
        {
            if (building.BufferedQuantity == 0 || city.HasTransportFrom(building.Id))
            {
                continue;
            }

            var distance =
                Math.Abs(building.Col - destination.Col) + Math.Abs(building.Row - destination.Row);
            var travelMs = LogisticsTuning.TravelMilliseconds(distance);

            // Snapshot the buffer: the loop below mutates it.
            foreach (var (resource, buffered) in building.OutputBuffer.ToArray())
            {
                // Space already claimed by carts on the road, so two of them cannot both
                // target the last free slot.
                var inFlight = city.Transports
                    .Where(t => t.Resource == resource)
                    .Sum(t => t.Quantity);

                var room = Math.Max(0, city.Inventory.FreeSpace(resource) - inFlight);
                var load = Math.Min(buffered, room);

                if (load <= 0)
                {
                    continue; // storage is full; the goods wait here and the building halts
                }

                building.TakeFromBuffer(resource, load);

                // A deterministic id, not a random Guid: settling the same city twice must
                // produce identical state, ids included.
                var id = $"{building.Id}:{resource.Value}:{at.ToUnixTimeMilliseconds()}";

                city.AddTransport(new TransportJob(
                    id, building.Id, resource, load, at, at.AddMilliseconds(travelMs)));
            }
        }
    }

    /// <summary>
    /// True when no further step could change anything.
    /// </summary>
    /// <remarks>
    /// Pending construction and carts on the road are both part of the test. Without them, a
    /// city whose buildings are all halted would exit early and skip past a build that was due
    /// to finish or a delivery that was due to land.
    /// </remarks>
    private static bool IsFixedPoint(
        City city,
        IReadOnlyList<BuildingInstance> producers,
        bool anythingHappened,
        bool anyCompleted,
        DateTimeOffset now)
    {
        if (anythingHappened || anyCompleted)
        {
            return false;
        }

        if (city.Buildings.Any(b => b.IsUnderConstruction && b.CompletesAtUtc <= now))
        {
            return false;
        }

        // A cart still on the road will change the inventory when it lands.
        if (city.Transports.Count > 0)
        {
            return false;
        }

        // Idle counts as settled: a switched-off building cannot change without the player.
        return producers.All(b =>
            b.IsUnderConstruction ||
            b.State == BuildingState.Idle ||
            b.HaltReason != HaltReason.None);
    }

    private static DateTimeOffset NextGridBoundary(DateTimeOffset from)
    {
        var ms = from.ToUnixTimeMilliseconds();
        var next = ((ms / StepMilliseconds) + 1) * StepMilliseconds;
        return DateTimeOffset.FromUnixTimeMilliseconds(next);
    }

    private static bool AdvanceStep(
        City city,
        IReadOnlyList<BuildingInstance> producers,
        long deltaMs,
        Dictionary<ResourceId, long> producedTotals)
    {
        var anyProduction = false;

        foreach (var building in producers)
        {
            // A half-built factory produces nothing, and neither does one the player has
            // switched off. Idle is a deliberate choice, so it is silent — no halt reason,
            // no warning mote.
            if (building.IsUnderConstruction || building.State == BuildingState.Idle)
            {
                continue;
            }

            var recipe = building.Definition.Recipe!;
            building.ProgressMilliseconds += deltaMs;

            var cycleMs = building.CycleMilliseconds;
            var byTime = building.ProgressMilliseconds / cycleMs;
            if (byTime <= 0)
            {
                continue; // mid-cycle, nothing to decide yet
            }

            var byInput = long.MaxValue;
            foreach (var input in recipe.Inputs)
            {
                byInput = Math.Min(byInput, city.Inventory.Get(input.Resource) / input.Quantity);
            }

            // Output is limited by the building's own buffer, not by central storage: the
            // goods are not in the warehouse yet, a cart still has to take them there.
            var outputPerCycle = recipe.Outputs.Sum(o => o.Quantity);
            var byCapacity = outputPerCycle <= 0
                ? long.MaxValue
                : building.BufferFreeSpace / outputPerCycle;

            var cycles = Math.Min(byTime, Math.Min(byInput, byCapacity));

            if (cycles > 0)
            {
                // Inputs come out of central storage immediately, so two buildings competing
                // for the same input in one step cannot both spend it.
                foreach (var input in recipe.Inputs)
                {
                    city.Inventory.Remove(input.Resource, input.Quantity * cycles);
                }

                foreach (var output in recipe.Outputs)
                {
                    var quantity = output.Quantity * cycles;
                    building.AddToBuffer(output.Resource, quantity);
                    producedTotals[output.Resource] = producedTotals.GetValueOrDefault(output.Resource) + quantity;
                }

                building.ProgressMilliseconds -= cycles * cycleMs;
                building.HaltReason = HaltReason.None;
                building.State = BuildingState.Producing;
                anyProduction = true;
            }
            else
            {
                building.HaltReason = byInput <= 0 ? HaltReason.NoInput : HaltReason.NoCapacity;
                building.State = BuildingState.Halted;

                // A halted building does not bank time. Without this cap, a sawmill starved
                // for eight hours would dump 960 planks the instant a single log arrived.
                building.ProgressMilliseconds = Math.Min(building.ProgressMilliseconds, cycleMs - 1);
            }
        }

        return anyProduction;
    }
}

/// <summary>What a settlement pass did. Drives the "while you were away" recap.</summary>
public sealed record SettlementResult(
    DateTimeOffset SettledAt,
    int StepsRun,
    IReadOnlyDictionary<ResourceId, long> Produced,
    IReadOnlyList<string> CompletedBuildings,
    IReadOnlyDictionary<ResourceId, long> Delivered)
{
    public static SettlementResult Empty(DateTimeOffset at) =>
        new(at, 0, new Dictionary<ResourceId, long>(), [], new Dictionary<ResourceId, long>());

    public bool ProducedAnything => Produced.Count > 0;

    /// <summary>
    /// What actually reached storage.
    /// </summary>
    /// <remarks>
    /// The offline recap reports this rather than <see cref="Produced"/>. Goods still on a cart
    /// are not yet the player's to spend, and claiming otherwise would make the recap disagree
    /// with the balances shown right next to it.
    /// </remarks>
    public bool DeliveredAnything => Delivered.Count > 0;
}
