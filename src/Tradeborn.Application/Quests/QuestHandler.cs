using System.Text.Json;
using Tradeborn.Application.Abstractions;
using Tradeborn.Application.Contracts;
using Tradeborn.Domain.Cities;
using Tradeborn.Domain.Production;
using Tradeborn.Domain.Progression;
using Tradeborn.Domain.Quests;

namespace Tradeborn.Application.Quests;

/// <summary>
/// Lists tutorial quests and pays their rewards.
/// </summary>
/// <remarks>
/// <para>
/// Claiming is a deliberate act rather than an automatic grant. That is a design choice, not a
/// technical one: the moment of taking the reward is where the coin-fly and the level-up land
/// (PLAYER_JOURNEY.md), and a reward that appears silently while the player is looking
/// elsewhere is a reward they never felt.
/// </para>
/// <para>
/// It is also what makes double-payment impossible to express: the claim is a row insert
/// guarded by a primary key, so a replayed or raced request collides rather than pays twice
/// (SECURITY_MODEL.md T5).
/// </para>
/// </remarks>
public sealed class QuestHandler(
    ICityStore cityStore,
    IQuestStore questStore,
    IPlayerStore playerStore,
    IUnitOfWork unitOfWork,
    IIdempotencyStore idempotency,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Operation = "quest.claim";

    /// <summary>Returns null when the player has no city.</summary>
    public async Task<QuestBoardDto?> GetBoardAsync(
        Guid playerId,
        CancellationToken cancellationToken = default)
    {
        var aggregate = await cityStore.LoadAsync(playerId, cancellationToken);
        if (aggregate is null)
        {
            return null;
        }

        // Settling first matters: a cart that landed while the player was away is what
        // completes the delivery quest, and they should find it ready to claim on arrival.
        SettlementEngine.Settle(aggregate.City, timeProvider.GetUtcNow());

        var claimed = await questStore.LoadClaimedAsync(playerId, cancellationToken);
        var statuses = QuestRules.Evaluate(ContextFor(aggregate.City), claimed);

        return Map(statuses);
    }

    /// <summary>Returns null when the player has no city.</summary>
    public async Task<ClaimQuestResponse?> ClaimAsync(
        Guid playerId,
        string questId,
        string idempotencyKey,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var replay = await idempotency.TryGetResponseAsync(playerId, idempotencyKey, Operation, cancellationToken);
        if (replay is not null)
        {
            return JsonSerializer.Deserialize<ClaimQuestResponse>(replay, Json);
        }

        var aggregate = await cityStore.LoadForUpdateAsync(playerId, cancellationToken);
        if (aggregate is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        SettlementEngine.Settle(aggregate.City, now);

        var city = aggregate.City;
        var progress = await playerStore.LoadProgressAsync(playerId, cancellationToken)
            ?? new PlayerProgress(1, 0);

        var claimed = await questStore.LoadClaimedAsync(playerId, cancellationToken);
        var check = QuestRules.CanClaim(ContextFor(city), questId, claimed);

        ClaimQuestResponse response;

        if (!check.IsAllowed)
        {
            response = ClaimQuestResponse.Refused(check.Refusal, questId, city, progress, now, []);
        }
        else
        {
            var quest = QuestChain.ById(questId)!;

            // The insert is the guard. Checking-then-inserting would leave a window in which
            // two concurrent claims both pass the check.
            var recorded = await questStore.TryRecordClaimAsync(playerId, questId, now, cancellationToken);

            if (!recorded)
            {
                response = ClaimQuestResponse.Refused(
                    QuestRefusal.AlreadyClaimed, questId, city, progress, now, []);
            }
            else
            {
                city.Credit(quest.RewardCoins);
                var levelsGained = progress.AddXp(quest.RewardXp);

                await playerStore.SaveProgressAsync(playerId, progress, cancellationToken);

                await auditLog.AppendAsync(
                    new AuditEntry(
                        PlayerId: playerId,
                        CityId: aggregate.Id,
                        Kind: "quest.claimed",
                        MoneyDeltaCent: quest.RewardCoins.Cent,
                        BalanceAfterCent: city.Balance.Cent,
                        ResourceDeltas: new Dictionary<string, long>(),
                        CorrelationId: correlationId,
                        IdempotencyKey: idempotencyKey,
                        Metadata: new Dictionary<string, string>
                        {
                            ["questId"] = questId,
                            ["rewardXp"] = quest.RewardXp.ToString(),
                        }),
                    cancellationToken);

                var updated = QuestRules.Evaluate(
                    ContextFor(city),
                    new HashSet<string>(claimed, StringComparer.Ordinal) { questId });

                response = new ClaimQuestResponse(
                    Accepted: true,
                    RefusalCode: null,
                    RefusalMessage: null,
                    QuestId: questId,
                    RewardCoins: quest.RewardCoins.Coins,
                    RewardXp: quest.RewardXp,
                    BalanceCoins: city.Balance.Coins,
                    PlayerLevel: progress.Level,
                    PlayerXp: progress.Xp,
                    XpToNextLevel: progress.XpToNextLevel,
                    LevelsGained: levelsGained,
                    Board: Map(updated),
                    ServerTimeUtc: now);
            }
        }

        await cityStore.SaveAsync(aggregate, cancellationToken);

        var stored = await idempotency.TryRecordAsync(
            playerId, idempotencyKey, Operation, JsonSerializer.Serialize(response, Json), cancellationToken);

        if (!stored)
        {
            var previous = await idempotency.TryGetResponseAsync(
                playerId, idempotencyKey, Operation, cancellationToken);

            return previous is null ? response : JsonSerializer.Deserialize<ClaimQuestResponse>(previous, Json);
        }

        await unitOfWork.CommitAsync(cancellationToken);
        return response;
    }

    private static QuestContext ContextFor(City city) =>
        new(city, city.DeliveriesCompleted, city.SalesCompleted);

    private static QuestBoardDto Map(IReadOnlyList<QuestStatus> statuses)
    {
        var current = QuestRules.Current(statuses);

        return new QuestBoardDto(
            current is null ? null : ToDto(current),
            statuses.Select(ToDto).ToArray(),
            statuses.Count(s => s.IsClaimed),
            statuses.Count);
    }

    private static QuestDto ToDto(QuestStatus status) =>
        new(
            status.Definition.Id,
            status.Definition.Order,
            status.Definition.Title,
            status.Definition.Hint,
            status.Definition.RewardCoins.Coins,
            status.Definition.RewardXp,
            status.IsComplete,
            status.IsClaimed,
            status.IsClaimable);
}
