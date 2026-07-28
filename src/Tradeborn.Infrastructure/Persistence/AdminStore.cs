using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Tradeborn.Application.Abstractions;
using Tradeborn.Application.Contracts;
using Tradeborn.Domain.Buildings;
using Tradeborn.Domain.Production;
using Tradeborn.Infrastructure.Seed;

namespace Tradeborn.Infrastructure.Persistence;

/// <summary>
/// Admin queries and economy tuning.
/// </summary>
/// <remarks>
/// Every read here is <c>AsNoTracking</c> and paged. An admin panel is exactly where an
/// unbounded query gets written and then quietly loads a hundred thousand rows in production,
/// so the page size is clamped rather than trusted.
/// </remarks>
public sealed class AdminStore(
    TradebornDbContext db,
    GameCatalogHolder catalog,
    IConfiguration configuration,
    ILogger<AdminStore> logger) : IAdminStore
{
    private const int MaxPageSize = 100;

    public async Task<AdminPlayerPageDto> ListPlayersAsync(
        int page,
        int pageSize,
        string? search,
        CancellationToken cancellationToken = default)
    {
        var (skip, take, safePage) = Paging(page, pageSize);

        var query = db.Players.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p => EF.Functions.ILike(p.DisplayName, $"%{term}%"));
        }

        var total = await query.CountAsync(cancellationToken);

        var players = await query
            .OrderByDescending(p => p.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .Select(p => new
            {
                p.Id,
                p.DisplayName,
                p.Role,
                p.Level,
                p.Xp,
                p.CreatedAtUtc,
                City = db.Cities
                    .Where(c => c.PlayerId == p.Id)
                    .Select(c => new
                    {
                        c.Name,
                        c.BalanceCent,
                        c.LastSettledAtUtc,
                        Buildings = c.Buildings.Count,
                    })
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return new AdminPlayerPageDto(
            players.Select(p => new AdminPlayerDto(
                p.Id,
                p.DisplayName,
                p.Role,
                p.Level,
                p.Xp,
                p.CreatedAtUtc,
                p.City?.Name,
                (p.City?.BalanceCent ?? 0) / 100,
                p.City?.Buildings ?? 0,
                p.City?.LastSettledAtUtc)).ToArray(),
            safePage,
            take,
            total);
    }

    public async Task<AdminCityDto?> InspectCityAsync(
        Guid playerId,
        CancellationToken cancellationToken = default)
    {
        var player = await db.Players
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == playerId, cancellationToken);

        if (player is null)
        {
            return null;
        }

        var city = await db.Cities
            .AsNoTracking()
            .AsSplitQuery()
            .Include(c => c.Buildings)
            .Include(c => c.Inventory)
            .Include(c => c.Transports)
            .FirstOrDefaultAsync(c => c.PlayerId == playerId, cancellationToken);

        if (city is null)
        {
            return null;
        }

        var quests = await db.PlayerQuests
            .AsNoTracking()
            .Where(q => q.PlayerId == playerId)
            .Select(q => q.QuestId)
            .ToListAsync(cancellationToken);

        // Capacity is derived from buildings in the domain; recomputed here rather than
        // stored so the panel can never show a figure the game itself would disagree with.
        var capacity = city.Buildings
            .Where(b => b.State != nameof(BuildingState.UnderConstruction))
            .Sum(b => StorageFor(b.DefinitionId, b.Level));

        return new AdminCityDto(
            playerId,
            player.DisplayName,
            city.Name,
            city.BalanceCent / 100,
            capacity,
            CityLevelFor(city),
            city.DeliveriesCompleted,
            city.SalesCompleted,
            city.LastSettledAtUtc,
            city.Buildings
                .OrderBy(b => b.Row).ThenBy(b => b.Col)
                .Select(b => new BuildingDto(
                    b.Id.ToString(), b.DefinitionId, b.Col, b.Row, b.Level, b.State,
                    b.HaltReason == nameof(HaltReason.None) ? null : b.HaltReason,
                    b.CompletesAtUtc, b.PendingLevel, 1))
                .ToArray(),
            city.Inventory
                .OrderBy(i => i.ResourceId, StringComparer.Ordinal)
                .Select(i => new ResourceBalanceDto(i.ResourceId, i.Quantity, capacity))
                .ToArray(),
            city.Transports
                .OrderBy(t => t.ArrivesAtUtc)
                .Select(t => new TransportDto(
                    t.Id.ToString(), t.FromBuildingId.ToString(), t.ResourceId,
                    t.Quantity, t.DepartedAtUtc, t.ArrivesAtUtc))
                .ToArray(),
            quests);
    }

    public async Task<AuditPageDto> ReadAuditAsync(
        Guid? playerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var (skip, take, safePage) = Paging(page, pageSize);

        var query = db.AuditLedger.AsNoTracking();
        if (playerId is not null)
        {
            query = query.Where(e => e.PlayerId == playerId);
        }

        var total = await query.CountAsync(cancellationToken);

        var entries = await query
            .OrderByDescending(e => e.OccurredAtUtc)
            .ThenByDescending(e => e.Id)
            .Skip(skip)
            .Take(take)
            .Select(e => new AuditEntryDto(
                e.Id, e.PlayerId, e.ActorPlayerId, e.OccurredAtUtc, e.Kind,
                e.MoneyDeltaCent, e.BalanceAfterCent, e.ResourceDeltas, e.CorrelationId, e.Metadata))
            .ToListAsync(cancellationToken);

        return new AuditPageDto(entries, safePage, take, total);
    }

    public async Task<AdminSystemDto> ReadSystemAsync(CancellationToken cancellationToken = default) =>
        new(
            DateTimeOffset.UtcNow,
            await db.Players.CountAsync(cancellationToken),
            await db.Cities.CountAsync(cancellationToken),
            await db.TransportJobs.LongCountAsync(cancellationToken),
            await db.AuditLedger.LongCountAsync(cancellationToken),
            // The total money in circulation. The inflation guard in ECONOMY_DESIGN.md §12 is
            // this number over time, so it belongs on the first screen an operator opens.
            await db.Cities.SumAsync(c => c.BalanceCent, cancellationToken) / 100,
            // Reported rather than assumed. Redis is optional in development and required in
            // production from Phase 3 onward, so an operator needs to see which one they have.
            RedisConfigured: !string.IsNullOrWhiteSpace(configuration.GetConnectionString("Redis")),
            configuration["ASPNETCORE_ENVIRONMENT"] ?? "Unknown");

    // -----------------------------------------------------------------------------------
    // Tuning
    // -----------------------------------------------------------------------------------

    public async Task<EconomyTuningDto> ReadTuningAsync(CancellationToken cancellationToken = default)
    {
        var resources = await db.ResourceDefinitions.AsNoTracking()
            .OrderBy(r => r.BasePriceCoins)
            .Select(r => new ResourceTuningDto(r.Id, r.Tier, r.BasePriceCoins, r.MarketDepth))
            .ToListAsync(cancellationToken);

        var buildings = await db.BuildingDefinitions.AsNoTracking()
            .OrderBy(b => b.UnlockCityLevel).ThenBy(b => b.Id)
            .Select(b => new BuildingTuningDto(
                b.Id, b.StoragePerResource, b.BuildCostCoins, b.BuildSeconds, b.UnlockCityLevel))
            .ToListAsync(cancellationToken);

        var recipes = await db.Recipes.AsNoTracking()
            .OrderBy(r => r.TopologicalRank).ThenBy(r => r.Id)
            .Select(r => new RecipeTuningDto(r.Id, r.CycleMilliseconds))
            .ToListAsync(cancellationToken);

        return new EconomyTuningDto(resources, buildings, recipes);
    }

    public async Task<ApplyTuningResponse> ApplyTuningAsync(
        EconomyTuningDto tuning,
        CancellationToken cancellationToken = default)
    {
        var resources = await db.ResourceDefinitions.ToDictionaryAsync(r => r.Id, cancellationToken);
        var buildings = await db.BuildingDefinitions.ToDictionaryAsync(b => b.Id, cancellationToken);
        var recipes = await db.Recipes.ToDictionaryAsync(r => r.Id, cancellationToken);

        var resourcesUpdated = 0;
        var buildingsUpdated = 0;
        var recipesUpdated = 0;

        // Unknown ids are skipped rather than created. Tuning edits what exists; adding a new
        // resource changes the recipe graph and its topological ranks, which is a seed change
        // and a deploy, not a slider.
        foreach (var row in tuning.Resources)
        {
            if (!resources.TryGetValue(row.Id, out var entity))
            {
                continue;
            }

            if (row.BasePriceCoins <= 0 || row.MarketDepth <= 0)
            {
                continue;
            }

            entity.BasePriceCoins = row.BasePriceCoins;
            entity.MarketDepth = row.MarketDepth;
            resourcesUpdated++;
        }

        foreach (var row in tuning.Buildings)
        {
            if (!buildings.TryGetValue(row.Id, out var entity))
            {
                continue;
            }

            if (row.BuildCostCoins < 0 || row.BuildSeconds < 0 || row.UnlockCityLevel < 1)
            {
                continue;
            }

            entity.StoragePerResource = row.StoragePerResource;
            entity.BuildCostCoins = row.BuildCostCoins;
            entity.BuildSeconds = row.BuildSeconds;
            entity.UnlockCityLevel = row.UnlockCityLevel;
            buildingsUpdated++;
        }

        foreach (var row in tuning.Recipes)
        {
            if (!recipes.TryGetValue(row.Id, out var entity))
            {
                continue;
            }

            // A zero or negative cycle would divide by zero in settlement and take the whole
            // economy down. Rejecting it here is cheaper than discovering it at runtime.
            if (row.CycleMilliseconds <= 0)
            {
                continue;
            }

            entity.CycleMilliseconds = row.CycleMilliseconds;
            recipesUpdated++;
        }

        await db.SaveChangesAsync(cancellationToken);

        // The reload is what makes this take effect. Without it the rows change and every
        // request keeps using the catalog loaded at startup.
        catalog.Set(await GameCatalog.LoadAsync(db, cancellationToken));

        logger.LogWarning(
            "Economy tuning applied: {Resources} resources, {Buildings} buildings, {Recipes} recipes. Catalog reloaded.",
            resourcesUpdated, buildingsUpdated, recipesUpdated);

        return new ApplyTuningResponse(
            true,
            $"Applied and reloaded. Existing market prices drift to the new base over ~35 minutes.",
            resourcesUpdated,
            buildingsUpdated,
            recipesUpdated,
            DateTimeOffset.UtcNow);
    }

    // -----------------------------------------------------------------------------------
    // Feature flags
    // -----------------------------------------------------------------------------------

    public async Task<IReadOnlyList<FeatureFlagDto>> ListFlagsAsync(
        CancellationToken cancellationToken = default) =>
        await db.FeatureFlags.AsNoTracking()
            .OrderBy(f => f.Key)
            .Select(f => new FeatureFlagDto(f.Key, f.Enabled, f.Description, f.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

    public async Task<FeatureFlagDto> SetFlagAsync(
        string key,
        bool enabled,
        string? description,
        CancellationToken cancellationToken = default)
    {
        var flag = await db.FeatureFlags.FirstOrDefaultAsync(f => f.Key == key, cancellationToken);

        if (flag is null)
        {
            flag = new FeatureFlagEntity { Key = key };
            db.FeatureFlags.Add(flag);
        }

        flag.Enabled = enabled;
        flag.Description = description ?? flag.Description;
        flag.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        logger.LogWarning("Feature flag '{Key}' set to {Enabled}.", key, enabled);

        return new FeatureFlagDto(flag.Key, flag.Enabled, flag.Description, flag.UpdatedAtUtc);
    }

    // -----------------------------------------------------------------------------------

    private static (int Skip, int Take, int Page) Paging(int page, int pageSize)
    {
        var safePage = Math.Max(1, page);
        var safeSize = Math.Clamp(pageSize <= 0 ? 25 : pageSize, 1, MaxPageSize);
        return ((safePage - 1) * safeSize, safeSize, safePage);
    }

    private long StorageFor(string definitionId, int level) =>
        catalog.TryGetBuilding(definitionId, out var definition) ? definition.StorageAtLevel(level) : 0;

    private static int CityLevelFor(CityEntity city)
    {
        var total = city.Buildings
            .Where(b => b.State != nameof(BuildingState.UnderConstruction))
            .Sum(b => b.Level);

        return Math.Max(1, total / 4);
    }
}
