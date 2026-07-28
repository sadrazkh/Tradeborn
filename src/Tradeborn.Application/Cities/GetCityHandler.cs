using Tradeborn.Application.Abstractions;
using Tradeborn.Application.Contracts;
using Tradeborn.Domain.Buildings;
using Tradeborn.Domain.Production;
using Tradeborn.Domain.Progression;

namespace Tradeborn.Application.Cities;

/// <summary>
/// Reads a player's city, settling it to the current server time first.
/// </summary>
/// <remarks>
/// <para>
/// Every read settles before it returns (REALTIME_AND_TIME_MODEL.md §2). This is the single
/// entry point for settlement — no other module may call
/// <see cref="SettlementEngine"/> directly, which is what guarantees a caller can never
/// observe stale economy state.
/// </para>
/// <para>
/// Settlement mutates the city, so a read is a write. That is inherent to lazy settlement
/// and is why this runs inside a transaction like any command.
/// </para>
/// </remarks>
public sealed class GetCityHandler(
    ICityStore cityStore,
    IPlayerStore playerStore,
    TimeProvider timeProvider)
{
    public async Task<CityDto?> HandleAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        var aggregate = await cityStore.LoadAsync(playerId, cancellationToken);
        if (aggregate is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var settledFrom = aggregate.City.LastSettledAt;

        var settlement = SettlementEngine.Settle(aggregate.City, now);

        if (settlement.StepsRun > 0)
        {
            await cityStore.SaveAsync(aggregate, cancellationToken);
        }

        var progress = await playerStore.LoadProgressAsync(playerId, cancellationToken)
            ?? new PlayerProgress(1, 0);

        return Map(aggregate, now, settledFrom, settlement, progress);
    }

    private static CityDto Map(
        CityAggregate aggregate,
        DateTimeOffset now,
        DateTimeOffset settledFrom,
        SettlementResult settlement,
        PlayerProgress progress)
    {
        var city = aggregate.City;
        var capacity = city.Inventory.CapacityPerResource;

        var resources = city.Inventory
            .Snapshot()
            .OrderBy(pair => pair.Key.Value, StringComparer.Ordinal)
            .Select(pair => new ResourceBalanceDto(pair.Key.Value, pair.Value, capacity))
            .ToArray();

        var buildings = city.Buildings
            .Select(b => new BuildingDto(
                b.Id,
                b.Definition.Id,
                b.Col,
                b.Row,
                b.Level,
                b.State.ToString(),
                b.HaltReason == HaltReason.None ? null : b.HaltReason.ToString(),
                b.CompletesAtUtc,
                b.PendingLevel,
                b.ConstructionProgress(now)))
            .ToArray();

        var plots = city.Plots
            .OrderBy(p => p.Row).ThenBy(p => p.Col)
            .Select(p => new PlotDto(p.Col, p.Row, p.Terrain, p.Unlocked))
            .ToArray();

        // Only worth showing a recap for a real absence; a page refresh should not pop one.
        OfflineSummaryDto? summary = null;
        if (settlement.DeliveredAnything && now - settledFrom > TimeSpan.FromMinutes(2))
        {
            // Deliveries, not gross production: goods still on a cart are not the player's to
            // spend, and the recap must agree with the balances shown beside it.
            summary = new OfflineSummaryDto(
                settledFrom,
                settlement.Delivered
                    .OrderBy(pair => pair.Key.Value, StringComparer.Ordinal)
                    .Select(pair => new ResourceBalanceDto(pair.Key.Value, pair.Value, capacity))
                    .ToArray(),
                city.Buildings
                    .Where(b => b.HaltReason != HaltReason.None)
                    .Select(b => b.Id)
                    .ToArray());
        }

        var transports = city.Transports
            .OrderBy(t => t.ArrivesAtUtc)
            .Select(t => new TransportDto(
                t.Id,
                t.FromBuildingId,
                t.Resource.Value,
                t.Quantity,
                t.DepartedAtUtc,
                t.ArrivesAtUtc))
            .ToArray();

        return new CityDto(
            aggregate.Name,
            aggregate.GridSize,
            now,
            city.Balance.Coins,
            capacity,
            plots,
            buildings,
            resources,
            transports,
            new PlayerProgressDto(progress.Level, progress.Xp, progress.XpToNextLevel, city.Level),
            summary);
    }
}
