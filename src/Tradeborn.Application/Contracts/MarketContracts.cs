using Tradeborn.Domain.Market;

namespace Tradeborn.Application.Contracts;

/// <summary>
/// Request to sell goods to the NPC market.
/// </summary>
/// <remarks>
/// Intent only: what and how much. There is deliberately no price, no total and no expected
/// proceeds in this shape — the server computes all of them at commit time
/// (SECURITY_MODEL.md T2). A price the client could send is a price the client could forge.
/// </remarks>
public sealed record SellRequest(string Resource, long Quantity);

/// <summary>
/// One resource's market state.
/// </summary>
/// <remarks>
/// Prices are in <b>cent</b>, not coins. Elasticity moves wood from 200 to 180 cent — a
/// coins-only field would round that to "2" and the player would watch the price visibly move
/// on the chart while the number beside it never changed.
/// </remarks>
public sealed record MarketQuoteDto(
    string Resource,
    string Tier,
    long SellPriceCent,
    long BuyPriceCent,
    long BasePriceCent,
    long FloorCent,
    long CeilingCent,
    long Held,
    IReadOnlyList<PricePointDto> History);

public sealed record PricePointDto(DateTimeOffset AtUtc, long PriceCent);

public sealed record MarketBoardDto(
    DateTimeOffset ServerTimeUtc,
    long OrderLimit,
    long FeePercent,
    IReadOnlyList<MarketQuoteDto> Quotes);

/// <summary>The outcome of a sale.</summary>
public sealed record SellResponse(
    bool Accepted,
    string? RefusalCode,
    string? RefusalMessage,
    string Resource,
    long QuantitySold,
    long UnitPriceCent,
    long GrossCent,
    long FeeCent,
    long NetCent,
    long BalanceCoins,
    IReadOnlyList<ResourceBalanceDto> Resources,
    long XpGained,
    int PlayerLevel,
    long PlayerXp,
    long XpToNextLevel,
    int LevelsGained,
    long NewSellPriceCent,
    DateTimeOffset ServerTimeUtc)
{
    public static SellResponse Refused(
        TradeRefusal refusal,
        string resource,
        long balanceCoins,
        IReadOnlyList<ResourceBalanceDto> resources,
        int playerLevel,
        long playerXp,
        long xpToNext,
        DateTimeOffset now) =>
        new(false, refusal.ToString(), TradeRefusalMessages.For(refusal),
            resource, 0, 0, 0, 0, 0, balanceCoins, resources,
            0, playerLevel, playerXp, xpToNext, 0, 0, now);
}

public static class TradeRefusalMessages
{
    public static string For(TradeRefusal refusal) => refusal switch
    {
        TradeRefusal.UnknownResource => "That is not something the market trades.",
        TradeRefusal.QuantityMustBePositive => "Choose how much you want to sell.",
        TradeRefusal.NotEnoughGoods => "You do not have that much in storage.",
        TradeRefusal.ExceedsOrderLimit => "That is more than your Market can handle in one order.",
        TradeRefusal.NoMarket => "You need a Market to trade.",
        TradeRefusal.PriceMoved => "The price moved while you were deciding. Check it and try again.",
        _ => "That trade is not allowed right now.",
    };
}
