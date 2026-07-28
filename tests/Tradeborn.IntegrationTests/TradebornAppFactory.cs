using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tradeborn.Infrastructure.Persistence;
using Xunit;

namespace Tradeborn.IntegrationTests;

/// <summary>
/// Database provisioning for integration tests.
/// </summary>
/// <remarks>
/// Docker is not installed on the development machine (RISKS.md R-09), so Testcontainers
/// cannot be the only path. Resolution order:
/// <list type="number">
/// <item><c>TRADEBORN_TEST_POSTGRES</c> if set — the local-dev and CI path.</item>
/// <item>Otherwise the test is <b>skipped with a message</b>, never silently passed.</item>
/// </list>
/// A skipped test that looks green is worse than no test, because it removes the pressure
/// to fix the environment.
/// </remarks>
public static class TestDatabase
{
    public const string EnvironmentVariable = "TRADEBORN_TEST_POSTGRES";

    public static string? ConnectionString =>
        Environment.GetEnvironmentVariable(EnvironmentVariable);

    public static bool IsAvailable => !string.IsNullOrWhiteSpace(ConnectionString);

    public const string SkipReason =
        "No test database. Set TRADEBORN_TEST_POSTGRES to a PostgreSQL connection string " +
        "(see docs/operations/LOCAL_DEVELOPMENT.md). CI always sets it.";
}

/// <summary>A <see cref="FactAttribute"/> that skips instead of failing when no database is configured.</summary>
public sealed class RequiresPostgresFactAttribute : FactAttribute
{
    public RequiresPostgresFactAttribute()
    {
        if (!TestDatabase.IsAvailable)
        {
            Skip = TestDatabase.SkipReason;
        }
    }
}

public sealed class TradebornAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = TestDatabase.ConnectionString,
                // Deterministic, and obviously not a production value.
                ["Tradeborn:Auth:SigningKey"] = "integration-test-signing-key-at-least-32-chars",
            });
        });
    }

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>
    /// Drops the schema between runs so every test class starts from an empty database and
    /// exercises the migration path rather than whatever a previous run happened to leave.
    /// </summary>
    public async Task ResetAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TradebornDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
    }
}
