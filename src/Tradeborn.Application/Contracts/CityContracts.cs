namespace Tradeborn.Application.Contracts;

/// <summary>
/// The city as the client sees it.
/// </summary>
/// <remarks>
/// DTOs, never domain entities, cross the API boundary — asserted by
/// <c>Tradeborn.ArchitectureTests</c>. Shapes here deliberately match what the Babylon
/// renderer already consumes so that replacing the Phase 0 prototype endpoint required no
/// client changes.
/// </remarks>
public sealed record CityDto(
    string Name,
    int GridSize,
    DateTimeOffset ServerTimeUtc,
    long BalanceCoins,
    long CapacityPerResource,
    IReadOnlyList<PlotDto> Plots,
    IReadOnlyList<BuildingDto> Buildings,
    IReadOnlyList<ResourceBalanceDto> Resources,
    IReadOnlyList<TransportDto> Transports,
    PlayerProgressDto Progress,
    OfflineSummaryDto? OfflineSummary);

/// <summary>The player's level and experience, so the HUD is correct from the first frame.</summary>
public sealed record PlayerProgressDto(int Level, long Xp, long XpToNextLevel, int CityLevel);

/// <summary>
/// A load on the road.
/// </summary>
/// <remarks>
/// Departure and arrival are absolute server instants rather than a progress fraction, so a
/// client that loads mid-journey can place the cart at the right point on the road instead of
/// restarting the trip from the depot.
/// </remarks>
public sealed record TransportDto(
    string Id,
    string FromBuildingId,
    string Resource,
    long Quantity,
    DateTimeOffset DepartedAtUtc,
    DateTimeOffset ArrivesAtUtc);

public sealed record PlotDto(int Col, int Row, string Terrain, bool Unlocked);

public sealed record BuildingDto(
    string Id,
    string DefinitionId,
    int Col,
    int Row,
    int Level,
    string State,
    string? HaltReason,
    /// <summary>When an in-flight build or upgrade lands. Null when nothing is in flight.</summary>
    DateTimeOffset? CompletesAtUtc = null,
    /// <summary>Level once the in-flight work finishes; equal to <see cref="Level"/> when idle.</summary>
    int PendingLevel = 1,
    /// <summary>
    /// Build progress 0..1 at <c>ServerTimeUtc</c>.
    /// </summary>
    /// <remarks>
    /// Sent so the client can pick the right construction stage immediately on load rather
    /// than replaying from zero. It then interpolates against the synchronised server clock —
    /// never against <c>Date.now()</c> (REALTIME_AND_TIME_MODEL.md §7).
    /// </remarks>
    double ConstructionProgress = 1);

public sealed record ResourceBalanceDto(string Resource, long Quantity, long Capacity);

/// <summary>
/// What happened while the player was away. Drives the "while you were away" recap.
/// </summary>
/// <remarks>
/// Framing matters here (PLAYER_JOURNEY.md): this reports what was <i>produced</i> and what
/// <i>stopped and why</i>. It never reports what was "lost" — same facts, opposite feeling.
/// </remarks>
public sealed record OfflineSummaryDto(
    DateTimeOffset Since,
    IReadOnlyList<ResourceBalanceDto> Produced,
    IReadOnlyList<string> HaltedBuildings);
