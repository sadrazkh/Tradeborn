using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tradeborn.Application.Abstractions;
using Tradeborn.Application.Cities;
using Tradeborn.Application.Construction;
using Tradeborn.Application.Market;
using Tradeborn.Application.Production;
using Tradeborn.Application.Admin;
using Tradeborn.Application.Quests;
using Tradeborn.Domain.Buildings;
using Tradeborn.Infrastructure.Identity;
using Tradeborn.Infrastructure.Persistence;
using Tradeborn.Infrastructure.Seed;

namespace Tradeborn.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddTradebornInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException(
                "Connection string 'Postgres' is not configured. See docs/operations/LOCAL_DEVELOPMENT.md.");

        services.AddDbContext<TradebornDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(3));

            // Loud in development, quiet in production: a mis-tracked entity should fail a
            // developer's build rather than silently corrupt a balance later.
            options.EnableDetailedErrors(configuration.GetValue("Tradeborn:DetailedDbErrors", false));
        });

        services.AddSingleton(TimeProvider.System);

        services.AddSingleton<GameCatalogHolder>();
        services.AddSingleton<IGameCatalog>(sp => sp.GetRequiredService<GameCatalogHolder>());

        services.AddScoped<ICityStore, CityStore>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<CityProvisioner>();
        services.AddScoped<GameCatalogSeeder>();
        services.AddScoped<GetCityHandler>();
        services.AddScoped<ConstructionHandler>();
        services.AddScoped<ProductionHandler>();
        services.AddScoped<IMarketStore, MarketStore>();
        services.AddScoped<IPlayerStore, PlayerStore>();
        services.AddScoped<MarketHandler>();
        services.AddScoped<IQuestStore, QuestStore>();
        services.AddScoped<QuestHandler>();
        services.AddScoped<IAdminStore, AdminStore>();
        services.AddScoped<AdminHandler>();

        var auth = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
        if (string.IsNullOrWhiteSpace(auth.SigningKey) || auth.SigningKey.Length < 32)
        {
            throw new InvalidOperationException(
                $"'{AuthOptions.SectionName}:SigningKey' must be configured and at least 32 characters. " +
                "Never commit it — use user secrets locally and environment variables in production " +
                "(docs/architecture/SECURITY_MODEL.md §8).");
        }

        services.AddSingleton(auth);
        services.AddScoped<AuthService>();

        // Redis is optional until Phase 3 (DECISIONS_REQUIRED.md A-01). It is not running on
        // the development machine, and blocking startup on it would stall visible progress
        // for no design benefit. PostgreSQL remains the system of record either way, so this
        // is a latency decision, not a correctness one.
        services.AddSingleton<ICacheStore, InMemoryCacheStore>();

        // Extension point only — nothing in the critical path calls it (ARCHITECTURE.md §10).
        services.AddSingleton<IAdvisorService, NoOpAdvisorService>();

        return services;
    }
}

/// <summary>
/// Holds the catalog loaded during startup.
/// </summary>
/// <remarks>
/// A holder rather than an async singleton factory: loading the catalog needs a scoped
/// DbContext and an await, and blocking on that inside a DI factory would be sync-over-async
/// in the composition root. This keeps the async initialisation explicit and in one place.
/// </remarks>
public sealed class GameCatalogHolder : IGameCatalog
{
    private GameCatalog? inner;

    public void Set(GameCatalog catalog) => inner = catalog;

    private GameCatalog Current => inner
        ?? throw new InvalidOperationException(
            "Game catalog was not initialised. Call InitialiseTradebornAsync during startup.");

    public IReadOnlyList<ResourceDefinition> Resources => Current.Resources;
    public IReadOnlyList<BuildingDefinition> Buildings => Current.Buildings;
    public BuildingDefinition GetBuilding(string definitionId) => Current.GetBuilding(definitionId);

    public bool TryGetBuilding(string definitionId, out BuildingDefinition definition) =>
        Current.TryGetBuilding(definitionId, out definition);
}

/// <summary>
/// In-memory cache used until Redis becomes a hard dependency in Phase 3.
/// </summary>
/// <remarks>
/// Deliberately simple: expiry is checked lazily on read rather than swept by a timer. It
/// holds idempotency lookups and rate-limit counters, both short-lived and small, and it is
/// never the system of record.
/// </remarks>
public sealed class InMemoryCacheStore : ICacheStore
{
    private readonly ConcurrentDictionary<string, (string Value, DateTimeOffset ExpiresAt)> entries = new();
    private readonly TimeProvider timeProvider;

    public InMemoryCacheStore(TimeProvider timeProvider, ILogger<InMemoryCacheStore> logger)
    {
        this.timeProvider = timeProvider;
        logger.LogWarning(
            "Redis is not configured; using an in-memory cache. This is expected in development " +
            "but must not be used in production from Phase 3 onward.");
    }

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!entries.TryGetValue(key, out var entry))
        {
            return Task.FromResult<string?>(null);
        }

        if (entry.ExpiresAt <= timeProvider.GetUtcNow())
        {
            entries.TryRemove(key, out _);
            return Task.FromResult<string?>(null);
        }

        return Task.FromResult<string?>(entry.Value);
    }

    public Task SetAsync(string key, string value, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        entries[key] = (value, timeProvider.GetUtcNow().Add(ttl));
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        entries.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}

internal sealed class NoOpAdvisorService : IAdvisorService
{
    public Task<string?> ExplainAsync(Guid playerId, string question, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
}
