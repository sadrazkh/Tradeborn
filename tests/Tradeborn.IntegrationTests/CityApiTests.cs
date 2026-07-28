using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tradeborn.Infrastructure.Persistence;
using Tradeborn.Infrastructure.Seed;
using Xunit;

namespace Tradeborn.IntegrationTests;

public sealed class CityApiTests : IClassFixture<TradebornAppFactory>
{
    private readonly TradebornAppFactory factory;

    public CityApiTests(TradebornAppFactory factory) => this.factory = factory;

    [RequiresPostgresFact]
    public async Task Seed_is_idempotent()
    {
        // A seeder that duplicates on re-run corrupts a live catalog on every deploy.
        // "Probably idempotent" is how that ships, so it is asserted rather than assumed.
        await factory.ResetAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TradebornDbContext>();
        var seeder = scope.ServiceProvider.GetRequiredService<GameCatalogSeeder>();

        await seeder.SeedAsync();
        var first = await SnapshotAsync(db);

        await seeder.SeedAsync();
        var second = await SnapshotAsync(db);

        Assert.Equal(first, second);
    }

    [RequiresPostgresFact]
    public async Task Registering_provisions_a_playable_city()
    {
        await factory.ResetAsync();
        var client = factory.CreateClient();

        var token = await RegisterAsync(client, "founder@example.com");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var city = await client.GetFromJsonAsync<CityResponse>("/api/cities/me");

        Assert.NotNull(city);
        Assert.Equal(8, city!.GridSize);
        Assert.Equal(64, city.Plots.Count);
        Assert.Equal(800, city.BalanceCoins);

        // Pre-placed Town Hall and Market, per ECONOMY_DESIGN.md §10.
        Assert.Contains(city.Buildings, b => b.DefinitionId == "town_hall");
        Assert.Contains(city.Buildings, b => b.DefinitionId == "market");

        // Starting wood must be present and the Town Hall's storage must already apply,
        // or the tutorial's first two builds are not affordable.
        Assert.Contains(city.Resources, r => r.Resource == "wood" && r.Quantity == 80);
        Assert.Equal(100, city.CapacityPerResource);
    }

    [RequiresPostgresFact]
    public async Task City_endpoint_rejects_anonymous_callers()
    {
        await factory.ResetAsync();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/cities/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [RequiresPostgresFact]
    public async Task Each_player_sees_only_their_own_city()
    {
        // SECURITY_MODEL.md T7. The endpoint takes no player id at all, so this asserts the
        // structural property: two tokens cannot resolve to the same city.
        await factory.ResetAsync();

        var alice = factory.CreateClient();
        var bob = factory.CreateClient();

        alice.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await RegisterAsync(alice, "alice@example.com", "Alice"));
        bob.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await RegisterAsync(bob, "bob@example.com", "Bob"));

        var aliceCity = await alice.GetFromJsonAsync<CityResponse>("/api/cities/me");
        var bobCity = await bob.GetFromJsonAsync<CityResponse>("/api/cities/me");

        Assert.NotNull(aliceCity);
        Assert.NotNull(bobCity);
        Assert.NotEqual(aliceCity!.Name, bobCity!.Name);
        Assert.StartsWith("Alice", aliceCity.Name, StringComparison.Ordinal);
        Assert.StartsWith("Bob", bobCity.Name, StringComparison.Ordinal);
    }

    [RequiresPostgresFact]
    public async Task Registering_the_same_email_twice_is_rejected()
    {
        await factory.ResetAsync();
        var client = factory.CreateClient();

        await RegisterAsync(client, "duplicate@example.com");

        var second = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = "duplicate@example.com",
            password = "correct horse battery",
            displayName = "Impostor",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
    }

    [RequiresPostgresFact]
    public async Task A_refresh_token_cannot_be_used_twice()
    {
        // ADR-007 reuse detection: replaying a rotated token means it leaked, so the whole
        // family is revoked rather than the replay simply being ignored.
        await factory.ResetAsync();

        var client = factory.CreateClient();
        await RegisterAsync(client, "rotate@example.com");

        var first = await client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // The handler rotated the cookie; replaying the ORIGINAL one is the attack.
        var replay = await client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
    }

    private static async Task<string> RegisterAsync(
        HttpClient client,
        string email,
        string displayName = "Founder")
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = "correct horse battery",
            displayName,
        });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        return body!.AccessToken;
    }

    private static async Task<string> SnapshotAsync(TradebornDbContext db)
    {
        var resources = await db.ResourceDefinitions.AsNoTracking()
            .OrderBy(r => r.Id).Select(r => $"{r.Id}:{r.Tier}:{r.BasePriceCoins}:{r.MarketDepth}").ToListAsync();
        var recipes = await db.Recipes.AsNoTracking()
            .OrderBy(r => r.Id).Select(r => $"{r.Id}:{r.CycleMilliseconds}:{r.TopologicalRank}").ToListAsync();
        var ingredients = await db.RecipeIngredients.AsNoTracking()
            .OrderBy(i => i.RecipeId).ThenBy(i => i.ResourceId).ThenBy(i => i.IsOutput)
            .Select(i => $"{i.RecipeId}:{i.ResourceId}:{i.Quantity}:{i.IsOutput}").ToListAsync();
        var buildings = await db.BuildingDefinitions.AsNoTracking()
            .OrderBy(b => b.Id).Select(b => $"{b.Id}:{b.RecipeId}:{b.StoragePerResource}:{b.BuildCostCoins}").ToListAsync();

        return string.Join("|", resources.Concat(recipes).Concat(ingredients).Concat(buildings));
    }

    private sealed record AuthResponseDto(string AccessToken, Guid PlayerId);

    private sealed record CityResponse(
        string Name,
        int GridSize,
        long BalanceCoins,
        long CapacityPerResource,
        List<PlotResponse> Plots,
        List<BuildingResponse> Buildings,
        List<ResourceResponse> Resources);

    private sealed record PlotResponse(int Col, int Row, string Terrain, bool Unlocked);
    private sealed record BuildingResponse(string Id, string DefinitionId, int Col, int Row, int Level, string State);
    private sealed record ResourceResponse(string Resource, long Quantity, long Capacity);
}
