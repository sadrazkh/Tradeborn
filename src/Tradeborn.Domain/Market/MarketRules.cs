using Tradeborn.Domain.Cities;
using Tradeborn.Domain.Common;

namespace Tradeborn.Domain.Market;

/// <summary>Why a trade was refused.</summary>
public enum TradeRefusal
{
    None = 0,
    UnknownResource,
    QuantityMustBePositive,
    NotEnoughGoods,
    ExceedsOrderLimit,
    NoMarket,
    PriceMoved,
}

public readonly record struct TradeCheck(TradeRefusal Refusal)
{
    public static TradeCheck Allowed => new(TradeRefusal.None);
    public static TradeCheck Refused(TradeRefusal reason) => new(reason);

    public bool IsAllowed => Refusal == TradeRefusal.None;
}

/// <summary>
/// What a sale is worth, broken down so the player can see where the money went.
/// </summary>
/// <remarks>
/// The fee is shown, never silently deducted. A player who sells 60 wood for 120 coins and
/// receives 116 must be able to see why, or the game looks like it is cheating them.
/// </remarks>
public sealed record SaleQuote(
    Money UnitPrice,
    long Quantity,
    Money Gross,
    Money Fee,
    Money Net)
{
    public static SaleQuote For(Money unitPrice, long quantity)
    {
        var gross = unitPrice * quantity;

        // Integer division floors, so the fee rounds in the player's favour rather than
        // against them. Over thousands of sales that difference is invisible; getting the
        // direction wrong is the kind of thing players notice and resent.
        var fee = Money.FromCent(gross.Cent * MarketTuning.TransactionFeePercent / 100);

        return new SaleQuote(unitPrice, quantity, gross, fee, gross - fee);
    }
}

/// <summary>
/// The single authority on whether a trade is legal.
/// </summary>
/// <remarks>
/// Pure functions over already-settled state, matching <c>ConstructionRules</c> and
/// <c>ProductionRules</c>. The client shows a projected total, but this decides
/// (SECURITY_MODEL.md §3) — and the client never sends a price at all.
/// </remarks>
public static class MarketRules
{
    /// <summary>
    /// Units the player may sell in one order.
    /// </summary>
    /// <remarks>
    /// Scales with the Market building's level, so a bigger market is worth building. It also
    /// caps how far a single order can move the price, which stops one player crashing a
    /// resource to the floor in a single click (SECURITY_MODEL.md T10).
    /// </remarks>
    public static long OrderLimit(City city)
    {
        var market = city.Buildings.FirstOrDefault(b =>
            string.Equals(b.Definition.Id, "market", StringComparison.Ordinal));

        return market is null ? 0 : market.Level * MarketTuning.VolumeCapPerMarketLevel;
    }

    public static TradeCheck CanSell(City city, MarketPrice? price, long quantity)
    {
        if (price is null)
        {
            return TradeCheck.Refused(TradeRefusal.UnknownResource);
        }

        if (quantity <= 0)
        {
            return TradeCheck.Refused(TradeRefusal.QuantityMustBePositive);
        }

        var limit = OrderLimit(city);
        if (limit <= 0)
        {
            return TradeCheck.Refused(TradeRefusal.NoMarket);
        }

        if (quantity > limit)
        {
            return TradeCheck.Refused(TradeRefusal.ExceedsOrderLimit);
        }

        // Only goods actually in storage can be sold. Anything still on a cart belongs to
        // logistics, not to the player's purse (GDD §3.5).
        if (city.Inventory.Get(price.Resource) < quantity)
        {
            return TradeCheck.Refused(TradeRefusal.NotEnoughGoods);
        }

        return TradeCheck.Allowed;
    }
}
