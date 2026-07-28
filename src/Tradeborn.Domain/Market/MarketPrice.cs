using Tradeborn.Domain.Common;
using Tradeborn.Domain.Economy;

namespace Tradeborn.Domain.Market;

/// <summary>Market tuning from docs/economy/ECONOMY_DESIGN.md §7.</summary>
/// <remarks>
/// <c>decimal</c> throughout. These multiply every price in the game, and binary floating
/// point cannot represent 0.5, 1.25 or 0.02 exactly — the architecture tests reject doubles
/// in the economy for exactly this reason.
/// </remarks>
public static class MarketTuning
{
    /// <summary>How hard a sale pushes the price down, relative to market depth.</summary>
    public const decimal Elasticity = 0.5m;

    /// <summary>Fraction of the gap to base recovered each minute. ~35 min half-life.</summary>
    public const decimal RecoveryPerMinute = 0.02m;

    public const decimal PriceFloorFactor = 0.4m;
    public const decimal PriceCeilingFactor = 1.6m;

    /// <summary>
    /// What the NPC charges relative to what it pays.
    /// </summary>
    /// <remarks>
    /// The single most important number in this file. A round trip loses 20 % before the fee,
    /// so no sequence of NPC trades is profitable — arbitrage is <b>arithmetically</b>
    /// impossible rather than merely rate-limited (SECURITY_MODEL.md T10). A property test
    /// asserts it against random trade sequences.
    /// </remarks>
    public const decimal BuySellSpread = 1.25m;

    /// <summary>Percent taken from sale proceeds. Discourages micro-churn.</summary>
    public const long TransactionFeePercent = 3;

    /// <summary>Units sellable in one order, per level of the Market building.</summary>
    public const long VolumeCapPerMarketLevel = 200;

    /// <summary>
    /// Beyond this the decay factor has underflowed to nothing and the price is simply base.
    /// </summary>
    /// <remarks>
    /// Guards the exponentiation below from doing pointless work for a player who has been
    /// away for a month; 0.98^2000 is already far below decimal's smallest positive value.
    /// </remarks>
    private const int MaxRecoveryMinutes = 2_000;

    /// <summary>
    /// <paramref name="value"/> raised to <paramref name="exponent"/>, by binary exponentiation.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="Math.Pow"/>: that returns a <see cref="double"/> and would
    /// reintroduce binary rounding into every price. Repeated squaring keeps it exact-ish and
    /// O(log n), and decimal underflowing to zero is the correct answer here — a price that has
    /// had thousands of minutes to recover has fully recovered.
    /// </remarks>
    public static decimal Pow(decimal value, int exponent)
    {
        if (exponent <= 0)
        {
            return 1m;
        }

        if (exponent > MaxRecoveryMinutes)
        {
            return 0m;
        }

        var result = 1m;
        var factor = value;
        var remaining = exponent;

        while (remaining > 0)
        {
            if ((remaining & 1) == 1)
            {
                result *= factor;
            }

            remaining >>= 1;
            if (remaining > 0)
            {
                factor *= factor;
            }
        }

        return result;
    }
}

/// <summary>
/// The NPC market's price for one resource.
/// </summary>
/// <remarks>
/// <para>
/// The current price is a <b>pure function</b> of
/// <c>(basePrice, priceAtLastTrade, lastTradeAt, now)</c>. Nothing ticks; recovery is computed
/// on read, exactly like production settlement (REALTIME_AND_TIME_MODEL.md §2). A market with
/// no traders costs nothing to run.
/// </para>
/// <para>
/// Prices are <see cref="Money"/> — integer cent. A price of "2 coins" is 200 cent, so
/// elasticity has two decimal digits of room to move without rounding to nothing.
/// </para>
/// </remarks>
public sealed class MarketPrice
{
    public MarketPrice(
        ResourceId resource,
        Money basePrice,
        long marketDepth,
        Money priceAtLastTrade,
        DateTimeOffset lastTradeAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(marketDepth, 0);

        Resource = resource;
        BasePrice = basePrice;
        MarketDepth = marketDepth;
        PriceAtLastTrade = priceAtLastTrade;
        LastTradeAtUtc = lastTradeAtUtc;
    }

    /// <summary>A price that has never traded, sitting exactly at base.</summary>
    public static MarketPrice AtBase(
        ResourceId resource,
        Money basePrice,
        long marketDepth,
        DateTimeOffset now) =>
        new(resource, basePrice, marketDepth, basePrice, now);

    public ResourceId Resource { get; }
    public Money BasePrice { get; }
    public long MarketDepth { get; }
    public Money PriceAtLastTrade { get; private set; }
    public DateTimeOffset LastTradeAtUtc { get; private set; }

    public Money Floor => Money.FromCent((long)(BasePrice.Cent * MarketTuning.PriceFloorFactor));
    public Money Ceiling => Money.FromCent((long)(BasePrice.Cent * MarketTuning.PriceCeilingFactor));

    /// <summary>
    /// What the NPC pays per unit right now, after mean reversion.
    /// </summary>
    /// <remarks>
    /// <c>price(t) = base + (priceAtLastTrade - base) × (1 - recovery)^minutes</c>.
    /// Recovery is fast enough that a player returning next session finds a healed market — no
    /// lingering punishment for having sold — but slow enough that dumping stock inside one
    /// session genuinely hurts (BALANCE_ASSUMPTIONS A-9).
    /// </remarks>
    public Money SellPriceAt(DateTimeOffset now)
    {
        var minutes = (int)Math.Max(0, (now - LastTradeAtUtc).TotalMinutes);
        var decay = MarketTuning.Pow(1m - MarketTuning.RecoveryPerMinute, minutes);

        var gap = PriceAtLastTrade.Cent - BasePrice.Cent;
        var cent = BasePrice.Cent + (long)(gap * decay);

        return Money.FromCent(Math.Clamp(cent, Floor.Cent, Ceiling.Cent));
    }

    /// <summary>What the NPC charges per unit — always above what it pays.</summary>
    public Money BuyPriceAt(DateTimeOffset now) =>
        Money.FromCent((long)(SellPriceAt(now).Cent * MarketTuning.BuySellSpread));

    /// <summary>
    /// Pushes the price down after a sale.
    /// </summary>
    /// <remarks>
    /// <c>impact = (volume / depth) × elasticity</c>, applied to the price that was actually
    /// paid. Deep, cheap markets (raw goods) barely notice a large sale in percentage terms
    /// but are worth little; shallow, rich markets (bread) move more per unit sold. Both
    /// forces push the player up the production chain, which is the point.
    /// </remarks>
    public void ApplySale(long volume, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(volume);

        var priceBefore = SellPriceAt(now);
        var impact = (decimal)volume / MarketDepth * MarketTuning.Elasticity;
        var moved = (long)(priceBefore.Cent * (1m - impact));

        PriceAtLastTrade = Money.FromCent(Math.Clamp(moved, Floor.Cent, Ceiling.Cent));
        LastTradeAtUtc = now;
    }
}
