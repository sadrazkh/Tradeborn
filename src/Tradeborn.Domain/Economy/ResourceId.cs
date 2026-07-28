namespace Tradeborn.Domain.Economy;

/// <summary>
/// A resource identifier such as <c>wood</c> or <c>planks</c>.
/// </summary>
/// <remarks>
/// A wrapper rather than a bare <see cref="string"/> so that a resource id can never be
/// passed where a building id was expected — the classic "wrong string" defect. Values come
/// from seed data (docs/economy/RESOURCE_GRAPH.md §4), never from user input.
/// </remarks>
public readonly record struct ResourceId(string Value)
{
    public static ResourceId From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Resource id must not be empty.", nameof(value));
        }

        return new ResourceId(value);
    }

    public override string ToString() => Value;
}

/// <summary>A quantity of a resource. Always a whole number of units — there is no half a plank.</summary>
public readonly record struct ResourceAmount(ResourceId Resource, long Quantity)
{
    public static ResourceAmount Of(string resource, long quantity)
    {
        if (quantity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Resource quantities are never negative.");
        }

        return new ResourceAmount(ResourceId.From(resource), quantity);
    }

    public ResourceAmount Times(long factor) => this with { Quantity = checked(Quantity * factor) };

    public override string ToString() => $"{Quantity} {Resource}";
}
