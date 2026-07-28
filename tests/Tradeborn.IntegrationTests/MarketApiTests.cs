using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tradeborn.Infrastructure.Persistence;
using Xunit;

namespace Tradeborn.IntegrationTests;

/// <summary>
/// The sale is the step that closes the core loop, and the one most worth attacking: it is
/// where goods become money.
/// </summary>
public sealed class MarketApiTests(TradebornAppFactory factory) : IClassFixture<TradebornAppFactory>
{
    [RequiresPostgresFact]
    public async Task The_board_lists_every_resource_at_its_base_price()
    {
        await factory.ResetAsync();
        var client = await AuthenticatedClientAsync("trader@example.com");

        var board = await client.GetFromJsonAsync<BoardDto>("/api/market");

        Assert.NotNull(board);
        Assert.Equal(5, board!.Quotes.Count);
        Assert.Equal(200, board.Quotes.Single(q => q.Resource == "wood").SellPriceCent);
        Assert.Equal(6_000, board.Quotes.Single(q => q.Resource == "bread").SellPriceCent);

        // A fresh Market building allows 200 units per order.
        Assert.Equal(200, board.OrderLimit);
        Assert.Equal(3, board.FeePercent);
    }

    [RequiresPostgresFact]
    public async Task Bread_is_listed_first_so_the_value_ladder_is_visible()
    {
        // PLAYER_JOURNEY.md 7:00 — the player has to *notice* that bread is worth far more
        // than planks. Sorting by value is what makes that discovery happen unprompted.
        await factory.ResetAsync();
        var client = await AuthenticatedClientAsync("browser@example.com");

        var board = await client.GetFromJsonAsync<BoardDto>("/api/market");

        Assert.Equal("bread", board!.Quotes[0].Resource);
    }

    [RequiresPostgresFact]
    public async Task Selling_pays_the_player_and_moves_the_price()
    {
        await factory.ResetAsync();
        var client = await AuthenticatedClientAsync("seller@example.com");

        // A new city starts with 80 wood at 2 coins each.
        var response = await SellAsync(client, "wood", 60);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SellDto>();

        Assert.True(body!.Accepted);
        Assert.Equal(60, body.QuantitySold);
        Assert.Equal(200, body.UnitPriceCent);
        Assert.Equal(12_000, body.GrossCent);
        Assert.Equal(360, body.FeeCent);           // 3%
        Assert.Equal(11_640, body.NetCent);
        Assert.Equal(916, body.BalanceCoins);      // 800 + 116 (net floors to whole coins)

        // impact = (60 / 500) x 0.5 = 0.06 -> 200 x 0.94 = 188
        Assert.Equal(188, body.NewSellPriceCent);

        Assert.Contains(body.Resources, r => r.Resource == "wood" && r.Quantity == 20);
    }

    [RequiresPostgresFact]
    public async Task A_sale_awards_xp_and_can_raise_the_player_level()
    {
        await factory.ResetAsync();
        var client = await AuthenticatedClientAsync("earner@example.com");

        var body = await (await SellAsync(client, "wood", 60)).Content.ReadFromJsonAsync<SellDto>();

        // 1 XP per 20 coins of net proceeds: 116 / 20 = 5.
        Assert.Equal(5, body!.XpGained);
        Assert.Equal(1, body.PlayerLevel);
        Assert.Equal(5, body.PlayerXp);
        Assert.Equal(95, body.XpToNextLevel); // 100 needed to leave level 1
    }

    [RequiresPostgresFact]
    public async Task The_price_a_client_supplies_is_ignored()
    {
        // SECURITY_MODEL.md T2. The request shape has no price field at all, so a forged one
        // is not merely rejected — there is nothing for it to override.
        await factory.ResetAsync();
        var client = await AuthenticatedClientAsync("forger@example.com");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/market/sell")
        {
            Content = JsonContent.Create(new
            {
                resource = "wood",
                quantity = 10,
                unitPriceCent = 999_999,
                netCent = 999_999,
            }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var body = await (await client.SendAsync(request)).Content.ReadFromJsonAsync<SellDto>();

        Assert.True(body!.Accepted);
        Assert.Equal(200, body.UnitPriceCent);  // the real price, not the forged one
        Assert.Equal(819, body.BalanceCoins);   // 800 + 19, not 800 + a fortune
    }

    [RequiresPostgresFact]
    public async Task Replaying_a_sale_pays_only_once()
    {
        // SECURITY_MODEL.md T3.
        await factory.ResetAsync();
        var client = await AuthenticatedClientAsync("retry@example.com");

        var key = Guid.NewGuid().ToString();
        var first = await (await SellAsync(client, "wood", 40, key)).Content.ReadFromJsonAsync<SellDto>();
        var second = await (await SellAsync(client, "wood", 40, key)).Content.ReadFromJsonAsync<SellDto>();

        Assert.Equal(first!.BalanceCoins, second!.BalanceCoins);
        await AssertWoodAsync(expected: 40);
    }

    [RequiresPostgresFact]
    public async Task Concurrent_sales_cannot_sell_the_same_goods_twice()
    {
        // SECURITY_MODEL.md T4. Twenty parallel orders for 60 wood each against a stock of 80:
        // exactly one can succeed, and the balance must reflect exactly one sale.
        await factory.ResetAsync();
        var client = await AuthenticatedClientAsync("racer@example.com");

        var attempts = Enumerable.Range(0, 20)
            .Select(_ => SellAsync(client, "wood", 60, Guid.NewGuid().ToString()))
            .ToArray();

        var responses = await Task.WhenAll(attempts);

        var accepted = 0;
        foreach (var response in responses)
        {
            var body = await response.Content.ReadFromJsonAsync<SellDto>();
            if (body!.Accepted)
            {
                accepted++;
            }
        }

        Assert.Equal(1, accepted);
        await AssertWoodAsync(expected: 20);
    }

    [RequiresPostgresFact]
    public async Task Selling_more_than_is_held_is_refused_without_charging()
    {
        await factory.ResetAsync();
        var client = await AuthenticatedClientAsync("greedy@example.com");

        var response = await SellAsync(client, "wood", 150);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SellDto>();

        Assert.False(body!.Accepted);
        Assert.Equal("NotEnoughGoods", body.RefusalCode);
        Assert.Equal(800, body.BalanceCoins);
        await AssertWoodAsync(expected: 80);
    }

    [RequiresPostgresFact]
    public async Task Selling_beyond_the_order_limit_is_refused()
    {
        await factory.ResetAsync();
        var client = await AuthenticatedClientAsync("bulk@example.com");

        var body = await (await SellAsync(client, "wood", 500)).Content.ReadFromJsonAsync<SellDto>();

        Assert.False(body!.Accepted);
        Assert.Equal("ExceedsOrderLimit", body.RefusalCode);
    }

    [RequiresPostgresFact]
    public async Task A_sale_is_written_to_the_audit_ledger()
    {
        await factory.ResetAsync();
        var client = await AuthenticatedClientAsync("audited@example.com");

        await SellAsync(client, "wood", 60);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TradebornDbContext>();

        var entry = await db.AuditLedger.AsNoTracking().SingleAsync(e => e.Kind == "market.sold");

        Assert.Equal(11_640, entry.MoneyDeltaCent);
        Assert.Contains("wood", entry.ResourceDeltas, StringComparison.Ordinal);
        Assert.NotNull(entry.IdempotencyKey);
    }

    [RequiresPostgresFact]
    public async Task The_price_a_player_moves_is_visible_to_the_next_reader()
    {
        // The market is global state, so one player's dumping is another player's problem.
        await factory.ResetAsync();

        var alice = await AuthenticatedClientAsync("alice.market@example.com");
        var bob = await AuthenticatedClientAsync("bob.market@example.com");

        await SellAsync(alice, "wood", 80);

        var board = await bob.GetFromJsonAsync<BoardDto>("/api/market");

        Assert.True(
            board!.Quotes.Single(q => q.Resource == "wood").SellPriceCent < 200,
            "Alice's sale should have moved the price Bob sees.");
    }

    // -- helpers ---------------------------------------------------------------------------

    private async Task<HttpClient> AuthenticatedClientAsync(string email)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = "correct horse battery",
            displayName = "Trader",
        });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<AuthDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.AccessToken);
        return client;
    }

    private static Task<HttpResponseMessage> SellAsync(
        HttpClient client,
        string resource,
        long quantity,
        string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/market/sell")
        {
            Content = JsonContent.Create(new { resource, quantity }),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString());
        return client.SendAsync(request);
    }

    private async Task AssertWoodAsync(long expected)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TradebornDbContext>();

        var wood = await db.CityInventory.AsNoTracking().SingleAsync(i => i.ResourceId == "wood");
        Assert.Equal(expected, wood.Quantity);
    }

    private sealed record AuthDto(string AccessToken, Guid PlayerId);

    private sealed record BoardDto(
        DateTimeOffset ServerTimeUtc,
        long OrderLimit,
        long FeePercent,
        List<QuoteDto> Quotes);

    private sealed record QuoteDto(
        string Resource,
        string Tier,
        long SellPriceCent,
        long BuyPriceCent,
        long BasePriceCent,
        long Held);

    private sealed record SellDto(
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
        List<ResourceDto> Resources,
        long XpGained,
        int PlayerLevel,
        long PlayerXp,
        long XpToNextLevel,
        int LevelsGained,
        long NewSellPriceCent);

    private sealed record ResourceDto(string Resource, long Quantity, long Capacity);
}
