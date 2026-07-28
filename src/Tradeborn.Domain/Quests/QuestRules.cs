namespace Tradeborn.Domain.Quests;

/// <summary>Why a reward could not be claimed.</summary>
public enum QuestRefusal
{
    None = 0,
    UnknownQuest,
    NotComplete,
    AlreadyClaimed,
}

public readonly record struct QuestCheck(QuestRefusal Refusal)
{
    public static QuestCheck Allowed => new(QuestRefusal.None);
    public static QuestCheck Refused(QuestRefusal reason) => new(reason);

    public bool IsAllowed => Refusal == QuestRefusal.None;
}

/// <summary>How one quest stands for one player.</summary>
public sealed record QuestStatus(
    QuestDefinition Definition,
    bool IsComplete,
    bool IsClaimed)
{
    /// <summary>Done, but the player has not taken the reward yet.</summary>
    public bool IsClaimable => IsComplete && !IsClaimed;
}

/// <summary>
/// The authority on quest completion and claiming.
/// </summary>
/// <remarks>
/// Completion is derived from city state every time it is asked; the only thing stored is
/// <i>which rewards have been paid</i>. That is the smallest possible amount of state, and it
/// is the piece that actually has to be durable — a reward paid twice is real money.
/// </remarks>
public static class QuestRules
{
    /// <summary>Every quest with its current standing, in chain order.</summary>
    public static IReadOnlyList<QuestStatus> Evaluate(
        QuestContext context,
        IReadOnlySet<string> claimedQuestIds) =>
        QuestChain.All
            .OrderBy(q => q.Order)
            .Select(q => new QuestStatus(q, q.IsComplete(context), claimedQuestIds.Contains(q.Id)))
            .ToArray();

    /// <summary>
    /// The step the player should be looking at.
    /// </summary>
    /// <remarks>
    /// Claimable first, so a finished quest is never buried under the next instruction — the
    /// reward is the payoff and it has to be the thing on screen. Otherwise the first
    /// unfinished step. Null once the chain is done and guidance should stop
    /// (PLAYER_JOURNEY.md 7:00).
    /// </remarks>
    public static QuestStatus? Current(IReadOnlyList<QuestStatus> statuses) =>
        statuses.FirstOrDefault(s => s.IsClaimable)
        ?? statuses.FirstOrDefault(s => !s.IsClaimed);

    public static QuestCheck CanClaim(
        QuestContext context,
        string questId,
        IReadOnlySet<string> claimedQuestIds)
    {
        var definition = QuestChain.ById(questId);
        if (definition is null)
        {
            return QuestCheck.Refused(QuestRefusal.UnknownQuest);
        }

        if (claimedQuestIds.Contains(questId))
        {
            return QuestCheck.Refused(QuestRefusal.AlreadyClaimed);
        }

        if (!definition.IsComplete(context))
        {
            return QuestCheck.Refused(QuestRefusal.NotComplete);
        }

        return QuestCheck.Allowed;
    }
}
