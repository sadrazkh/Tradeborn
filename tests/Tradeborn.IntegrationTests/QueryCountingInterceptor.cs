using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Tradeborn.IntegrationTests;

/// <summary>
/// Counts SQL commands so tests can assert query counts instead of eyeballing them.
/// </summary>
/// <remarks>
/// PERFORMANCE_BUDGET.md §6 requires the city read path to be free of N+1 queries, and
/// TEST_STRATEGY.md §3 requires that to be <i>asserted</i>. An N+1 is invisible in review and
/// in local testing — it only shows up as latency once a player has twenty buildings — so the
/// only way to keep it out is to count.
/// </remarks>
public sealed class QueryCountingInterceptor : DbCommandInterceptor
{
    private int count;
    private bool counting;

    public int Count => Volatile.Read(ref count);

    /// <summary>Starts counting and resets the total. Disposing stops it.</summary>
    public IDisposable Measure()
    {
        Interlocked.Exchange(ref count, 0);
        counting = true;
        return new Stopper(this);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        Record();
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Record();
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void Record()
    {
        if (counting)
        {
            Interlocked.Increment(ref count);
        }
    }

    private sealed class Stopper(QueryCountingInterceptor owner) : IDisposable
    {
        public void Dispose() => owner.counting = false;
    }
}
