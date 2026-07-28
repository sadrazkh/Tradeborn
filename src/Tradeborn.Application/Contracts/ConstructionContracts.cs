using Tradeborn.Domain.Construction;

namespace Tradeborn.Application.Contracts;

/// <summary>
/// Request to place a new building.
/// </summary>
/// <remarks>
/// Intent, not outcome (SECURITY_MODEL.md §3). There is deliberately no cost, no duration and
/// no resulting level in this shape — the server computes all of them. A field that does not
/// exist cannot be tampered with.
/// </remarks>
public sealed record StartConstructionRequest(string DefinitionId, int Col, int Row);

public sealed record StartUpgradeRequest(string BuildingId);

/// <summary>The outcome of a construction command.</summary>
public sealed record ConstructionResponse(
    bool Accepted,
    string? RefusalCode,
    string? RefusalMessage,
    BuildingDto? Building,
    long BalanceCoins,
    IReadOnlyList<ResourceBalanceDto> Resources,
    DateTimeOffset ServerTimeUtc)
{
    public static ConstructionResponse Refused(
        ConstructionRefusal refusal,
        long balanceCoins,
        IReadOnlyList<ResourceBalanceDto> resources,
        DateTimeOffset now) =>
        new(false, refusal.ToString(), RefusalMessages.For(refusal), null, balanceCoins, resources, now);
}

/// <summary>
/// Player-facing text for each refusal.
/// </summary>
/// <remarks>
/// Every refusal gets a specific, actionable sentence. "That didn't work" teaches nothing and
/// reads as a bug; "You need a bigger city" is a goal. The client keys off
/// <c>RefusalCode</c> for its own localisation, so these are the fallback rather than the
/// only presentation.
/// </remarks>
public static class RefusalMessages
{
    public static string For(ConstructionRefusal refusal) => refusal switch
    {
        ConstructionRefusal.UnknownPlot => "That plot is not part of your city.",
        ConstructionRefusal.PlotLocked => "That plot is locked. Raise your city level to unlock it.",
        ConstructionRefusal.PlotOccupied => "There is already a building on that plot.",
        ConstructionRefusal.NotUnlocked => "Your city level is too low for that building.",
        ConstructionRefusal.CannotBeBuilt => "That building cannot be built.",
        ConstructionRefusal.InsufficientFunds => "You cannot afford that yet.",
        ConstructionRefusal.QueueFull => "Your build queue is full. Wait for the current build to finish.",
        ConstructionRefusal.BuildingNotFound => "That building is not in your city.",
        ConstructionRefusal.AlreadyUnderConstruction => "That building is already being worked on.",
        ConstructionRefusal.MaxLevelReached => "That building is already at its maximum level.",
        _ => "That action is not allowed right now.",
    };
}
