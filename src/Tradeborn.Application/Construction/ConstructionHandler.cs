using System.Text.Json;
using Tradeborn.Application.Abstractions;
using Tradeborn.Application.Contracts;
using Tradeborn.Domain.Buildings;
using Tradeborn.Domain.Cities;
using Tradeborn.Domain.Construction;
using Tradeborn.Domain.Production;

namespace Tradeborn.Application.Construction;

/// <summary>
/// Starts builds and upgrades.
/// </summary>
/// <remarks>
/// <para>
/// Both commands follow the single write pipeline from ARCHITECTURE.md §6:
/// </para>
/// <code>
/// BEGIN
///   check idempotency key      -> replay stored response if present
///   SELECT city FOR UPDATE     -> serialises commands for this city
///   settle to server time      -> validate against current, not stale, state
///   validate                   -> ConstructionRules is the authority
///   apply                      -> spend, place or upgrade
///   append audit ledger
///   record idempotency key
/// COMMIT
/// </code>
/// <para>
/// Ordering matters. Settlement runs <b>before</b> validation so a player whose warehouse
/// finished while they were away can immediately spend the materials it now holds. Validating
/// first would reject them on state that is already out of date.
/// </para>
/// </remarks>
public sealed class ConstructionHandler(
    ICityStore cityStore,
    IUnitOfWork unitOfWork,
    IIdempotencyStore idempotency,
    IAuditLog auditLog,
    IGameCatalog catalog,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<ConstructionResponse?> StartConstructionAsync(
        Guid playerId,
        StartConstructionRequest request,
        string idempotencyKey,
        string? correlationId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            playerId,
            idempotencyKey,
            operation: "construction.start",
            correlationId,
            (aggregate, now) => Place(aggregate, request, now),
            cancellationToken);

    public Task<ConstructionResponse?> StartUpgradeAsync(
        Guid playerId,
        StartUpgradeRequest request,
        string idempotencyKey,
        string? correlationId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            playerId,
            idempotencyKey,
            operation: "upgrade.start",
            correlationId,
            (aggregate, now) => Upgrade(aggregate, request, now),
            cancellationToken);

    /// <summary>Returns null when the player has no city.</summary>
    private async Task<ConstructionResponse?> ExecuteAsync(
        Guid playerId,
        string idempotencyKey,
        string operation,
        string? correlationId,
        Func<CityAggregate, DateTimeOffset, Applied> apply,
        CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var replay = await idempotency.TryGetResponseAsync(playerId, idempotencyKey, operation, cancellationToken);
        if (replay is not null)
        {
            // A retry of a request that already succeeded. Return the original response
            // verbatim rather than charging the player a second time.
            return JsonSerializer.Deserialize<ConstructionResponse>(replay, Json);
        }

        var aggregate = await cityStore.LoadForUpdateAsync(playerId, cancellationToken);
        if (aggregate is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        SettlementEngine.Settle(aggregate.City, now);

        var applied = apply(aggregate, now);
        var response = applied.Response;

        // A refusal still persists the settlement that just ran — that work is real and
        // discarding it would silently roll back production the player had earned.
        await cityStore.SaveAsync(aggregate, cancellationToken);

        if (applied.Audit is not null)
        {
            await auditLog.AppendAsync(
                applied.Audit with
                {
                    PlayerId = playerId,
                    CityId = aggregate.Id,
                    CorrelationId = correlationId,
                    IdempotencyKey = idempotencyKey,
                },
                cancellationToken);
        }

        var recorded = await idempotency.TryRecordAsync(
            playerId, idempotencyKey, operation, JsonSerializer.Serialize(response, Json), cancellationToken);

        if (!recorded)
        {
            // A concurrent duplicate of this exact request won the race. Abandoning the
            // transaction is the correct outcome: the other attempt already applied it.
            return JsonSerializer.Deserialize<ConstructionResponse>(
                await idempotency.TryGetResponseAsync(playerId, idempotencyKey, operation, cancellationToken)
                ?? JsonSerializer.Serialize(response, Json),
                Json);
        }

        await unitOfWork.CommitAsync(cancellationToken);
        return response;
    }

    private Applied Place(CityAggregate aggregate, StartConstructionRequest request, DateTimeOffset now)
    {
        var city = aggregate.City;

        if (!catalog.TryGetBuilding(request.DefinitionId, out var definition))
        {
            return Applied.Refusal(ConstructionRefusal.CannotBeBuilt, city, now);
        }

        var check = ConstructionRules.CanPlace(city, definition, request.Col, request.Row);
        if (!check.IsAllowed)
        {
            return Applied.Refusal(check.Refusal, city, now);
        }

        var cost = definition.CostAtLevel(1);
        city.Spend(cost);

        var building = BuildingInstance.PlaceNew(
            Guid.NewGuid().ToString(), definition, request.Col, request.Row, now);
        city.Add(building);

        return Applied.Success(city, building, now, "construction.started", cost);
    }

    private static Applied Upgrade(CityAggregate aggregate, StartUpgradeRequest request, DateTimeOffset now)
    {
        var city = aggregate.City;

        var check = ConstructionRules.CanUpgrade(city, request.BuildingId);
        if (!check.IsAllowed)
        {
            return Applied.Refusal(check.Refusal, city, now);
        }

        var building = city.BuildingById(request.BuildingId)!;
        var cost = building.NextUpgradeCost!;

        city.Spend(cost);
        building.BeginUpgrade(now);

        // Storage buildings contribute nothing extra until the upgrade lands, but a
        // downstream recompute keeps capacity consistent with whatever just changed.
        city.RecomputeCapacity();

        return Applied.Success(city, building, now, "upgrade.started", cost);
    }

    private sealed record Applied(ConstructionResponse Response, AuditEntry? Audit)
    {
        public static Applied Refusal(ConstructionRefusal refusal, City city, DateTimeOffset now) =>
            new(ConstructionResponse.Refused(refusal, city.Balance.Coins, Snapshot(city), now), null);

        public static Applied Success(
            City city,
            BuildingInstance building,
            DateTimeOffset now,
            string kind,
            BuildCost cost)
        {
            var response = new ConstructionResponse(
                Accepted: true,
                RefusalCode: null,
                RefusalMessage: null,
                Building: Map(building, now),
                BalanceCoins: city.Balance.Coins,
                Resources: Snapshot(city),
                ServerTimeUtc: now);

            var audit = new AuditEntry(
                PlayerId: Guid.Empty,   // filled in by the caller, which knows the identity
                CityId: Guid.Empty,
                Kind: kind,
                MoneyDeltaCent: -cost.Coins.Cent,
                BalanceAfterCent: city.Balance.Cent,
                ResourceDeltas: cost.Resources.ToDictionary(r => r.Resource.Value, r => -r.Quantity),
                CorrelationId: null,
                IdempotencyKey: null,
                Metadata: new Dictionary<string, string>
                {
                    ["buildingId"] = building.Id,
                    ["definitionId"] = building.Definition.Id,
                    ["targetLevel"] = building.PendingLevel.ToString(),
                });

            return new Applied(response, audit);
        }
    }

    private static BuildingDto Map(BuildingInstance building, DateTimeOffset now) =>
        new(
            building.Id,
            building.Definition.Id,
            building.Col,
            building.Row,
            building.Level,
            building.State.ToString(),
            building.HaltReason == HaltReason.None ? null : building.HaltReason.ToString(),
            building.CompletesAtUtc,
            building.PendingLevel,
            building.ConstructionProgress(now));

    private static IReadOnlyList<ResourceBalanceDto> Snapshot(City city)
    {
        var capacity = city.Inventory.CapacityPerResource;
        return city.Inventory
            .Snapshot()
            .OrderBy(pair => pair.Key.Value, StringComparer.Ordinal)
            .Select(pair => new ResourceBalanceDto(pair.Key.Value, pair.Value, capacity))
            .ToArray();
    }
}
