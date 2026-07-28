using System.Security.Claims;
using Tradeborn.Application.Cities;

namespace Tradeborn.Web.Endpoints;

public static class CityEndpoints
{
    public static void MapCityEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/cities").RequireRateLimiting("game");

        group.MapGet("/me", async (
            ClaimsPrincipal user,
            GetCityHandler handler,
            CancellationToken cancellationToken) =>
        {
            // The city is resolved from the TOKEN, never from a route or body parameter.
            // That single choice is what makes cross-tenant access (SECURITY_MODEL.md T7)
            // structurally impossible rather than something a check has to catch.
            var playerId = user.PlayerId();
            if (playerId is null)
            {
                return Results.Unauthorized();
            }

            var city = await handler.HandleAsync(playerId.Value, cancellationToken);

            return city is null
                ? Results.Problem(
                    title: "No city",
                    detail: "This player has no city yet.",
                    statusCode: StatusCodes.Status404NotFound,
                    extensions: new Dictionary<string, object?> { ["code"] = "CITY_NOT_FOUND" })
                : Results.Ok(city);
        });
    }

    private static Guid? PlayerId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub");

        return Guid.TryParse(value, out var id) ? id : null;
    }
}
