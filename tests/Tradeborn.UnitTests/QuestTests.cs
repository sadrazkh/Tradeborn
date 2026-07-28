using Tradeborn.Domain.Buildings;
using Tradeborn.Domain.Cities;
using Tradeborn.Domain.Common;
using Tradeborn.Domain.Production;
using Tradeborn.Domain.Progression;
using Tradeborn.Domain.Quests;

namespace Tradeborn.UnitTests;

public class QuestTests
{
    private static readonly IReadOnlySet<string> NothingClaimed = new HashSet<string>(StringComparer.Ordinal);

    private static QuestContext Context(City city, long deliveries = 0, long sales = 0) =>
        new(city, deliveries, sales);

    // -----------------------------------------------------------------------------------
    // The chain matches the design
    // -----------------------------------------------------------------------------------

    [Fact]
    public void The_chain_pays_the_documented_totals()
    {
        // BALANCE_ASSUMPTIONS A-7: roughly 5x passive income over the first fifteen minutes,
        // which is what lets build times stay short without inflating the steady-state economy.
        Assert.Equal(7, QuestChain.All.Count);
        Assert.Equal(Money.FromCoins(1_200), QuestChain.TotalCoins);
        Assert.Equal(330, QuestChain.TotalXp);
    }

    [Fact]
    public void Every_hint_is_short_enough_to_read_at_a_glance()
    {
        // PLAYER_JOURNEY.md caps tutorial copy at twelve words per step. A step that needs a
        // paragraph is a badly designed step, and this is the cheapest place to catch that.
        foreach (var quest in QuestChain.All)
        {
            var words = quest.Hint.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            Assert.True(words <= 12, $"'{quest.Id}' hint is {words} words: \"{quest.Hint}\"");
        }
    }

    [Fact]
    public void Quest_order_is_contiguous_and_starts_at_one()
    {
        Assert.Equal(
            Enumerable.Range(1, QuestChain.All.Count),
            QuestChain.All.OrderBy(q => q.Order).Select(q => q.Order));
    }

    // -----------------------------------------------------------------------------------
    // Completion conditions
    // -----------------------------------------------------------------------------------

    [Fact]
    public void A_fresh_city_has_completed_nothing()
    {
        var statuses = QuestRules.Evaluate(Context(SliceEconomy.NewPlayerCity()), NothingClaimed);

        Assert.All(statuses, s => Assert.False(s.IsComplete));
        Assert.Equal("build_lumber_camp", QuestRules.Current(statuses)!.Definition.Id);
    }

    [Fact]
    public void Building_a_lumber_camp_completes_the_first_quest()
    {
        var city = SliceEconomy.NewPlayerCity();
        city.Add(BuildingInstance.PlaceNew("camp", SliceEconomy.LumberCamp, 1, 2, SliceEconomy.Epoch));

        var statuses = QuestRules.Evaluate(Context(city), NothingClaimed);

        Assert.True(statuses.Single(s => s.Definition.Id == "build_lumber_camp").IsComplete);
    }

    [Fact]
    public void A_halted_building_still_counts_as_started()
    {
        // The player made the decision; the world got in the way. Refusing them the reward
        // for a full warehouse would punish them for succeeding too well.
        var city = SliceEconomy.WithCapacity(0, (SliceEconomy.LumberCamp, 1));
        SettlementEngine.Settle(city, SliceEconomy.Epoch.AddHours(1));

        Assert.Equal(BuildingState.Halted, city.Buildings.Single(b => b.Definition.Id == "lumber_camp").State);

        var statuses = QuestRules.Evaluate(Context(city), NothingClaimed);
        Assert.True(statuses.Single(s => s.Definition.Id == "start_production").IsComplete);
    }

    [Fact]
    public void An_upgrade_counts_the_moment_it_is_committed()
    {
        // Waiting out the build timer before congratulating the player would put the reward
        // minutes away from the action that earned it.
        var city = SliceEconomy.WithBuildings((SliceEconomy.LumberCamp, 1));
        city.Buildings.Single(b => b.Definition.Id == "lumber_camp").BeginUpgrade(SliceEconomy.Epoch);

        var statuses = QuestRules.Evaluate(Context(city), NothingClaimed);

        Assert.True(statuses.Single(s => s.Definition.Id == "first_upgrade").IsComplete);
    }

    [Fact]
    public void The_delivery_quest_needs_a_delivery_that_actually_happened()
    {
        var city = SliceEconomy.WithBuildings((SliceEconomy.LumberCamp, 1));

        Assert.False(QuestRules
            .Evaluate(Context(city), NothingClaimed)
            .Single(s => s.Definition.Id == "first_delivery").IsComplete);

        SettlementEngine.Settle(city, SliceEconomy.Epoch.AddMinutes(5));

        Assert.True(city.DeliveriesCompleted > 0, "A cart should have arrived by now.");
        Assert.True(QuestRules
            .Evaluate(Context(city, city.DeliveriesCompleted), NothingClaimed)
            .Single(s => s.Definition.Id == "first_delivery").IsComplete);
    }

    [Fact]
    public void Selling_everything_does_not_undo_the_delivery_quest()
    {
        // The counter exists precisely for this: by inventory alone, a player who sold
        // everything looks identical to one who never received anything.
        var city = SliceEconomy.WithBuildings((SliceEconomy.LumberCamp, 1));
        SettlementEngine.Settle(city, SliceEconomy.Epoch.AddMinutes(5));

        var held = city.Inventory.Get(Tradeborn.Domain.Economy.ResourceId.From("wood"));
        city.Inventory.Remove(Tradeborn.Domain.Economy.ResourceId.From("wood"), held);

        Assert.True(QuestRules
            .Evaluate(Context(city, city.DeliveriesCompleted), NothingClaimed)
            .Single(s => s.Definition.Id == "first_delivery").IsComplete);
    }

    // -----------------------------------------------------------------------------------
    // Which step the player is shown
    // -----------------------------------------------------------------------------------

    [Fact]
    public void A_claimable_reward_outranks_the_next_instruction()
    {
        // The payoff has to be the thing on screen. Burying a finished quest under the next
        // instruction means the player never feels they earned anything.
        var city = SliceEconomy.NewPlayerCity();
        city.Add(BuildingInstance.PlaceNew("camp", SliceEconomy.LumberCamp, 1, 2, SliceEconomy.Epoch));

        var current = QuestRules.Current(QuestRules.Evaluate(Context(city), NothingClaimed));

        Assert.Equal("build_lumber_camp", current!.Definition.Id);
        Assert.True(current.IsClaimable);
    }

    [Fact]
    public void Guidance_stops_once_the_chain_is_finished()
    {
        // PLAYER_JOURNEY.md 7:00 — the tutorial ends and the player is on their own.
        var claimed = QuestChain.All.Select(q => q.Id).ToHashSet(StringComparer.Ordinal);

        var current = QuestRules.Current(
            QuestRules.Evaluate(Context(SliceEconomy.NewPlayerCity()), claimed));

        Assert.Null(current);
    }

    // -----------------------------------------------------------------------------------
    // Claiming
    // -----------------------------------------------------------------------------------

    [Fact]
    public void An_incomplete_quest_cannot_be_claimed()
    {
        var check = QuestRules.CanClaim(
            Context(SliceEconomy.NewPlayerCity()), "build_lumber_camp", NothingClaimed);

        Assert.Equal(QuestRefusal.NotComplete, check.Refusal);
    }

    [Fact]
    public void A_claimed_quest_cannot_be_claimed_again()
    {
        var city = SliceEconomy.NewPlayerCity();
        city.Add(BuildingInstance.PlaceNew("camp", SliceEconomy.LumberCamp, 1, 2, SliceEconomy.Epoch));

        var claimed = new HashSet<string>(StringComparer.Ordinal) { "build_lumber_camp" };
        var check = QuestRules.CanClaim(Context(city), "build_lumber_camp", claimed);

        Assert.Equal(QuestRefusal.AlreadyClaimed, check.Refusal);
    }

    [Fact]
    public void An_unknown_quest_is_refused()
    {
        var check = QuestRules.CanClaim(
            Context(SliceEconomy.NewPlayerCity()), "not_a_quest", NothingClaimed);

        Assert.Equal(QuestRefusal.UnknownQuest, check.Refusal);
    }

    // -----------------------------------------------------------------------------------
    // XP and levels
    // -----------------------------------------------------------------------------------

    [Theory]
    [InlineData(1, 100)]
    [InlineData(2, 150)]
    [InlineData(3, 225)]
    [InlineData(4, 337)]
    public void The_xp_curve_matches_the_design(int level, long required)
    {
        Assert.Equal(required, PlayerProgress.XpForLevel(level));
    }

    [Fact]
    public void Xp_levels_the_player_up_and_carries_the_remainder()
    {
        var progress = new PlayerProgress(1, 0);

        var gained = progress.AddXp(120);

        Assert.Equal(1, gained);
        Assert.Equal(2, progress.Level);
        Assert.Equal(20, progress.Xp);           // 120 - 100
        Assert.Equal(130, progress.XpToNextLevel); // 150 - 20
    }

    [Fact]
    public void One_large_reward_can_cross_several_levels_at_once()
    {
        // Swallowing the extra levels would silently lose the player's progress.
        var progress = new PlayerProgress(1, 0);

        var gained = progress.AddXp(1_000);

        Assert.True(gained >= 3, $"Expected several levels from 1000 XP, got {gained}.");
        Assert.True(progress.Xp < PlayerProgress.XpForLevel(progress.Level));
    }

    [Fact]
    public void The_whole_tutorial_chain_reaches_player_level_three()
    {
        // PLAYER_JOURNEY.md: the chain should land the player at level 3 by the time guidance
        // stops. If a reward is retuned and this breaks, the pacing changed.
        var progress = new PlayerProgress(1, 0);
        progress.AddXp(QuestChain.TotalXp);

        Assert.Equal(3, progress.Level);
    }

    [Fact]
    public void Sale_xp_comes_from_net_proceeds()
    {
        // Gross would quietly refund the transaction fee as progression, letting a player who
        // churns trades out-level one who builds.
        Assert.Equal(5, XpAwards.ForSale(Money.FromCoins(100)));
        Assert.Equal(0, XpAwards.ForSale(Money.FromCoins(19)));
    }
}
