using System.Security.Claims;
using Tradeborn.Application.Contracts;
using Tradeborn.Application.Quests;
using Tradeborn.Infrastructure.Persistence;

namespace Tradeborn.Web.Endpoints;

public static class QuestEndpoints
{
    public static void MapQuestEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/quests").RequireRateLimiting("game");

        group.MapGet("/", async (
            ClaimsPrincipal user,
            QuestHandler handler,
            CancellationToken cancellationToken) =>
        {
            var playerId = user.PlayerId();
            if (playerId is null)
            {
                return Results.Unauthorized();
            }

            var board = await handler.GetBoardAsync(playerId.Value, cancellationToken);

            return board is null
                ? Results.Problem(
                    title: "No city",
                    detail: "This player has no city yet.",
                    statusCode: StatusCodes.Status404NotFound,
                    extensions: new Dictionary<string, object?> { ["code"] = "CITY_NOT_FOUND" })
                : Results.Ok(board);
        });

        group.MapPost("/{questId}/claim", async (
            string questId,
            ClaimsPrincipal user,
            HttpContext http,
            QuestHandler handler,
            CancellationToken cancellationToken) =>
        {
            var playerId = user.PlayerId();
            if (playerId is null)
            {
                return Results.Unauthorized();
            }

            var key = http.Request.Headers["Idempotency-Key"].ToString();
            if (string.IsNullOrWhiteSpace(key) || key.Length > 128)
            {
                return Results.Problem(
                    title: "Missing Idempotency-Key",
                    detail: "Economic commands require an 'Idempotency-Key' header of up to 128 characters.",
                    statusCode: StatusCodes.Status400BadRequest,
                    extensions: new Dictionary<string, object?> { ["code"] = "IDEMPOTENCY_KEY_REQUIRED" });
            }

            try
            {
                var result = await handler.ClaimAsync(
                    playerId.Value, questId, key, http.TraceIdentifier, cancellationToken);

                if (result is null)
                {
                    return Results.Problem(
                        title: "No city",
                        detail: "This player has no city yet.",
                        statusCode: StatusCodes.Status404NotFound,
                        extensions: new Dictionary<string, object?> { ["code"] = "CITY_NOT_FOUND" });
                }

                // A refusal is a 409: the request was well formed, the world just said no.
                return result.Accepted
                    ? Results.Ok(result)
                    : Results.Json(result, statusCode: StatusCodes.Status409Conflict);
            }
            catch (IdempotencyConflictException ex)
            {
                return Results.Problem(
                    title: "Idempotency key reused",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?> { ["code"] = "IDEMPOTENCY_KEY_REUSED" });
            }
        }).RequireRateLimiting("command");
    }

    private static Guid? PlayerId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub");

        return Guid.TryParse(value, out var id) ? id : null;
    }
}
