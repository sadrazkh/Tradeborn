using Microsoft.EntityFrameworkCore;
using Tradeborn.Application.Abstractions;
using Tradeborn.Domain.Buildings;
using Tradeborn.Domain.Cities;
using Tradeborn.Domain.Common;
using Tradeborn.Domain.Economy;

namespace Tradeborn.Infrastructure.Persistence;

/// <summary>
/// Loads and saves the city aggregate, mapping between persistence entities and the domain.
/// </summary>
public sealed class CityStore(TradebornDbContext db, IGameCatalog catalog) : ICityStore
{
    public Task<CityAggregate?> LoadAsync(Guid playerId, CancellationToken cancellationToken = default) =>
        LoadCoreAsync(playerId, cancellationToken);

    public async Task<CityAggregate?> LoadForUpdateAsync(
        Guid playerId,
        CancellationToken cancellationToken = default)
    {
        // Takes the row lock before reading anything else, so two concurrent commands for the
        // same city serialise here rather than racing through validation with the same
        // balance (SECURITY_MODEL.md T4). Identifiers are quoted because the schema uses
        // PascalCase columns; the parameter is still bound, never interpolated into SQL.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM cities WHERE "PlayerId" = {playerId} FOR UPDATE""",
            cancellationToken);

        return await LoadCoreAsync(playerId, cancellationToken);
    }

    private async Task<CityAggregate?> LoadCoreAsync(Guid playerId, CancellationToken cancellationToken)
    {
        // Split queries: one JOIN across buildings, inventory and plots would multiply rows
        // together. Verified by the query-count assertion in TEST_STRATEGY.md §3 rather than
        // by inspection.
        var entity = await db.Cities
            .AsSplitQuery()
            .Include(c => c.Buildings)
            .Include(c => c.Inventory)
            .Include(c => c.Plots)
            .FirstOrDefaultAsync(c => c.PlayerId == playerId, cancellationToken);

        if (entity is null)
        {
            return null;
        }

        var city = new City(entity.Id.ToString(), entity.LastSettledAtUtc);
        city.Credit(Money.FromCent(entity.BalanceCent));

        city.SetPlots(entity.Plots
            .OrderBy(p => p.Row).ThenBy(p => p.Col)
            .Select(p => new CityPlot(p.Col, p.Row, p.Terrain, p.Unlocked)));

        foreach (var building in entity.Buildings.OrderBy(b => b.Id))
        {
            if (!catalog.TryGetBuilding(building.DefinitionId, out var definition))
            {
                // A building whose definition was removed from the catalog. Skipping keeps
                // the city loadable instead of failing the player's whole session; the
                // orphan is visible in the logs and in the admin panel (Phase 8).
                continue;
            }

            city.Add(BuildingInstance.Rehydrate(
                building.Id.ToString(),
                definition,
                building.Col,
                building.Row,
                building.Level,
                Enum.Parse<BuildingState>(building.State),
                Enum.Parse<HaltReason>(building.HaltReason),
                building.ProgressMilliseconds,
                building.CompletesAtUtc,
                building.PendingLevel == 0 ? building.Level : building.PendingLevel));
        }

        // Capacity is derived from the buildings just added, so inventory must be restored
        // after them — otherwise Set() would clamp against a capacity of zero.
        city.RecomputeCapacity();

        foreach (var item in entity.Inventory)
        {
            city.Inventory.Set(ResourceId.From(item.ResourceId), item.Quantity);
        }

        return new CityAggregate(entity.Id, city, entity.Name, entity.GridSize);
    }

    public async Task SaveAsync(CityAggregate aggregate, CancellationToken cancellationToken = default)
    {
        var cityId = aggregate.Id;

        var entity = await db.Cities
            .Include(c => c.Buildings)
            .Include(c => c.Inventory)
            .FirstOrDefaultAsync(c => c.Id == cityId, cancellationToken)
            ?? throw new InvalidOperationException($"City '{cityId}' no longer exists.");

        entity.BalanceCent = aggregate.City.Balance.Cent;
        entity.LastSettledAtUtc = aggregate.City.LastSettledAt;

        SaveBuildings(entity, aggregate, cityId);
        SaveInventory(entity, aggregate, cityId);

        await db.SaveChangesAsync(cancellationToken);
    }

    private static void SaveBuildings(CityEntity entity, CityAggregate aggregate, Guid cityId)
    {
        var stored = entity.Buildings.ToDictionary(b => b.Id.ToString());

        foreach (var building in aggregate.City.Buildings)
        {
            if (stored.TryGetValue(building.Id, out var row))
            {
                row.Level = building.Level;
                row.State = building.State.ToString();
                row.HaltReason = building.HaltReason.ToString();
                row.ProgressMilliseconds = building.ProgressMilliseconds;
                row.CompletesAtUtc = building.CompletesAtUtc;
                row.PendingLevel = building.PendingLevel;
                continue;
            }

            // A newly placed building. Inserting it here — inside the command's transaction,
            // while the city row is locked — is what makes the unique index on
            // (CityId, Col, Row) the final backstop against two builds on one plot.
            entity.Buildings.Add(new CityBuildingEntity
            {
                Id = Guid.Parse(building.Id),
                CityId = cityId,
                DefinitionId = building.Definition.Id,
                Col = building.Col,
                Row = building.Row,
                Level = building.Level,
                State = building.State.ToString(),
                HaltReason = building.HaltReason.ToString(),
                ProgressMilliseconds = building.ProgressMilliseconds,
                CompletesAtUtc = building.CompletesAtUtc,
                PendingLevel = building.PendingLevel,
            });
        }
    }

    private static void SaveInventory(CityEntity entity, CityAggregate aggregate, Guid cityId)
    {
        var byResource = entity.Inventory.ToDictionary(i => i.ResourceId, StringComparer.Ordinal);

        foreach (var (resource, quantity) in aggregate.City.Inventory.Snapshot())
        {
            if (byResource.TryGetValue(resource.Value, out var row))
            {
                row.Quantity = quantity;
            }
            else
            {
                entity.Inventory.Add(new CityInventoryEntity
                {
                    CityId = cityId,
                    ResourceId = resource.Value,
                    Quantity = quantity,
                });
            }
        }
    }
}
