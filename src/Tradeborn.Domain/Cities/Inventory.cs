using Tradeborn.Domain.Economy;

namespace Tradeborn.Domain.Cities;

/// <summary>
/// Per-resource balances with a shared per-resource capacity.
/// </summary>
/// <remarks>
/// Production halts at capacity — it never overflows and never destroys goods
/// (docs/economy/ECONOMY_DESIGN.md §8). A full warehouse is a design signal, not a penalty.
/// </remarks>
public sealed class Inventory
{
    private readonly Dictionary<ResourceId, long> balances = [];

    public long CapacityPerResource { get; internal set; }

    public long Get(ResourceId resource) =>
        balances.TryGetValue(resource, out var value) ? value : 0;

    public long FreeSpace(ResourceId resource) => Math.Max(0, CapacityPerResource - Get(resource));

    public bool Has(ResourceAmount amount) => Get(amount.Resource) >= amount.Quantity;

    public void Add(ResourceId resource, long quantity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(quantity);

        // Clamped rather than checked: settlement computes capacity before producing, so a
        // clamp here should be unreachable. It exists so that a bug upstream cannot create
        // a balance above capacity, which would then be impossible to spend down correctly.
        balances[resource] = Math.Min(CapacityPerResource, Get(resource) + quantity);
    }

    public void Remove(ResourceId resource, long quantity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(quantity);

        var next = Get(resource) - quantity;
        if (next < 0)
        {
            throw new InvalidOperationException(
                $"Cannot remove {quantity} of '{resource}': only {Get(resource)} held.");
        }

        balances[resource] = next;
    }

    public void Set(ResourceId resource, long quantity) =>
        balances[resource] = Math.Clamp(quantity, 0, CapacityPerResource);

    public IReadOnlyDictionary<ResourceId, long> Snapshot() => new Dictionary<ResourceId, long>(balances);
}
