using Microsoft.EntityFrameworkCore;
using Tradeborn.Domain.Buildings;
using Tradeborn.Infrastructure.Persistence;

namespace Tradeborn.Infrastructure.Seed;

/// <summary>
/// Creates a new player's starting city.
/// </summary>
/// <remarks>
/// Starting state from docs/economy/ECONOMY_DESIGN.md §10: 800 coins and 80 wood, with a
/// pre-placed Town Hall and Market. Those numbers are load-bearing — they are exactly what
/// makes the tutorial's first two builds affordable without waiting
/// (PLAYER_JOURNEY.md, minutes 1:00 and 2:10).
/// </remarks>
public sealed class CityProvisioner(TradebornDbContext db, TimeProvider timeProvider)
{
    public const int GridSize = 8;
    public const long StartingCoins = 800;
    public const long StartingWood = 80;

    public async Task<CityEntity> CreateForAsync(
        Guid playerId,
        string cityName,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();

        var city = new CityEntity
        {
            Id = Guid.NewGuid(),
            PlayerId = playerId,
            Name = cityName,
            GridSize = GridSize,
            BalanceCent = StartingCoins * 100,
            LastSettledAtUtc = now,
        };

        for (var row = 0; row < GridSize; row++)
        {
            for (var col = 0; col < GridSize; col++)
            {
                city.Plots.Add(new CityPlotEntity
                {
                    CityId = city.Id,
                    Col = col,
                    Row = row,
                    Terrain = TerrainAt(col, row),
                    // The starting cluster: 16 plots unlocked at city level 1.
                    Unlocked = col is >= 1 and <= 4 && row is >= 2 and <= 5,
                });
            }
        }

        AddBuilding(city, "town_hall", 2, 3);
        AddBuilding(city, "market", 4, 5);

        city.Inventory.Add(new CityInventoryEntity
        {
            CityId = city.Id,
            ResourceId = "wood",
            Quantity = StartingWood,
        });

        db.Cities.Add(city);
        await db.SaveChangesAsync(cancellationToken);

        return city;
    }

    private static void AddBuilding(CityEntity city, string definitionId, int col, int row) =>
        city.Buildings.Add(new CityBuildingEntity
        {
            Id = Guid.NewGuid(),
            CityId = city.Id,
            DefinitionId = definitionId,
            Col = col,
            Row = row,
            Level = 1,
            State = BuildingState.Idle.ToString(),
            HaltReason = HaltReason.None.ToString(),
            ProgressMilliseconds = 0,
        });

    /// <summary>
    /// Deterministic terrain: a stone ridge to the north-east and a dirt crossroads through
    /// the middle. Deterministic rather than random so every player's first impression is
    /// the one that was art-directed, and so screenshots are reproducible.
    /// </summary>
    private static string TerrainAt(int col, int row) => (col, row) switch
    {
        _ when col >= 6 && row <= 1 => "stone",
        _ when col == 3 || row == 4 => "dirt",
        _ => "grass",
    };

    public Task<bool> HasCityAsync(Guid playerId, CancellationToken cancellationToken = default) =>
        db.Cities.AnyAsync(c => c.PlayerId == playerId, cancellationToken);
}
