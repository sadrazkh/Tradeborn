using Tradeborn.Domain.Common;

namespace Tradeborn.Domain.Progression;

/// <summary>XP awards from docs/economy/ECONOMY_DESIGN.md §9.</summary>
public static class XpAwards
{
    public const long PerBuildingLevelConstructed = 10;
    public const long PerBuildingLevelUpgraded = 25;

    /// <summary>Coins of sale proceeds that earn one XP.</summary>
    public const long CoinsPerSaleXp = 20;

    public static long ForConstruction(int level) => PerBuildingLevelConstructed * level;

    public static long ForUpgrade(int newLevel) => PerBuildingLevelUpgraded * newLevel;

    /// <summary>
    /// XP for a sale, from the <b>net</b> proceeds.
    /// </summary>
    /// <remarks>
    /// Net rather than gross, so the transaction fee is not quietly refunded as progression.
    /// A player who churns trades should not out-level one who builds.
    /// </remarks>
    public static long ForSale(Money netProceeds) => netProceeds.Coins / CoinsPerSaleXp;
}

/// <summary>
/// A player's level and experience.
/// </summary>
/// <remarks>
/// XP is spent on levelling rather than accumulated forever: reaching a threshold consumes it
/// and the remainder carries into the next level. That keeps the "XP to next level" number the
/// player sees small and legible instead of growing into the tens of thousands.
/// </remarks>
public sealed class PlayerProgress
{
    public PlayerProgress(int level, long xp)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(xp);

        Level = level;
        Xp = xp;
    }

    public int Level { get; private set; }
    public long Xp { get; private set; }

    /// <summary>
    /// XP needed to leave <paramref name="level"/>.
    /// </summary>
    /// <remarks>
    /// <c>100 × 1.5^(level-1)</c> → 100, 150, 225, 338, 506 … Computed by exact repeated
    /// multiplication rather than <see cref="Math.Pow"/>, for the same reason every other
    /// curve in this domain is: no binary floating point in the economy.
    /// </remarks>
    public static long XpForLevel(int level)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);

        var required = 100m;
        for (var i = 1; i < level; i++)
        {
            required *= 1.5m;
        }

        return (long)required;
    }

    public long XpToNextLevel => XpForLevel(Level) - Xp;

    /// <summary>
    /// Adds XP and applies any level-ups.
    /// </summary>
    /// <returns>How many levels were gained, so the caller can celebrate each one.</returns>
    public int AddXp(long amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);

        Xp += amount;
        var gained = 0;

        // A loop, not a single check: one large reward can legitimately cross several
        // thresholds at once, and swallowing the extra levels would lose the player's progress.
        while (Xp >= XpForLevel(Level))
        {
            Xp -= XpForLevel(Level);
            Level++;
            gained++;
        }

        return gained;
    }
}
