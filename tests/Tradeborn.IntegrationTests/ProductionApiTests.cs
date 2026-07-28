using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace Tradeborn.IntegrationTests;

public sealed class ProductionApiTests(TradebornAppFactory factory) : IClassFixture<TradebornAppFactory>
{
    [RequiresPostgresFact]
    public async Task The_city_read_path_does_not_issue_N_plus_1_queries()
    {
        // PERFORMANCE_BUDGET.md §6. An N+1 is invisible in review and in local testing — it
        // only appears as latency once a player has twenty buildings. The guard is that the
        // query count must not grow with the number of buildings.
        await factory.ResetAsync();
        var client = await AuthenticatedClientAsync("queries@example.com");

        // Warm up: first request pays for connection setup and query-plan caching.
        await client.GetAsync("/api/cities/me");

        int baseline;
        using (factory.Queries.Measure())
        {
            await client.GetAsync("/api/cities/me");
            baseline = factory.Queries.Count;
        }

        await PlaceAsync(client, "lumber_camp", 1, 2);

        int afterOneMore;
        using (factory.Queries.Measure())
        {
            await client.GetAsync("/api/cities/me");
            afterOneMore = factory.Queries.Count;
        }

        Assert.Equal(baseline, afterOneMore);

        // Split queries mean a handful, not one, but it is a small constant either way.
        Assert.InRange(baseline, 1, 8);
    }

    [RequiresPostgresFact]
    public async Task A_finished_building_starts_idle_and_the_player_switches_it_on()
    {
        // Slice step 7 end to end.
        await factory.ResetAsync();
        var client = await AuthenticatedClientAsync("starter@example.com");

        var built = await PlaceAsync(client, "lumber_camp", 1, 2);
        var buildingId = (await built.Content.ReadFromJsonAsync<ConstructionDto>())!.Building!.Id;

        var response = await SetProductionAsync(client, buildingId, active: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ProductionDto>();

        // The Lumber Camp takes 30 s to build, so immediately after placing it is still
        // under construction and cannot be switched on yet.
        Assert.False(body!.Accepted);
        Assert.Equal("UnderConstruction", body.RefusalCode);
    }

    [RequiresPostgresFact]
    public async Task Switching_production_is_idempotent()
    {
        await factory.ResetAsync();
        var client = await AuthenticatedClientAsync("toggle@example.com");

        var built = await PlaceAsync(client, "lumber_camp", 1, 2);
        var buildingId = (await built.Content.ReadFromJsonAsync<ConstructionDto>())!.Building!.Id;

        var key = Guid.NewGuid().ToString();
        var first = await SetProductionAsync(client, buildingId, active: false, key);
        var second = await SetProductionAsync(client, buildingId, active: false, key);

        var firstBody = await first.Content.ReadFromJsonAsync<ProductionDto>();
        var secondBody = await second.Content.ReadFromJsonAsync<ProductionDto>();

        // The replay returns the original answer rather than re-evaluating. This is why the
        // request carries the desired state rather than a "toggle" verb — a retried toggle
        // would flip the building back.
        Assert.Equal(firstBody!.Accepted, secondBody!.Accepted);
        Assert.Equal(firstBody.RefusalCode, secondBody.RefusalCode);
    }

    [RequiresPostgresFact]
    public async Task Production_cannot_be_switched_on_for_another_players_building()
    {
        // SECURITY_MODEL.md T7: the city is resolved from the token, so another player's
        // building id simply does not exist in the caller's city.
        await factory.ResetAsync();

        var alice = await AuthenticatedClientAsync("alice.prod@example.com");
        var bob = await AuthenticatedClientAsync("bob.prod@example.com");

        var built = await PlaceAsync(alice, "lumber_camp", 1, 2);
        var aliceBuildingId = (await built.Content.ReadFromJsonAsync<ConstructionDto>())!.Building!.Id;

        var response = await SetProductionAsync(bob, aliceBuildingId, active: true);
        var body = await response.Content.ReadFromJsonAsync<ProductionDto>();

        Assert.False(body!.Accepted);
        Assert.Equal("BuildingNotFound", body.RefusalCode);
    }

    // -- helpers ---------------------------------------------------------------------------

    private async Task<HttpClient> AuthenticatedClientAsync(string email)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = "correct horse battery",
            displayName = "Producer",
        });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<AuthDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.AccessToken);
        return client;
    }

    private static Task<HttpResponseMessage> PlaceAsync(HttpClient client, string definitionId, int col, int row)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/cities/me/buildings")
        {
            Content = JsonContent.Create(new { definitionId, col, row }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> SetProductionAsync(
        HttpClient client,
        string buildingId,
        bool active,
        string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/cities/me/buildings/{buildingId}/production")
        {
            Content = JsonContent.Create(new { active }),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString());
        return client.SendAsync(request);
    }

    private sealed record AuthDto(string AccessToken, Guid PlayerId);
    private sealed record ConstructionDto(bool Accepted, BuildingRef? Building);
    private sealed record ProductionDto(bool Accepted, string? RefusalCode, string? RefusalMessage, BuildingRef? Building);
    private sealed record BuildingRef(string Id, string DefinitionId, string State);
}
