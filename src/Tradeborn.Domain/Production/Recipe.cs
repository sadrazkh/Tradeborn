using Tradeborn.Domain.Economy;

namespace Tradeborn.Domain.Production;

/// <summary>
/// A production recipe: inputs consumed and outputs produced per completed cycle.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TopologicalRank"/> orders recipes so that producers are always resolved
/// before their consumers within a settlement step. It is precomputed from the recipe graph
/// at seed time. The graph is acyclic by invariant (docs/economy/RESOURCE_GRAPH.md §2) —
/// a cycle would make settlement non-terminating, so a unit test rejects one.
/// </para>
/// </remarks>
public sealed class Recipe
{
    public Recipe(
        string id,
        long cycleMilliseconds,
        IReadOnlyList<ResourceAmount> inputs,
        IReadOnlyList<ResourceAmount> outputs,
        int topologicalRank)
    {
        if (cycleMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cycleMilliseconds), "Cycle length must be positive.");
        }

        if (outputs.Count == 0)
        {
            throw new ArgumentException("A recipe must produce something.", nameof(outputs));
        }

        Id = id;
        CycleMilliseconds = cycleMilliseconds;
        Inputs = inputs;
        Outputs = outputs;
        TopologicalRank = topologicalRank;
    }

    public string Id { get; }

    /// <summary>Base cycle length at level 1.</summary>
    public long CycleMilliseconds { get; }

    public IReadOnlyList<ResourceAmount> Inputs { get; }
    public IReadOnlyList<ResourceAmount> Outputs { get; }

    /// <summary>Lower ranks are resolved first. Extractors are rank 0.</summary>
    public int TopologicalRank { get; }

    /// <summary>
    /// Cycle length at a given building level.
    /// </summary>
    /// <remarks>
    /// Upgrades scale the <b>cycle time down</b> rather than scaling output quantity up.
    /// Both express the same rate, but scaling quantity would floor a fractional result —
    /// a level-3 building producing 1 unit per cycle would silently lose ~25 % of its
    /// output. Shortening the cycle has no such rounding loss and reproduces the published
    /// rates exactly: 30 000 ms / 1.6 = 18 750 ms = 192 units/hour at level 2, matching
    /// docs/economy/ECONOMY_DESIGN.md §5.
    /// </remarks>
    public long CycleMillisecondsAtLevel(int level)
    {
        var factor = UpgradeCurve.PowForLevel(UpgradeCurve.OutputFactor, level);
        return Math.Max(1, (long)(CycleMilliseconds / factor));
    }
}

/// <summary>Upgrade scaling constants from docs/economy/ECONOMY_DESIGN.md §5.</summary>
/// <remarks>
/// <c>decimal</c>, not <c>double</c>. These multipliers feed every cost and rate in the game,
/// and `decimal` represents 1.6 and 2.5 exactly where binary floating point does not. The
/// architecture tests reject floating point anywhere in the economy domain for this reason.
/// </remarks>
public static class UpgradeCurve
{
    public const decimal OutputFactor = 1.6m;
    public const decimal CostFactor = 2.5m;
    public const decimal TimeFactor = 3.0m;
    public const int MaxLevel = 3;

    /// <summary>
    /// <paramref name="factor"/> raised to <c>level - 1</c>, by exact repeated multiplication.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="Math.Pow"/>: that returns a <see cref="double"/> and would
    /// reintroduce binary rounding into every cost in the game. Levels are capped at 3, so a
    /// loop is both exact and cheaper than a transcendental call.
    /// </remarks>
    public static decimal PowForLevel(decimal factor, int level)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);

        var result = 1m;
        for (var i = 1; i < level; i++)
        {
            result *= factor;
        }

        return result;
    }
}
