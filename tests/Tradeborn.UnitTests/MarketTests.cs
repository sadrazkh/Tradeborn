using Tradeborn.Domain.Common;
using Tradeborn.Domain.Economy;
using Tradeborn.Domain.Market;

namespace Tradeborn.UnitTests;

public class MarketTests
{
    private static readonly ResourceId Wood = ResourceId.From("wood");
    private static readonly ResourceId Bread = ResourceId.From("bread");

    private static readonly DateTimeOffset Now = SliceEconomy.Epoch;

    /// <summary>Wood: 2 coins base, depth 500 (docs/economy/RESOURCE_GRAPH.md §4).</summary>
    private static MarketPrice WoodPrice() =>
        MarketPrice.AtBase(Wood, Money.FromCoins(2), marketDepth: 500, Now);

    /// <summary>Bread: 60 coins base, depth 150 — shallow but rich.</summary>
    private static MarketPrice BreadPrice() =>
        MarketPrice.AtBase(Bread, Money.FromCoins(60), marketDepth: 150, Now);

    // -----------------------------------------------------------------------------------
    // Published prices
    // -----------------------------------------------------------------------------------

    [Fact]
    public void An_untraded_market_sits_exactly_at_base()
    {
        Assert.Equal(Money.FromCoins(2), WoodPrice().SellPriceAt(Now));
        Assert.Equal(Money.FromCoins(60), BreadPrice().SellPriceAt(Now));
    }

    [Fact]
    public void The_buy_price_is_always_above_the_sell_price()
    {
        var price = WoodPrice();

        Assert.True(price.BuyPriceAt(Now) > price.SellPriceAt(Now));
        Assert.Equal(250, price.BuyPriceAt(Now).Cent); // 2 coins x 1.25
    }

    // -----------------------------------------------------------------------------------
    // Elasticity — selling moves the price
    // -----------------------------------------------------------------------------------

    [Fact]
    public void Selling_pushes_the_price_down()
    {
        var price = WoodPrice();
        var before = price.SellPriceAt(Now);

        price.ApplySale(100, Now);

        Assert.True(price.SellPriceAt(Now) < before, "Dumping stock must move the price.");
    }

    [Fact]
    public void Price_impact_follows_the_documented_formula()
    {
        // impact = (volume / depth) x elasticity = (100 / 500) x 0.5 = 0.10
        // 200 cent x (1 - 0.10) = 180 cent
        var price = WoodPrice();

        price.ApplySale(100, Now);

        Assert.Equal(180, price.SellPriceAt(Now).Cent);
    }

    [Fact]
    public void A_shallow_market_moves_further_for_the_same_volume()
    {
        // Bread's depth is 150 against wood's 500, so the same order hurts bread more in
        // percentage terms. That, plus bread's higher value, is what pushes players up the
        // production chain (BALANCE_ASSUMPTIONS A-3).
        var wood = WoodPrice();
        var bread = BreadPrice();

        wood.ApplySale(75, Now);
        bread.ApplySale(75, Now);

        var woodDrop = 1m - ((decimal)wood.SellPriceAt(Now).Cent / Money.FromCoins(2).Cent);
        var breadDrop = 1m - ((decimal)bread.SellPriceAt(Now).Cent / Money.FromCoins(60).Cent);

        Assert.True(breadDrop > woodDrop, $"Bread {breadDrop:P1} should drop more than wood {woodDrop:P1}.");
    }

    [Fact]
    public void The_price_never_falls_below_the_floor()
    {
        var price = WoodPrice();

        // Twenty maximum-impact sales in a row.
        for (var i = 0; i < 20; i++)
        {
            price.ApplySale(500, Now);
        }

        Assert.Equal(price.Floor, price.SellPriceAt(Now));
        Assert.Equal(80, price.Floor.Cent); // 0.4 x 200
    }

    // -----------------------------------------------------------------------------------
    // Mean reversion — computed lazily, never ticked
    // -----------------------------------------------------------------------------------

    [Fact]
    public void The_price_recovers_toward_base_over_time()
    {
        var price = WoodPrice();
        price.ApplySale(200, Now);

        var depressed = price.SellPriceAt(Now);
        var later = price.SellPriceAt(Now.AddMinutes(35));

        Assert.True(later > depressed, "The market should heal while nobody is selling.");
        Assert.True(later < Money.FromCoins(2), "35 minutes is a half-life, not a full reset.");
    }

    [Fact]
    public void The_price_returns_to_base_after_a_long_absence()
    {
        var price = WoodPrice();
        price.ApplySale(500, Now);

        // A player returning the next day must not find a market still punishing them.
        Assert.Equal(Money.FromCoins(2), price.SellPriceAt(Now.AddDays(1)));
    }

    [Fact]
    public void Recovery_is_a_pure_function_of_elapsed_time()
    {
        // No ticking job: asking twice for the same instant gives the same answer, and asking
        // for a later instant does not depend on having asked for the earlier one.
        var a = WoodPrice();
        var b = WoodPrice();

        a.ApplySale(200, Now);
        b.ApplySale(200, Now);

        _ = a.SellPriceAt(Now.AddMinutes(5));
        _ = a.SellPriceAt(Now.AddMinutes(10));

        Assert.Equal(b.SellPriceAt(Now.AddMinutes(20)), a.SellPriceAt(Now.AddMinutes(20)));
    }

    // -----------------------------------------------------------------------------------
    // Fees
    // -----------------------------------------------------------------------------------

    [Fact]
    public void A_sale_quote_shows_gross_fee_and_net_separately()
    {
        // 60 wood at 2 coins = 120 coins gross, 3% fee = 3.60, net 116.40.
        var quote = SaleQuote.For(Money.FromCoins(2), 60);

        Assert.Equal(12_000, quote.Gross.Cent);
        Assert.Equal(360, quote.Fee.Cent);
        Assert.Equal(11_640, quote.Net.Cent);
    }

    [Fact]
    public void The_fee_rounds_in_the_players_favour()
    {
        // 1 coin gross: 3% is 3 cent exactly. 1 cent gross: 3% floors to 0, not up to 1.
        Assert.Equal(0, SaleQuote.For(Money.FromCent(1), 1).Fee.Cent);
        Assert.Equal(Money.FromCent(1), SaleQuote.For(Money.FromCent(1), 1).Net);
    }

    // -----------------------------------------------------------------------------------
    // The invariant that makes arbitrage impossible (SECURITY_MODEL.md T10)
    // -----------------------------------------------------------------------------------

    [Fact]
    public void Buying_and_immediately_selling_always_loses_money()
    {
        var price = WoodPrice();

        var bought = price.BuyPriceAt(Now) * 100;
        var soldBack = SaleQuote.For(price.SellPriceAt(Now), 100).Net;

        Assert.True(soldBack < bought, "A round trip must never be profitable.");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(50)]
    [InlineData(500)]
    public void No_sequence_of_trades_profits_without_production(int volume)
    {
        // A property test over the round trip at several volumes and after recovery. The
        // spread is arithmetic, not a rate limit — there is no waiting period that makes this
        // work, which is why arbitrage is structurally impossible rather than merely awkward.
        var price = WoodPrice();
        var balance = Money.FromCoins(10_000);
        var held = 0L;

        for (var round = 0; round < 10; round++)
        {
            var at = Now.AddMinutes(round * 20);

            var cost = price.BuyPriceAt(at) * volume;
            if (!balance.CanAfford(cost))
            {
                break;
            }

            balance = balance.Debit(cost);
            held += volume;

            var quote = SaleQuote.For(price.SellPriceAt(at), held);
            balance += quote.Net;
            price.ApplySale(held, at);
            held = 0;
        }

        Assert.True(
            balance < Money.FromCoins(10_000),
            $"Trading alone produced a profit: ended with {balance}, started with 10000.");
    }

    // -----------------------------------------------------------------------------------
    // Order limits
    // -----------------------------------------------------------------------------------

    [Fact]
    public void The_order_limit_scales_with_the_market_building()
    {
        var city = SliceEconomy.NewPlayerCity();

        Assert.Equal(200, MarketRules.OrderLimit(city));
    }

    [Fact]
    public void A_city_without_a_market_cannot_sell()
    {
        var city = SliceEconomy.WithBuildings((SliceEconomy.LumberCamp, 1));

        var check = MarketRules.CanSell(city, WoodPrice(), 10);

        Assert.Equal(TradeRefusal.NoMarket, check.Refusal);
    }

    [Fact]
    public void Selling_more_than_is_held_is_refused()
    {
        var city = SliceEconomy.NewPlayerCity(); // 80 wood

        var check = MarketRules.CanSell(city, WoodPrice(), 100);

        Assert.Equal(TradeRefusal.NotEnoughGoods, check.Refusal);
    }

    [Fact]
    public void Selling_beyond_the_order_limit_is_refused()
    {
        var city = SliceEconomy.NewPlayerCity();
        city.Inventory.Add(Wood, 500);

        var check = MarketRules.CanSell(city, WoodPrice(), 400);

        Assert.Equal(TradeRefusal.ExceedsOrderLimit, check.Refusal);
    }

    [Fact]
    public void A_valid_sale_is_allowed()
    {
        var city = SliceEconomy.NewPlayerCity();

        Assert.True(MarketRules.CanSell(city, WoodPrice(), 60).IsAllowed);
    }

    [Fact]
    public void Goods_still_on_a_cart_cannot_be_sold()
    {
        // Only what is in storage is the player's to sell. This is the same rule that stops a
        // sawmill consuming wood in transit.
        var city = SliceEconomy.NewPlayerCity();
        city.Inventory.Remove(Wood, 80);

        var check = MarketRules.CanSell(city, WoodPrice(), 1);

        Assert.Equal(TradeRefusal.NotEnoughGoods, check.Refusal);
    }
}
