namespace Tradeborn.Web.Prototype;

/// <summary>
/// A fixed, hand-authored city layout used by the Phase 0 prototype to validate the
/// Babylon.js renderer against server-supplied data.
///
/// This type is deliberately throwaway. Phase 1 replaces it with the real
/// <c>Cities</c> module backed by PostgreSQL; the client-side contract
/// (grid size, plot list, building list) is kept close to the eventual shape so the
/// renderer does not need rewriting.
/// </summary>
internal static class PrototypeCity
{
    private const int GridSize = 8;

    public static PrototypeCityDto Create(DateTimeOffset serverTimeUtc)
    {
        var plots = new List<PrototypePlotDto>(GridSize * GridSize);

        for (var row = 0; row < GridSize; row++)
        {
            for (var col = 0; col < GridSize; col++)
            {
                // A simple deterministic pattern: a stone ridge along the north-east,
                // a dirt crossroads through the middle, grass elsewhere. Deterministic so
                // the prototype looks identical on every load and screenshots are stable.
                var terrain = (col, row) switch
                {
                    _ when col >= 6 && row <= 1 => "stone",
                    _ when col == 3 || row == 4 => "dirt",
                    _ => "grass",
                };

                // Plots inside the starting 4x4 cluster are unlocked at city level 1.
                var unlocked = col is >= 1 and <= 4 && row is >= 2 and <= 5;

                plots.Add(new PrototypePlotDto(col, row, terrain, unlocked));
            }
        }

        var buildings = new[]
        {
            new PrototypeBuildingDto("b-town-hall", "town_hall", 2, 3, 1, "Idle"),
            new PrototypeBuildingDto("b-market", "market", 4, 5, 1, "Idle"),
            new PrototypeBuildingDto("b-lumber-1", "lumber_camp", 1, 2, 1, "Producing"),
            new PrototypeBuildingDto("b-warehouse-1", "warehouse", 2, 5, 1, "Idle"),
            new PrototypeBuildingDto("b-sawmill-1", "sawmill", 4, 2, 2, "Producing"),
        };

        return new PrototypeCityDto(
            Name: "Riverbend",
            GridSize: GridSize,
            ServerTimeUtc: serverTimeUtc,
            Plots: plots,
            Buildings: buildings);
    }
}

internal sealed record PrototypeCityDto(
    string Name,
    int GridSize,
    DateTimeOffset ServerTimeUtc,
    IReadOnlyList<PrototypePlotDto> Plots,
    IReadOnlyList<PrototypeBuildingDto> Buildings);

internal sealed record PrototypePlotDto(int Col, int Row, string Terrain, bool Unlocked);

internal sealed record PrototypeBuildingDto(
    string Id,
    string DefinitionId,
    int Col,
    int Row,
    int Level,
    string State);
