using Tradeborn.Domain.Production;

namespace Tradeborn.Application.Contracts;

/// <summary>Request to switch a building's production on or off.</summary>
/// <remarks>
/// Carries the desired state rather than a "toggle" verb. A toggle is not idempotent: a
/// retried toggle flips the building back, so a dropped response would leave the player's
/// city in the opposite state to the one they asked for.
/// </remarks>
public sealed record SetProductionRequest(bool Active);

public sealed record ProductionResponse(
    bool Accepted,
    string? RefusalCode,
    string? RefusalMessage,
    BuildingDto? Building,
    DateTimeOffset ServerTimeUtc)
{
    public static ProductionResponse Refused(ProductionRefusal refusal, DateTimeOffset now) =>
        new(false, refusal.ToString(), ProductionRefusalMessages.For(refusal), null, now);
}

public static class ProductionRefusalMessages
{
    public static string For(ProductionRefusal refusal) => refusal switch
    {
        ProductionRefusal.BuildingNotFound => "That building is not in your city.",
        ProductionRefusal.NoRecipe => "That building does not produce anything.",
        ProductionRefusal.UnderConstruction => "That building is still being built.",
        ProductionRefusal.AlreadyInThatState => "That building is already in that state.",
        _ => "That action is not allowed right now.",
    };
}
