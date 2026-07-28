using Tradeborn.Domain.Economy;

namespace Tradeborn.Domain.Logistics;

/// <summary>
/// A load of goods travelling from a producer to central storage.
/// </summary>
/// <remarks>
/// <para>
/// Goods do not teleport (GDD §3.5). Production fills a building's local output buffer, and
/// only a completed journey moves them into the city's inventory where they can be spent.
/// </para>
/// <para>
/// The job is the <b>economy</b>; the vehicle on screen is only its portrait. Arrival is
/// decided by <see cref="ArrivesAtUtc"/> against the server clock, so a killed animation, a
/// backgrounded tab, or a dropped connection cannot cost the player a single plank.
/// </para>
/// </remarks>
public sealed class TransportJob
{
    public TransportJob(
        string id,
        string fromBuildingId,
        ResourceId resource,
        long quantity,
        DateTimeOffset departedAtUtc,
        DateTimeOffset arrivesAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(quantity, 0);

        Id = id;
        FromBuildingId = fromBuildingId;
        Resource = resource;
        Quantity = quantity;
        DepartedAtUtc = departedAtUtc;
        ArrivesAtUtc = arrivesAtUtc;
    }

    public string Id { get; }
    public string FromBuildingId { get; }
    public ResourceId Resource { get; }
    public long Quantity { get; private set; }
    public DateTimeOffset DepartedAtUtc { get; }
    public DateTimeOffset ArrivesAtUtc { get; }

    public bool HasArrivedBy(DateTimeOffset at) => ArrivesAtUtc <= at;

    /// <summary>Journey progress in 0..1, for interpolating the vehicle's position.</summary>
    public double Progress(DateTimeOffset now)
    {
        var total = (ArrivesAtUtc - DepartedAtUtc).TotalMilliseconds;
        if (total <= 0)
        {
            return 1;
        }

        return Math.Clamp((now - DepartedAtUtc).TotalMilliseconds / total, 0, 1);
    }

    /// <summary>Reduces the load after a partial delivery; the remainder goes back to the source.</summary>
    internal void Deliver(long delivered)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(delivered);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(delivered, Quantity);
        Quantity -= delivered;
    }
}

/// <summary>Tuning for the logistics layer.</summary>
/// <remarks>
/// Journeys are deliberately short and buffers generous. Transport exists to make the economy
/// <i>visible</i>, not to add a throughput constraint — the chain ratios in
/// docs/economy/ECONOMY_DESIGN.md §3 assume goods flow freely, and a transport bottleneck
/// would silently invalidate every balance number in that document.
/// </remarks>
public static class LogisticsTuning
{
    /// <summary>Units a producer can hold before it must wait for a pickup.</summary>
    public const long BufferCapacity = 20;

    /// <summary>Fixed dispatch overhead — loading the cart.</summary>
    public const long BaseTravelMilliseconds = 3_000;

    /// <summary>Added per plot of Manhattan distance to the delivery point.</summary>
    public const long TravelMillisecondsPerPlot = 1_500;

    public static long TravelMilliseconds(int distanceInPlots) =>
        BaseTravelMilliseconds + (Math.Max(0, distanceInPlots) * TravelMillisecondsPerPlot);
}
