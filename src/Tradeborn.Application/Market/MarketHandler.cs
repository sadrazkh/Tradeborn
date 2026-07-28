using System.Text.Json;
using Tradeborn.Application.Abstractions;
using Tradeborn.Application.Contracts;
using Tradeborn.Domain.Cities;
using Tradeborn.Domain.Economy;
using Tradeborn.Domain.Market;
using Tradeborn.Domain.Production;
using Tradeborn.Domain.Progression;

namespace Tradeborn.Application.Market;

/// <summary>
/// Reads market prices and executes sales.
/// </summary>
/// <remarks>
/// <para>
/// The sale is the step that closes the core loop: goods become coins and XP
/// (CORE_LOOPS.md §3). It follows the same write pipeline as every other economic command
/// (ARCHITECTURE.md §6), with one addition — a second lock.
/// </para>
/// <para>
/// <b>Lock order is city, then market.</b> The market price row is shared between all players,
/// so two people selling wood at once contend on it. Every handler that touches both takes
/// them in this order; taking them in different orders is how a deadlock gets written.
/// </para>
/// </remarks>
public sealed class MarketHandler(
    ICityStore cityStore,
    IMarketStore marketStore,
    IPlayerStore playerStore,
    IUnitOfWork unitOfWork,
    IIdempotencyStore idempotency,
    IAuditLog auditLog,
    IGameCatalog catalog,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Operation = "market.sell";
    private const int HistoryPoints = 24;

    /// <summary>The trading board. Read-only, so it settles nothing and locks nothing.</summary>
    public async Task<MarketBoardDto?> GetBoardAsync(
        Guid playerId,
        CancellationToken cancellationToken = default)
    {
        var aggregate = await cityStore.LoadAsync(playerId, cancellationToken);
        if (aggregate is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var prices = await marketStore.LoadAllAsync(cancellationToken);
        var history = await marketStore.LoadHistoryAsync(HistoryPoints, cancellationToken);

        var tiers = catalog.Resources.ToDictionary(r => r.Id, r => r.Tier);

        var quotes = prices
            // Highest value first, so bread sits at the top of the panel where a player
            // browsing for the first time cannot miss that it is worth far more than planks
            // (PLAYER_JOURNEY.md 7:00).
            .OrderByDescending(p => p.BasePrice.Cent)
            .Select(p => new MarketQuoteDto(
                p.Resource.Value,
                tiers.GetValueOrDefault(p.Resource, "raw"),
                p.SellPriceAt(now).Cent,
                p.BuyPriceAt(now).Cent,
                p.BasePrice.Cent,
                p.Floor.Cent,
                p.Ceiling.Cent,
                aggregate.City.Inventory.Get(p.Resource),
                history.TryGetValue(p.Resource, out var points)
                    ? points.Select(pt => new PricePointDto(pt.AtUtc, pt.PriceCent)).ToArray()
                    : []))
            .ToArray();

        return new MarketBoardDto(
            now,
            MarketRules.OrderLimit(aggregate.City),
            MarketTuning.TransactionFeePercent,
            quotes);
    }

    /// <summary>Returns null when the player has no city.</summary>
    public async Task<SellResponse?> SellAsync(
        Guid playerId,
        SellRequest request,
        string idempotencyKey,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var replay = await idempotency.TryGetResponseAsync(playerId, idempotencyKey, Operation, cancellationToken);
        if (replay is not null)
        {
            return JsonSerializer.Deserialize<SellResponse>(replay, Json);
        }

        var aggregate = await cityStore.LoadForUpdateAsync(playerId, cancellationToken);
        if (aggregate is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        SettlementEngine.Settle(aggregate.City, now);

        var progress = await playerStore.LoadProgressAsync(playerId, cancellationToken)
            ?? new PlayerProgress(1, 0);

        var city = aggregate.City;
        var resource = ResourceId.From(request.Resource);

        // Second lock, after the city. See the class remarks on ordering.
        var price = await marketStore.LoadForUpdateAsync(resource, cancellationToken);

        var check = MarketRules.CanSell(city, price, request.Quantity);

        SellResponse response;

        if (!check.IsAllowed)
        {
            response = SellResponse.Refused(
                check.Refusal, request.Resource, city.Balance.Coins, Snapshot(city),
                progress.Level, progress.Xp, progress.XpToNextLevel, now);
        }
        else
        {
            response = ApplySale(playerId, aggregate, price!, progress, request.Quantity, now);

            await marketStore.SaveAsync(price!, cancellationToken);
            await marketStore.RecordHistoryAsync(price!, now, cancellationToken);
            await playerStore.SaveProgressAsync(playerId, progress, cancellationToken);

            await auditLog.AppendAsync(
                new AuditEntry(
                    PlayerId: playerId,
                    CityId: aggregate.Id,
                    Kind: "market.sold",
                    MoneyDeltaCent: response.NetCent,
                    BalanceAfterCent: city.Balance.Cent,
                    ResourceDeltas: new Dictionary<string, long> { [request.Resource] = -request.Quantity },
                    CorrelationId: correlationId,
                    IdempotencyKey: idempotencyKey,
                    Metadata: new Dictionary<string, string>
                    {
                        ["unitPriceCent"] = response.UnitPriceCent.ToString(),
                        ["feeCent"] = response.FeeCent.ToString(),
                        ["xpGained"] = response.XpGained.ToString(),
                    }),
                cancellationToken);
        }

        // A refusal still persists the settlement that just ran — that production is real.
        await cityStore.SaveAsync(aggregate, cancellationToken);

        var recorded = await idempotency.TryRecordAsync(
            playerId, idempotencyKey, Operation, JsonSerializer.Serialize(response, Json), cancellationToken);

        if (!recorded)
        {
            var stored = await idempotency.TryGetResponseAsync(
                playerId, idempotencyKey, Operation, cancellationToken);

            return stored is null ? response : JsonSerializer.Deserialize<SellResponse>(stored, Json);
        }

        await unitOfWork.CommitAsync(cancellationToken);
        return response;
    }

    /// <summary>
    /// Executes the sale against the price read inside this transaction.
    /// </summary>
    /// <remarks>
    /// The price used is the one read <i>here</i>, under the row lock — never one the client
    /// supplied or saw. Two players selling simultaneously each get the price as it stood when
    /// their transaction reached this point, and the second one gets the lower price the first
    /// one caused.
    /// </remarks>
    private static SellResponse ApplySale(
        Guid playerId,
        CityAggregate aggregate,
        MarketPrice price,
        PlayerProgress progress,
        long quantity,
        DateTimeOffset now)
    {
        _ = playerId;

        var city = aggregate.City;
        var unitPrice = price.SellPriceAt(now);
        var quote = SaleQuote.For(unitPrice, quantity);

        city.Inventory.Remove(price.Resource, quantity);
        city.Credit(quote.Net);
        city.RecordSale();

        // The sale moves the price only after it has been executed, so the seller gets the
        // price they were quoted and the *next* seller inherits the impact.
        price.ApplySale(quantity, now);

        var xp = XpAwards.ForSale(quote.Net);
        var levelsGained = progress.AddXp(xp);

        return new SellResponse(
            Accepted: true,
            RefusalCode: null,
            RefusalMessage: null,
            Resource: price.Resource.Value,
            QuantitySold: quantity,
            UnitPriceCent: unitPrice.Cent,
            GrossCent: quote.Gross.Cent,
            FeeCent: quote.Fee.Cent,
            NetCent: quote.Net.Cent,
            BalanceCoins: city.Balance.Coins,
            Resources: Snapshot(city),
            XpGained: xp,
            PlayerLevel: progress.Level,
            PlayerXp: progress.Xp,
            XpToNextLevel: progress.XpToNextLevel,
            LevelsGained: levelsGained,
            NewSellPriceCent: price.SellPriceAt(now).Cent,
            ServerTimeUtc: now);
    }

    private static ResourceBalanceDto[] Snapshot(City city)
    {
        var capacity = city.Inventory.CapacityPerResource;
        return city.Inventory
            .Snapshot()
            .OrderBy(pair => pair.Key.Value, StringComparer.Ordinal)
            .Select(pair => new ResourceBalanceDto(pair.Key.Value, pair.Value, capacity))
            .ToArray();
    }
}
