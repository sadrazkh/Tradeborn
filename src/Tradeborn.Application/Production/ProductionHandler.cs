using System.Text.Json;
using Tradeborn.Application.Abstractions;
using Tradeborn.Application.Contracts;
using Tradeborn.Domain.Buildings;
using Tradeborn.Domain.Cities;
using Tradeborn.Domain.Production;

namespace Tradeborn.Application.Production;

/// <summary>
/// Starts and pauses production on a building.
/// </summary>
/// <remarks>
/// <para>
/// Follows the same write pipeline as construction (ARCHITECTURE.md §6): lock, settle,
/// validate, apply, audit, record idempotency, commit. Toggling production moves no money,
/// but it changes how much the city will earn, so it is a first-class economic command
/// rather than a client-side preference.
/// </para>
/// <para>
/// Settlement runs <b>before</b> the toggle so that pausing captures everything the building
/// had already produced up to this instant. Pausing first and settling afterwards would
/// silently discard the current partial cycle.
/// </para>
/// </remarks>
public sealed class ProductionHandler(
    ICityStore cityStore,
    IUnitOfWork unitOfWork,
    IIdempotencyStore idempotency,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Operation = "production.setActive";

    /// <summary>Returns null when the player has no city.</summary>
    public async Task<ProductionResponse?> SetActiveAsync(
        Guid playerId,
        string buildingId,
        bool active,
        string idempotencyKey,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var replay = await idempotency.TryGetResponseAsync(playerId, idempotencyKey, Operation, cancellationToken);
        if (replay is not null)
        {
            return JsonSerializer.Deserialize<ProductionResponse>(replay, Json);
        }

        var aggregate = await cityStore.LoadForUpdateAsync(playerId, cancellationToken);
        if (aggregate is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        SettlementEngine.Settle(aggregate.City, now);

        var city = aggregate.City;
        var check = ProductionRules.CanSetActive(city, buildingId, active);

        ProductionResponse response;

        if (!check.IsAllowed)
        {
            response = ProductionResponse.Refused(check.Refusal, now);
        }
        else
        {
            var building = city.BuildingById(buildingId)!;

            if (active)
            {
                building.StartProduction();
            }
            else
            {
                building.StopProduction();
            }

            response = new ProductionResponse(
                Accepted: true,
                RefusalCode: null,
                RefusalMessage: null,
                Building: Map(building, now),
                ServerTimeUtc: now);

            await auditLog.AppendAsync(
                new AuditEntry(
                    PlayerId: playerId,
                    CityId: aggregate.Id,
                    Kind: active ? "production.started" : "production.paused",
                    MoneyDeltaCent: 0,
                    BalanceAfterCent: city.Balance.Cent,
                    ResourceDeltas: new Dictionary<string, long>(),
                    CorrelationId: correlationId,
                    IdempotencyKey: idempotencyKey,
                    Metadata: new Dictionary<string, string>
                    {
                        ["buildingId"] = building.Id,
                        ["definitionId"] = building.Definition.Id,
                    }),
                cancellationToken);
        }

        // A refusal still persists the settlement that just ran — that production is real and
        // discarding it would roll back output the player had earned.
        await cityStore.SaveAsync(aggregate, cancellationToken);

        var recorded = await idempotency.TryRecordAsync(
            playerId, idempotencyKey, Operation, JsonSerializer.Serialize(response, Json), cancellationToken);

        if (!recorded)
        {
            // A concurrent duplicate won the race; abandoning this transaction is correct.
            var stored = await idempotency.TryGetResponseAsync(
                playerId, idempotencyKey, Operation, cancellationToken);

            return stored is null
                ? response
                : JsonSerializer.Deserialize<ProductionResponse>(stored, Json);
        }

        await unitOfWork.CommitAsync(cancellationToken);
        return response;
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
}
