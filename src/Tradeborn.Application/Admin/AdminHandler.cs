using Tradeborn.Application.Abstractions;
using Tradeborn.Application.Contracts;
using Tradeborn.Domain.Cities;
using Tradeborn.Domain.Common;
using Tradeborn.Domain.Economy;
using Tradeborn.Domain.Production;

namespace Tradeborn.Application.Admin;

/// <summary>
/// Operator actions that change a player's city.
/// </summary>
/// <remarks>
/// <para>
/// These run through the same locked, settled, audited pipeline as player commands — an
/// operator granting coins must not be able to race a player's own sale into a corrupt
/// balance.
/// </para>
/// <para>
/// The difference is the audit entry: it records <b>who did it</b> as well as whose city it
/// touched. Without that the ledger says a balance rose by 5 000 and nothing about the person
/// responsible, which is exactly the question an audit exists to answer.
/// </para>
/// </remarks>
public sealed class AdminHandler(
    ICityStore cityStore,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    IGameCatalog catalog,
    TimeProvider timeProvider)
{
    /// <summary>Bounded on purpose — see <see cref="GrantRequest"/>.</summary>
    private const long MaxGrantCoins = 100_000;

    private const long MaxGrantQuantity = 10_000;

    /// <summary>Returns null when the target player has no city.</summary>
    public async Task<AdminActionResponse?> GrantAsync(
        Guid actorId,
        Guid targetPlayerId,
        GrantRequest request,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Rejected("A reason is required. An unexplained grant is indistinguishable from abuse.");
        }

        if (request.Coins < 0 || request.Coins > MaxGrantCoins)
        {
            return Rejected($"Coins must be between 0 and {MaxGrantCoins:N0}.");
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var aggregate = await cityStore.LoadForUpdateAsync(targetPlayerId, cancellationToken);
        if (aggregate is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        SettlementEngine.Settle(aggregate.City, now);

        var city = aggregate.City;
        var deltas = new Dictionary<string, long>();

        if (request.Coins > 0)
        {
            city.Credit(Money.FromCoins(request.Coins));
        }

        foreach (var grant in request.Resources ?? [])
        {
            if (grant.Quantity is <= 0 or > MaxGrantQuantity)
            {
                continue;
            }

            // Unknown resources are skipped rather than created: the inventory is keyed by
            // resource id and a typo would otherwise mint a currency that nothing can spend.
            if (!catalog.Resources.Any(r => r.Id.Value == grant.Resource))
            {
                continue;
            }

            var resource = ResourceId.From(grant.Resource);
            var before = city.Inventory.Get(resource);
            city.Inventory.Add(resource, grant.Quantity);

            // Add clamps at capacity, so the audit records what actually landed rather than
            // what was asked for.
            deltas[grant.Resource] = city.Inventory.Get(resource) - before;
        }

        await cityStore.SaveAsync(aggregate, cancellationToken);

        await auditLog.AppendAsync(
            new AuditEntry(
                PlayerId: targetPlayerId,
                CityId: aggregate.Id,
                Kind: "admin.granted",
                MoneyDeltaCent: Money.FromCoins(request.Coins).Cent,
                BalanceAfterCent: city.Balance.Cent,
                ResourceDeltas: deltas,
                CorrelationId: correlationId,
                IdempotencyKey: null,
                Metadata: new Dictionary<string, string> { ["reason"] = request.Reason },
                ActorPlayerId: actorId),
            cancellationToken);

        await unitOfWork.CommitAsync(cancellationToken);

        return new AdminActionResponse(true, "Granted.", city.Balance.Coins, Snapshot(city), now);
    }

    /// <summary>
    /// Empties a city back to a clean slate for testing.
    /// </summary>
    /// <remarks>
    /// Deliberately does <b>not</b> delete buildings. Removing rows would take the plot layout,
    /// the audit trail's subjects and the quest history with it; zeroing balances gives a
    /// clean economic state to test against while leaving everything explicable afterwards.
    /// </remarks>
    public async Task<AdminActionResponse?> ResetEconomyAsync(
        Guid actorId,
        Guid targetPlayerId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Rejected("A reason is required.");
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var aggregate = await cityStore.LoadForUpdateAsync(targetPlayerId, cancellationToken);
        if (aggregate is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        SettlementEngine.Settle(aggregate.City, now);

        var city = aggregate.City;
        var before = city.Balance;
        var deltas = new Dictionary<string, long>();

        foreach (var (resource, quantity) in city.Inventory.Snapshot())
        {
            if (quantity > 0)
            {
                deltas[resource.Value] = -quantity;
                city.Inventory.Set(resource, 0);
            }
        }

        city.Debit(before);

        await cityStore.SaveAsync(aggregate, cancellationToken);

        await auditLog.AppendAsync(
            new AuditEntry(
                PlayerId: targetPlayerId,
                CityId: aggregate.Id,
                Kind: "admin.reset",
                MoneyDeltaCent: -before.Cent,
                BalanceAfterCent: 0,
                ResourceDeltas: deltas,
                CorrelationId: null,
                IdempotencyKey: null,
                Metadata: new Dictionary<string, string> { ["reason"] = reason },
                ActorPlayerId: actorId),
            cancellationToken);

        await unitOfWork.CommitAsync(cancellationToken);

        return new AdminActionResponse(true, "Economy reset.", 0, Snapshot(city), now);
    }

    private AdminActionResponse Rejected(string message) =>
        new(false, message, 0, [], timeProvider.GetUtcNow());

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
