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
    public async Task<CityAggregate?> LoadAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        // A single round trip with split queries: one JOIN across buildings, inventory and
        // plots would multiply rows together. Verified by the query-count assertion in
        // TEST_STRATEGY.md §3 rather than by inspection.
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
                building.ProgressMilliseconds));
        }

        // Capacity is derived from the buildings just added, so inventory must be restored
        // after them — otherwise Set() would clamp against a capacity of zero.
        city.RecomputeCapacity();

        foreach (var item in entity.Inventory)
        {
            city.Inventory.Set(ResourceId.From(item.ResourceId), item.Quantity);
        }

        var plots = entity.Plots
            .OrderBy(p => p.Row).ThenBy(p => p.Col)
            .Select(p => new PlotState(p.Col, p.Row, p.Terrain, p.Unlocked))
            .ToArray();

        return new CityAggregate(city, entity.Name, entity.GridSize, plots);
    }

    public async Task SaveAsync(CityAggregate aggregate, CancellationToken cancellationToken = default)
    {
        var cityId = Guid.Parse(aggregate.City.Id);

        var entity = await db.Cities
            .Include(c => c.Buildings)
            .Include(c => c.Inventory)
            .FirstOrDefaultAsync(c => c.Id == cityId, cancellationToken)
            ?? throw new InvalidOperationException($"City '{cityId}' no longer exists.");

        entity.BalanceCent = aggregate.City.Balance.Cent;
        entity.LastSettledAtUtc = aggregate.City.LastSettledAt;

        var buildingsById = entity.Buildings.ToDictionary(b => b.Id.ToString());
        foreach (var building in aggregate.City.Buildings)
        {
            if (!buildingsById.TryGetValue(building.Id, out var stored))
            {
                continue;
            }

            stored.Level = building.Level;
            stored.State = building.State.ToString();
            stored.HaltReason = building.HaltReason.ToString();
            stored.ProgressMilliseconds = building.ProgressMilliseconds;
        }

        var inventoryByResource = entity.Inventory.ToDictionary(i => i.ResourceId, StringComparer.Ordinal);
        foreach (var (resource, quantity) in aggregate.City.Inventory.Snapshot())
        {
            if (inventoryByResource.TryGetValue(resource.Value, out var stored))
            {
                stored.Quantity = quantity;
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

        await db.SaveChangesAsync(cancellationToken);
    }
}
