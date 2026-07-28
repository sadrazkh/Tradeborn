using Tradeborn.Domain.Buildings;
using Tradeborn.Domain.Cities;
using Tradeborn.Domain.Economy;

namespace Tradeborn.Domain.Production;

/// <summary>
/// Deterministic Lazy Settlement — the heart of the time model (ADR-003).
/// </summary>
/// <remarks>
/// <para>
/// Nothing advances until someone looks. Calling <see cref="Settle"/> brings a city from
/// <see cref="City.LastSettledAt"/> up to a given instant, respecting the three limits that
/// a naive <c>elapsed × rate</c> calculation ignores: available time, available inputs, and
/// free storage. It also completes any build or upgrade whose deadline has passed.
/// </para>
/// <para><b>Why a fixed grid.</b> Sub-steps are aligned to an absolute time grid
/// (multiples of <see cref="StepMilliseconds"/> since the Unix epoch) rather than being
/// derived from the elapsed span. This is what makes the determinism invariant hold
/// exactly: settling once over eight hours walks precisely the same grid cells as settling
/// 480 times over one minute each, so both produce identical state. A step size computed
/// from the elapsed span would produce different cell boundaries in the two cases and the
/// results would diverge.</para>
/// <para><b>Why deferred commit.</b> Within a step, outputs accumulate in a buffer and are
/// added to the inventory only at the end. Without this a consumer could use, in the very
/// same step, goods its upstream producer had not yet made — a small but real way for the
/// economy to create value from nothing.</para>
/// <para><b>Why it is fast.</b> Once every producing building is halted, no construction is
/// pending, and a full step yields nothing, the state is a fixed point: no further step can
/// change anything until the player acts. Settlement stops there.</para>
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

            // Completions run first so a building that finishes at this boundary produces for
            // the rest of the step rather than idling until the next one.
            var anyCompleted = CompleteFinishedWork(city, stepEnd, completed);

            var deltaMs = (long)(stepEnd - cursor).TotalMilliseconds;
            var anyProduction = AdvanceStep(city, producers, deltaMs, produced);

            cursor = stepEnd;
            steps++;

            if (IsFixedPoint(city, producers, anyProduction, anyCompleted, now))
            {
                break;
            }
        }

        city.LastSettledAt = now;

        return new SettlementResult(now, steps, produced, completed);
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
            // before the production pass decides whether anything is full.
            city.RecomputeCapacity();
        }

        return any;
    }

    /// <summary>
    /// True when no further step could change anything.
    /// </summary>
    /// <remarks>
    /// Pending construction is part of the test. Without it, a city whose buildings are all
    /// halted would exit early and skip past a build that was due to complete — the build
    /// would land on the next read instead of this one.
    /// </remarks>
    private static bool IsFixedPoint(
        City city,
        IReadOnlyList<BuildingInstance> producers,
        bool anyProduction,
        bool anyCompleted,
        DateTimeOffset now)
    {
        if (anyProduction || anyCompleted)
        {
            return false;
        }

        if (city.Buildings.Any(b => b.IsUnderConstruction && b.CompletesAtUtc <= now))
        {
            return false;
        }

        return producers.All(b => b.IsUnderConstruction || b.HaltReason != HaltReason.None);
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
        // Outputs land here and are committed after every building has been resolved.
        var pending = new Dictionary<ResourceId, long>();
        var anyProduction = false;

        foreach (var building in producers)
        {
            // A half-built factory produces nothing.
            if (building.IsUnderConstruction)
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

            var byCapacity = long.MaxValue;
            foreach (var output in recipe.Outputs)
            {
                pending.TryGetValue(output.Resource, out var reserved);
                var free = Math.Max(0, city.Inventory.FreeSpace(output.Resource) - reserved);
                byCapacity = Math.Min(byCapacity, free / output.Quantity);
            }

            var cycles = Math.Min(byTime, Math.Min(byInput, byCapacity));

            if (cycles > 0)
            {
                // Inputs are consumed immediately so that two buildings competing for the
                // same input in one step cannot both spend it.
                foreach (var input in recipe.Inputs)
                {
                    city.Inventory.Remove(input.Resource, input.Quantity * cycles);
                }

                foreach (var output in recipe.Outputs)
                {
                    var quantity = output.Quantity * cycles;
                    pending[output.Resource] = pending.GetValueOrDefault(output.Resource) + quantity;
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

        foreach (var (resource, quantity) in pending)
        {
            city.Inventory.Add(resource, quantity);
        }

        return anyProduction;
    }
}

/// <summary>What a settlement pass did. Drives the "while you were away" recap.</summary>
public sealed record SettlementResult(
    DateTimeOffset SettledAt,
    int StepsRun,
    IReadOnlyDictionary<ResourceId, long> Produced,
    IReadOnlyList<string> CompletedBuildings)
{
    public static SettlementResult Empty(DateTimeOffset at) =>
        new(at, 0, new Dictionary<ResourceId, long>(), []);

    public bool ProducedAnything => Produced.Count > 0;
}
