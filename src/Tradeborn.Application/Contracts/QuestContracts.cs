using Tradeborn.Domain.Cities;
using Tradeborn.Domain.Progression;
using Tradeborn.Domain.Quests;

namespace Tradeborn.Application.Contracts;

public sealed record ClaimQuestRequest(string QuestId);

/// <summary>One tutorial step as the HUD sees it.</summary>
public sealed record QuestDto(
    string Id,
    int Order,
    string Title,
    string Hint,
    long RewardCoins,
    long RewardXp,
    bool IsComplete,
    bool IsClaimed,
    bool IsClaimable);

/// <summary>
/// The tutorial chain.
/// </summary>
/// <remarks>
/// <see cref="Current"/> is the only quest the HUD shows. Presenting all seven at once would
/// turn a contextual tutorial into a checklist, which is exactly the "wall of text" that
/// PLAYER_JOURNEY.md forbids. Null once the chain is finished and guidance should stop.
/// </remarks>
public sealed record QuestBoardDto(
    QuestDto? Current,
    IReadOnlyList<QuestDto> All,
    int Claimed,
    int Total);

public sealed record ClaimQuestResponse(
    bool Accepted,
    string? RefusalCode,
    string? RefusalMessage,
    string QuestId,
    long RewardCoins,
    long RewardXp,
    long BalanceCoins,
    int PlayerLevel,
    long PlayerXp,
    long XpToNextLevel,
    int LevelsGained,
    QuestBoardDto? Board,
    DateTimeOffset ServerTimeUtc)
{
    public static ClaimQuestResponse Refused(
        QuestRefusal refusal,
        string questId,
        City city,
        PlayerProgress progress,
        DateTimeOffset now,
        IReadOnlyList<QuestDto> all) =>
        new(false, refusal.ToString(), QuestRefusalMessages.For(refusal), questId,
            0, 0, city.Balance.Coins,
            progress.Level, progress.Xp, progress.XpToNextLevel, 0,
            new QuestBoardDto(null, all, 0, all.Count), now);
}

public static class QuestRefusalMessages
{
    public static string For(QuestRefusal refusal) => refusal switch
    {
        QuestRefusal.UnknownQuest => "That task does not exist.",
        QuestRefusal.NotComplete => "That task is not finished yet.",
        QuestRefusal.AlreadyClaimed => "You have already collected that reward.",
        _ => "That reward cannot be collected right now.",
    };
}
