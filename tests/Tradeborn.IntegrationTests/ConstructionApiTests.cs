using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tradeborn.Infrastructure.Persistence;
using Xunit;

namespace Tradeborn.IntegrationTests;

/// <summary>
/// The security properties Phase 3 has to prove: a player cannot be charged twice, and
/// cannot spend the same coins twice by racing requests.
/// </summary>
public sealed class ConstructionApiTests(TradebornAppFactory factory) : IClassFixture<TradebornAppFactory>
{
    [RequiresPostgresFact]
    public async Task Building_deducts_coins_and_materials_and_starts_construction()
    {
        await factory.ResetAsync();
        var client = await AuthenticatedClientAsync("builder@example.com");

        var response = await PlaceAsync(client, "lumber_camp", 1, 2);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ConstructionResponseDto>();

        Assert.True(body!.Accepted);
        Assert.Equal(650, body.BalanceCoins);                 // 800 - 150
        Assert.Equal("UnderConstruction", body.Building!.State);
        Assert.NotNull(body.Building.CompletesAtUtc);
        Assert.Contains(body.Resources, r => r.Resource == "wood" && r.Quantity == 60); // 80 - 20
    }

    [RequiresPostgresFact]
    public async Task Replaying_the_same_idempotency_key_charges_only_once()
    {
        // SECURITY_MODEL.md T3. A retry on a flaky mobile connection must never build twice.
        await factory.ResetAsync();
        var client = await AuthenticatedClientAsync("retry@example.com");

        var key = Guid.NewGuid().ToString();
        var first = await PlaceAsync(client, "lumber_camp", 1, 2, key);
        var second = await PlaceAsync(client, "lumber_camp", 1, 2, key);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var firstBody = await first.Content.ReadFromJsonAsync<ConstructionResponseDto>();
        var secondBody = await second.Content.ReadFromJsonAsync<ConstructionResponseDto>();

        // The replay returns the ORIGINAL response, not a fresh one.
        Assert.Equal(firstBody!.Building!.Id, secondBody!.Building!.Id);
        Assert.Equal(650, secondBody.BalanceCoins);

        await AssertBuildingCountAsync(expected: 1, definitionId: "lumber_camp");
    }

    [RequiresPostgresFact]
    public async Task Concurrent_builds_on_one_plot_produce_exactly_one_building()
    {
        // SECURITY_MODEL.md T4. Twenty parallel requests, each with its own idempotency key,
        // so idempotency cannot mask the race — this tests the row lock and the unique index.
        await factory.ResetAsync();
        var client = await AuthenticatedClientAsync("racer@example.com");

        var attempts = Enumerable.Range(0, 20)
            .Select(_ => PlaceAsync(client, "lumber_camp", 1, 2, Guid.NewGuid().ToString()))
            .ToArray();

        var responses = await Task.WhenAll(attempts);

        var accepted = 0;
        foreach (var response in responses)
        {
            if (response.StatusCode != HttpStatusCode.OK) continue;
            var body = await response.Content.ReadFromJsonAsync<ConstructionResponseDto>();
            if (body!.Accepted) accepted++;
        }

        Assert.Equal(1, accepted);
        await AssertBuildingCountAsync(expected: 1, definitionId: "lumber_camp");
        await AssertBalanceAsync(expected: 650);
    }

    [RequiresPostgresFact]
    public async Task Concurrent_builds_on_different_plots_cannot_overspend()
    {
        // The queue allows one build at a time, so twenty parallel requests across twenty
        // different plots must still yield exactly one — otherwise a player could drain
        // their balance below zero by racing.
        await factory.ResetAsync();
        var client = await AuthenticatedClientAsync("spender@example.com");

        var attempts = Enumerable.Range(0, 20)
            .Select(i => PlaceAsync(client, "lumber_camp", 1 + (i % 4), 2 + (i / 4 % 4), Guid.NewGuid().ToString()))
            .ToArray();

        await Task.WhenAll(attempts);

        await AssertBuildingCountAsync(expected: 1, definitionId: "lumber_camp");
        await AssertBalanceAsync(expected: 650);
    }

    [RequiresPostgresFact]
    public async Task A_command_without_an_idempotency_key_is_rejected()
    {
        await factory.ResetAsync();
        var client = await AuthenticatedClientAsync("nokey@example.com");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/cities/me/buildings")
        {
            Content = JsonContent.Create(new { definitionId = "lumber_camp", col = 1, row = 2 }),
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertBuildingCountAsync(expected: 0, definitionId: "lumber_camp");
    }

    [RequiresPostgresFact]
    public async Task Reusing_a_key_for_a_different_command_is_a_conflict()
    {
        // Replaying the first command's response would be actively wrong, so it is surfaced
        // rather than silently answered.
        await factory.ResetAsync();
        var client = await AuthenticatedClientAsync("mixed@example.com");

        var key = Guid.NewGuid().ToString();
        await PlaceAsync(client, "lumber_camp", 1, 2, key);

        var upgrade = new HttpRequestMessage(HttpMethod.Post, "/api/cities/me/buildings/whatever/upgrade");
        upgrade.Headers.Add("Idempotency-Key", key);
        var response = await client.SendAsync(upgrade);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [RequiresPostgresFact]
    public async Task A_forged_body_cannot_change_what_the_build_costs()
    {
        // SECURITY_MODEL.md T1. Extra fields are not merely ignored — they do not exist on
        // the request shape at all, so there is nothing to tamper with.
        await factory.ResetAsync();
        var client = await AuthenticatedClientAsync("forger@example.com");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/cities/me/buildings")
        {
            Content = JsonContent.Create(new
            {
                definitionId = "lumber_camp",
                col = 1,
                row = 2,
                costCoins = 0,
                buildSeconds = 0,
                level = 3,
            }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<ConstructionResponseDto>();

        Assert.True(body!.Accepted);
        Assert.Equal(650, body.BalanceCoins);       // charged the real 150, not the forged 0
        Assert.Equal(1, body.Building!.Level);      // level 1, not the forged 3
    }

    [RequiresPostgresFact]
    public async Task Building_on_a_locked_plot_is_refused_with_a_specific_reason()
    {
        await factory.ResetAsync();
        var client = await AuthenticatedClientAsync("locked@example.com");

        var response = await PlaceAsync(client, "lumber_camp", 7, 7);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ConstructionResponseDto>();

        Assert.False(body!.Accepted);
        Assert.Equal("PlotLocked", body.RefusalCode);
        Assert.Equal(800, body.BalanceCoins);   // nothing was charged
    }

    [RequiresPostgresFact]
    public async Task A_locked_building_type_is_refused()
    {
        // The Sawmill needs city level 2; a fresh city is level 1.
        await factory.ResetAsync();
        var client = await AuthenticatedClientAsync("early@example.com");

        var response = await PlaceAsync(client, "sawmill", 1, 2);
        var body = await response.Content.ReadFromJsonAsync<ConstructionResponseDto>();

        Assert.False(body!.Accepted);
        Assert.Equal("NotUnlocked", body.RefusalCode);
    }

    [RequiresPostgresFact]
    public async Task Every_accepted_command_is_written_to_the_audit_ledger()
    {
        // ADR-004: balances must be reconcilable by replaying the ledger.
        await factory.ResetAsync();
        var client = await AuthenticatedClientAsync("audited@example.com");

        await PlaceAsync(client, "lumber_camp", 1, 2);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TradebornDbContext>();

        var entry = await db.AuditLedger.AsNoTracking().SingleAsync();

        Assert.Equal("construction.started", entry.Kind);
        Assert.Equal(-15_000, entry.MoneyDeltaCent);      // 150 coins in cent
        Assert.Equal(65_000, entry.BalanceAfterCent);     // 650 coins remaining
        Assert.Contains("wood", entry.ResourceDeltas, StringComparison.Ordinal);
        Assert.NotNull(entry.IdempotencyKey);
    }

    // -- helpers ---------------------------------------------------------------------------

    private async Task<HttpClient> AuthenticatedClientAsync(string email)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = "correct horse battery",
            displayName = "Builder",
        });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<AuthDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.AccessToken);
        return client;
    }

    private static Task<HttpResponseMessage> PlaceAsync(
        HttpClient client,
        string definitionId,
        int col,
        int row,
        string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/cities/me/buildings")
        {
            Content = JsonContent.Create(new { definitionId, col, row }),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString());
        return client.SendAsync(request);
    }

    private async Task AssertBuildingCountAsync(int expected, string definitionId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TradebornDbContext>();

        var count = await db.CityBuildings.CountAsync(b => b.DefinitionId == definitionId);
        Assert.Equal(expected, count);
    }

    private async Task AssertBalanceAsync(long expected)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TradebornDbContext>();

        var city = await db.Cities.AsNoTracking().SingleAsync();
        Assert.Equal(expected, city.BalanceCent / 100);
    }

    private sealed record AuthDto(string AccessToken, Guid PlayerId);

    private sealed record ConstructionResponseDto(
        bool Accepted,
        string? RefusalCode,
        string? RefusalMessage,
        BuildingDto? Building,
        long BalanceCoins,
        List<ResourceDto> Resources);

    private sealed record BuildingDto(
        string Id, string DefinitionId, int Col, int Row, int Level, string State,
        string? HaltReason, DateTimeOffset? CompletesAtUtc, int PendingLevel, double ConstructionProgress);

    private sealed record ResourceDto(string Resource, long Quantity, long Capacity);
}
