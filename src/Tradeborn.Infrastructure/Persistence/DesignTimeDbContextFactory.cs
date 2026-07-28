using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Tradeborn.Infrastructure.Persistence;

/// <summary>
/// Builds a <see cref="TradebornDbContext"/> for `dotnet ef` commands.
/// </summary>
/// <remarks>
/// <para>
/// Without this, EF tools try to construct the web host's service provider to find the
/// context. That drags the whole application — configuration, authentication, the startup
/// initialiser — into what should be a schema-only operation, and it fails as soon as the
/// host needs a secret or a running database.
/// </para>
/// <para>
/// Migrations only need a provider and a connection string, and the connection string is
/// only used to pick the right SQL dialect — <c>migrations add</c> never connects. Override
/// it with <c>TRADEBORN_DESIGNTIME_POSTGRES</c> when scaffolding against a specific server.
/// </para>
/// </remarks>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TradebornDbContext>
{
    private const string FallbackConnectionString =
        "Host=localhost;Port=5432;Database=tradeborn;Username=postgres;Password=postgres";

    public TradebornDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("TRADEBORN_DESIGNTIME_POSTGRES")
            ?? FallbackConnectionString;

        var options = new DbContextOptionsBuilder<TradebornDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new TradebornDbContext(options);
    }
}
