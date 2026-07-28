using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tradeborn.Infrastructure.Persistence;
using Tradeborn.Infrastructure.Seed;

namespace Tradeborn.Infrastructure;

public static class TradebornInitializer
{
    /// <summary>
    /// Applies migrations, seeds the catalog, and loads it into memory.
    /// </summary>
    /// <remarks>
    /// Ordering is not incidental: migrations must run before the seeder (tables must exist),
    /// and the seeder before the catalog load (there would be nothing to read). Running
    /// migrations at startup suits a single-instance deployment; when this scales out it
    /// moves to a deploy step, which is why it lives behind one call.
    /// </remarks>
    public static async Task InitialiseTradebornAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Tradeborn.Startup");
        var db = scope.ServiceProvider.GetRequiredService<TradebornDbContext>();

        logger.LogInformation("Applying database migrations…");
        await db.Database.MigrateAsync(cancellationToken);

        var seeder = scope.ServiceProvider.GetRequiredService<GameCatalogSeeder>();
        await seeder.SeedAsync(cancellationToken);

        var catalog = await GameCatalog.LoadAsync(db, cancellationToken);
        scope.ServiceProvider.GetRequiredService<GameCatalogHolder>().Set(catalog);

        logger.LogInformation(
            "Tradeborn ready: {Buildings} building definitions, {Resources} resources loaded.",
            catalog.Buildings.Count, catalog.Resources.Count);
    }
}
