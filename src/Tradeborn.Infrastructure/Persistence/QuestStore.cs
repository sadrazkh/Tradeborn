using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tradeborn.Application.Abstractions;

namespace Tradeborn.Infrastructure.Persistence;

/// <summary>
/// Which tutorial rewards a player has collected.
/// </summary>
/// <remarks>
/// The primary key on (PlayerId, QuestId) is the anti-double-claim mechanism, not merely a
/// constraint that happens to be there. Checking "has this been claimed?" and then inserting
/// would leave a window between the two in which a raced or replayed request could pay twice —
/// so the insert itself is the check (SECURITY_MODEL.md T5).
/// </remarks>
public sealed class QuestStore(TradebornDbContext db) : IQuestStore
{
    public async Task<IReadOnlySet<string>> LoadClaimedAsync(
        Guid playerId,
        CancellationToken cancellationToken = default)
    {
        var ids = await db.PlayerQuests
            .AsNoTracking()
            .Where(q => q.PlayerId == playerId)
            .Select(q => q.QuestId)
            .ToListAsync(cancellationToken);

        return ids.ToHashSet(StringComparer.Ordinal);
    }

    public async Task<bool> TryRecordClaimAsync(
        Guid playerId,
        string questId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken = default)
    {
        db.PlayerQuests.Add(new PlayerQuestEntity
        {
            PlayerId = playerId,
            QuestId = questId,
            ClaimedAtUtc = atUtc,
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Already claimed. The collision is the answer, so the entry is detached and the
            // caller refuses cleanly rather than the whole transaction blowing up.
            db.ChangeTracker.Clear();
            return false;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
