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
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);

        var factor = Math.Pow(UpgradeCurve.OutputFactor, level - 1);
        return Math.Max(1, (long)(CycleMilliseconds / factor));
    }
}

/// <summary>Upgrade scaling constants from docs/economy/ECONOMY_DESIGN.md §5.</summary>
public static class UpgradeCurve
{
    public const double OutputFactor = 1.6;
    public const double CostFactor = 2.5;
    public const double TimeFactor = 3.0;
    public const int MaxLevel = 3;
}
