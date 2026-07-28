using Microsoft.EntityFrameworkCore;
using Tradeborn.Application.Abstractions;
using Tradeborn.Domain.Common;
using Tradeborn.Domain.Economy;
using Tradeborn.Domain.Market;
using Tradeborn.Domain.Progression;

namespace Tradeborn.Infrastructure.Persistence;

/// <summary>
/// The NPC market's prices, backed by PostgreSQL.
/// </summary>
/// <remarks>
/// Base price and depth come from the seeded catalog rather than the price row, so retuning
/// the economy is a seed change and every live price picks up the new base on its next read.
/// The row holds only what cannot be derived: the price at the last trade, and when that was.
/// </remarks>
public sealed class MarketStore(TradebornDbContext db, IGameCatalog catalog, TimeProvider timeProvider)
    : IMarketStore
{
    public async Task<IReadOnlyList<MarketPrice>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.MarketPrices.AsNoTracking().ToDictionaryAsync(
            r => r.ResourceId, StringComparer.Ordinal, cancellationToken);

        var now = timeProvider.GetUtcNow();

        // Driven by the catalog, not by the table: a resource added in a later deploy trades
        // at base immediately instead of being invisible until someone seeds a row for it.
        return catalog.Resources
            .Select(definition => Materialise(definition, rows.GetValueOrDefault(definition.Id.Value), now))
            .ToArray();
    }

    public async Task<MarketPrice?> LoadForUpdateAsync(
        ResourceId resource,
        CancellationToken cancellationToken = default)
    {
        var definition = catalog.Resources.FirstOrDefault(r => r.Id == resource);
        if (definition is null)
        {
            return null;
        }

        // Locks the price row so two concurrent sellers of this resource serialise. Taken
        // after the city lock — a consistent order across handlers is what avoids deadlock.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM market_prices WHERE "ResourceId" = {resource.Value} FOR UPDATE""",
            cancellationToken);

        var row = await db.MarketPrices
            .FirstOrDefaultAsync(r => r.ResourceId == resource.Value, cancellationToken);

        return Materialise(definition, row, timeProvider.GetUtcNow());
    }

    private static MarketPrice Materialise(
        ResourceDefinition definition,
        MarketPriceEntity? row,
        DateTimeOffset now) =>
        row is null
            ? MarketPrice.AtBase(definition.Id, Money.FromCoins(definition.BasePriceCoins), definition.MarketDepth, now)
            : new MarketPrice(
                definition.Id,
                Money.FromCoins(definition.BasePriceCoins),
                definition.MarketDepth,
                Money.FromCent(row.PriceAtLastTradeCent),
                row.LastTradeAtUtc);

    public async Task SaveAsync(MarketPrice price, CancellationToken cancellationToken = default)
    {
        var row = await db.MarketPrices
            .FirstOrDefaultAsync(r => r.ResourceId == price.Resource.Value, cancellationToken);

        if (row is null)
        {
            db.MarketPrices.Add(new MarketPriceEntity
            {
                ResourceId = price.Resource.Value,
                PriceAtLastTradeCent = price.PriceAtLastTrade.Cent,
                LastTradeAtUtc = price.LastTradeAtUtc,
            });
            return;
        }

        row.PriceAtLastTradeCent = price.PriceAtLastTrade.Cent;
        row.LastTradeAtUtc = price.LastTradeAtUtc;
    }

    public Task RecordHistoryAsync(
        MarketPrice price,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken = default)
    {
        db.MarketPriceHistory.Add(new MarketPriceHistoryEntity
        {
            ResourceId = price.Resource.Value,
            RecordedAtUtc = atUtc,
            PriceCent = price.SellPriceAt(atUtc).Cent,
        });

        return Task.CompletedTask;
    }

    public async Task<IReadOnlyDictionary<ResourceId, IReadOnlyList<PricePoint>>> LoadHistoryAsync(
        int pointsPerResource,
        CancellationToken cancellationToken = default)
    {
        // One query for all resources, then grouped in memory. A per-resource query would be
        // a textbook N+1 on a panel that shows five sparklines at once.
        var resourceCount = Math.Max(1, catalog.Resources.Count);

        var rows = await db.MarketPriceHistory
            .AsNoTracking()
            .OrderByDescending(h => h.RecordedAtUtc)
            .Take(pointsPerResource * resourceCount)
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(h => h.ResourceId, StringComparer.Ordinal)
            .ToDictionary(
                group => ResourceId.From(group.Key),
                group => (IReadOnlyList<PricePoint>)group
                    .OrderBy(h => h.RecordedAtUtc)
                    .TakeLast(pointsPerResource)
                    .Select(h => new PricePoint(h.RecordedAtUtc, h.PriceCent))
                    .ToArray());
    }
}

/// <summary>A player's level and experience.</summary>
public sealed class PlayerStore(TradebornDbContext db) : IPlayerStore
{
    public async Task<PlayerProgress?> LoadProgressAsync(
        Guid playerId,
        CancellationToken cancellationToken = default)
    {
        var row = await db.Players
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == playerId, cancellationToken);

        return row is null ? null : new PlayerProgress(Math.Max(1, row.Level), row.Xp);
    }

    public async Task SaveProgressAsync(
        Guid playerId,
        PlayerProgress progress,
        CancellationToken cancellationToken = default)
    {
        var row = await db.Players.FirstOrDefaultAsync(p => p.Id == playerId, cancellationToken);
        if (row is null)
        {
            return;
        }

        row.Level = progress.Level;
        row.Xp = progress.Xp;
    }
}
