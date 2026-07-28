using Tradeborn.Domain.Buildings;
using Tradeborn.Domain.Cities;
using Tradeborn.Domain.Common;

namespace Tradeborn.Domain.Quests;

/// <summary>
/// One step of the tutorial chain.
/// </summary>
/// <remarks>
/// <para>
/// <b>Copy is capped at one short sentence.</b> PLAYER_JOURNEY.md sets a hard limit of twelve
/// words per step: the tutorial teaches by doing, and a step that needs a paragraph is a badly
/// designed step, not a step that needs more text.
/// </para>
/// <para>
/// Completion is a <see cref="Func{T,TResult}"/> over city state rather than stored progress.
/// Nothing to keep in sync, nothing to migrate when a condition changes, and a player who
/// happens to build a Sawmill before being asked simply finds that quest already done.
/// </para>
/// </remarks>
public sealed record QuestDefinition(
    string Id,
    int Order,
    string Title,
    string Hint,
    Money RewardCoins,
    long RewardXp,
    Func<QuestContext, bool> IsComplete);

/// <summary>Everything the completion conditions are allowed to look at.</summary>
public sealed record QuestContext(City City, long DeliveriesCompleted, long SalesCompleted);

/// <summary>
/// The seven-quest tutorial chain from docs/economy/ECONOMY_DESIGN.md §11.
/// </summary>
/// <remarks>
/// Rewards total 1 200 coins and 330 XP — roughly five times passive income over the first
/// fifteen minutes. That is deliberate: it carries early pacing so build times can stay short
/// without inflating the steady-state economy (BALANCE_ASSUMPTIONS A-7).
/// </remarks>
public static class QuestChain
{
    public static IReadOnlyList<QuestDefinition> All { get; } =
    [
        new(
            "build_lumber_camp", 1,
            "Your town needs wood.",
            "Build a Lumber Camp.",
            Money.FromCoins(50), 20,
            ctx => ctx.City.Buildings.Any(b => b.Definition.Id == "lumber_camp")),

        new(
            "start_production", 2,
            "Start cutting wood.",
            "Tap your Lumber Camp and start production.",
            Money.FromCoins(50), 20,
            // Halted counts: the player made the decision, the world just got in the way.
            ctx => ctx.City.Buildings.Any(b =>
                b.Definition.Recipe is not null &&
                b.State is BuildingState.Producing or BuildingState.Halted)),

        new(
            "build_warehouse", 3,
            "Wood needs somewhere to go.",
            "Build a Warehouse.",
            Money.FromCoins(100), 30,
            ctx => ctx.City.Buildings.Any(b => b.Definition.Id == "warehouse")),

        new(
            "first_delivery", 4,
            "Watch your first delivery arrive.",
            "A cart will bring your wood to storage.",
            Money.FromCoins(100), 30,
            ctx => ctx.DeliveriesCompleted > 0),

        new(
            "first_sale", 5,
            "Turn goods into coins.",
            "Sell something at the Market.",
            Money.FromCoins(200), 50,
            ctx => ctx.SalesCompleted > 0),

        new(
            "first_upgrade", 6,
            "Make something better.",
            "Upgrade any building.",
            Money.FromCoins(300), 80,
            // PendingLevel counts, so the reward lands when the player commits rather than
            // making them wait out the build timer for their congratulations.
            ctx => ctx.City.Buildings.Any(b => b.Level > 1 || b.PendingLevel > b.Level)),

        new(
            "build_sawmill", 7,
            "Raw wood is cheap. Planks are not.",
            "Build a Sawmill.",
            Money.FromCoins(400), 100,
            ctx => ctx.City.Buildings.Any(b => b.Definition.Id == "sawmill")),
    ];

    public static QuestDefinition? ById(string id) =>
        All.FirstOrDefault(q => string.Equals(q.Id, id, StringComparison.Ordinal));

    public static Money TotalCoins => All.Aggregate(Money.Zero, (sum, q) => sum + q.RewardCoins);

    public static long TotalXp => All.Sum(q => q.RewardXp);
}
