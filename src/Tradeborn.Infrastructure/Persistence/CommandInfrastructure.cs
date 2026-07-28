using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Tradeborn.Application.Abstractions;

namespace Tradeborn.Infrastructure.Persistence;

/// <summary>
/// Transaction boundary for economic commands, backed by the EF Core connection.
/// </summary>
/// <remarks>
/// Scoped, so every participant in one request shares the same <see cref="DbContext"/> and
/// therefore the same transaction. If a handler returns without committing — a refusal, an
/// exception, a lost idempotency race — disposal rolls the transaction back and nothing was
/// applied.
/// </remarks>
public sealed class UnitOfWork(TradebornDbContext db) : IUnitOfWork
{
    private IDbContextTransaction? transaction;

    public async Task<IAsyncDisposable> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        // Tests and nested calls may already be inside a transaction; joining it keeps this
        // safe to call unconditionally.
        if (db.Database.CurrentTransaction is not null)
        {
            return new NoOpScope();
        }

        transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        return transaction;
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class NoOpScope : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>
/// Idempotency keys stored in PostgreSQL.
/// </summary>
/// <remarks>
/// PostgreSQL is the system of record here, not Redis. A cache in front of this would cut
/// latency, but losing a key would allow a double-charge — so correctness lives in the
/// database and Redis remains an optimisation (DECISIONS_REQUIRED.md A-01).
/// </remarks>
public sealed class IdempotencyStore(TradebornDbContext db, TimeProvider timeProvider) : IIdempotencyStore
{
    public async Task<string?> TryGetResponseAsync(
        Guid playerId,
        string key,
        string operation,
        CancellationToken cancellationToken = default)
    {
        var row = await db.IdempotencyKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(k => k.PlayerId == playerId && k.Key == key, cancellationToken);

        if (row is null)
        {
            return null;
        }

        // Same key, different command. Replaying the first command's response would be
        // actively wrong, so this is surfaced as a client error rather than silently answered.
        if (!string.Equals(row.Operation, operation, StringComparison.Ordinal))
        {
            throw new IdempotencyConflictException(key, row.Operation, operation);
        }

        return row.ResponseBody;
    }

    public async Task<bool> TryRecordAsync(
        Guid playerId,
        string key,
        string operation,
        string responseBody,
        CancellationToken cancellationToken = default)
    {
        db.IdempotencyKeys.Add(new IdempotencyKeyEntity
        {
            PlayerId = playerId,
            Key = key,
            Operation = operation,
            StatusCode = 200,
            ResponseBody = responseBody,
            CreatedAtUtc = timeProvider.GetUtcNow(),
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // A concurrent duplicate of the same request inserted first. The unique
            // constraint IS the concurrency control — checking-then-inserting would leave a
            // window between the two.
            return false;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}

public sealed class IdempotencyConflictException(string key, string existingOperation, string attemptedOperation)
    : Exception($"Idempotency key '{key}' was already used for '{existingOperation}', not '{attemptedOperation}'.")
{
    public string Key { get; } = key;
}

/// <summary>
/// Append-only audit ledger (ADR-004).
/// </summary>
/// <remarks>
/// Entries are added to the same <see cref="DbContext"/> as the command, so they commit
/// atomically with it. An audit trail written in a separate transaction would drift from the
/// balances it is supposed to explain.
/// </remarks>
public sealed class AuditLog(TradebornDbContext db, TimeProvider timeProvider) : IAuditLog
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        db.AuditLedger.Add(new AuditLedgerEntity
        {
            PlayerId = entry.PlayerId,
            CityId = entry.CityId,
            OccurredAtUtc = timeProvider.GetUtcNow(),
            Kind = entry.Kind,
            MoneyDeltaCent = entry.MoneyDeltaCent,
            BalanceAfterCent = entry.BalanceAfterCent,
            ResourceDeltas = JsonSerializer.Serialize(entry.ResourceDeltas, Json),
            CorrelationId = entry.CorrelationId,
            IdempotencyKey = entry.IdempotencyKey,
            Metadata = JsonSerializer.Serialize(entry.Metadata ?? new Dictionary<string, string>(), Json),
        });

        return Task.CompletedTask;
    }
}
