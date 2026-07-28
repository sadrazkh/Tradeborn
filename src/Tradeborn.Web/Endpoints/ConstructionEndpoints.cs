using System.Security.Claims;
using Tradeborn.Application.Construction;
using Tradeborn.Application.Contracts;
using Tradeborn.Application.Production;
using Tradeborn.Domain.Construction;
using Tradeborn.Infrastructure.Persistence;

namespace Tradeborn.Web.Endpoints;

public static class ConstructionEndpoints
{
    public static void MapConstructionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/cities/me").RequireRateLimiting("game");

        group.MapPost("/buildings", async (
            StartConstructionRequest request,
            ClaimsPrincipal user,
            HttpContext http,
            ConstructionHandler handler,
            CancellationToken cancellationToken) =>
        {
            var playerId = user.PlayerId();
            if (playerId is null)
            {
                return Results.Unauthorized();
            }

            if (!TryGetIdempotencyKey(http, out var key, out var keyProblem))
            {
                return keyProblem;
            }

            try
            {
                var result = await handler.StartConstructionAsync(
                    playerId.Value, request, key, http.TraceIdentifier, cancellationToken);

                return ToResult(result);
            }
            catch (IdempotencyConflictException ex)
            {
                return Problem(
                    "Idempotency key reused",
                    ex.Message,
                    StatusCodes.Status409Conflict,
                    "IDEMPOTENCY_KEY_REUSED");
            }
        });

        group.MapPost("/buildings/{buildingId}/upgrade", async (
            string buildingId,
            ClaimsPrincipal user,
            HttpContext http,
            ConstructionHandler handler,
            CancellationToken cancellationToken) =>
        {
            var playerId = user.PlayerId();
            if (playerId is null)
            {
                return Results.Unauthorized();
            }

            if (!TryGetIdempotencyKey(http, out var key, out var keyProblem))
            {
                return keyProblem;
            }

            try
            {
                var result = await handler.StartUpgradeAsync(
                    playerId.Value,
                    new StartUpgradeRequest(buildingId),
                    key,
                    http.TraceIdentifier,
                    cancellationToken);

                return ToResult(result);
            }
            catch (IdempotencyConflictException ex)
            {
                return Problem(
                    "Idempotency key reused",
                    ex.Message,
                    StatusCodes.Status409Conflict,
                    "IDEMPOTENCY_KEY_REUSED");
            }
        });
    }

    public static void MapProductionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/cities/me").RequireRateLimiting("game");

        group.MapPut("/buildings/{buildingId}/production", async (
            string buildingId,
            SetProductionRequest request,
            ClaimsPrincipal user,
            HttpContext http,
            ProductionHandler handler,
            CancellationToken cancellationToken) =>
        {
            var playerId = user.PlayerId();
            if (playerId is null)
            {
                return Results.Unauthorized();
            }

            if (!TryGetIdempotencyKey(http, out var key, out var keyProblem))
            {
                return keyProblem;
            }

            try
            {
                var result = await handler.SetActiveAsync(
                    playerId.Value, buildingId, request.Active, key, http.TraceIdentifier, cancellationToken);

                if (result is null)
                {
                    return Problem(
                        "No city", "This player has no city yet.",
                        StatusCodes.Status404NotFound, "CITY_NOT_FOUND");
                }

                return result.Accepted
                    ? Results.Ok(result)
                    : Results.Json(result, statusCode: StatusCodes.Status409Conflict);
            }
            catch (IdempotencyConflictException ex)
            {
                return Problem(
                    "Idempotency key reused", ex.Message,
                    StatusCodes.Status409Conflict, "IDEMPOTENCY_KEY_REUSED");
            }
        });
    }

    /// <summary>
    /// Every economic command must carry a client-generated <c>Idempotency-Key</c>.
    /// </summary>
    /// <remarks>
    /// Required rather than optional. If the header were optional, the safe path would be the
    /// one clients forget, and a retried request on a flaky mobile connection would charge the
    /// player twice (SECURITY_MODEL.md T3).
    /// </remarks>
    private static bool TryGetIdempotencyKey(HttpContext http, out string key, out IResult problem)
    {
        var header = http.Request.Headers["Idempotency-Key"].ToString();

        if (string.IsNullOrWhiteSpace(header) || header.Length > 128)
        {
            key = string.Empty;
            problem = Problem(
                "Missing Idempotency-Key",
                "Economic commands require an 'Idempotency-Key' header of up to 128 characters.",
                StatusCodes.Status400BadRequest,
                "IDEMPOTENCY_KEY_REQUIRED");
            return false;
        }

        key = header;
        problem = Results.Empty;
        return true;
    }

    private static IResult ToResult(ConstructionResponse? response)
    {
        if (response is null)
        {
            return Problem(
                "No city",
                "This player has no city yet.",
                StatusCodes.Status404NotFound,
                "CITY_NOT_FOUND");
        }

        if (response.Accepted)
        {
            return Results.Ok(response);
        }

        // A refusal is a 409 rather than a 400: the request was well-formed, the world just
        // did not allow it. The client distinguishes them — a 400 is a bug to report, a 409
        // is a game state to explain.
        var status = response.RefusalCode == nameof(ConstructionRefusal.UnknownPlot)
            ? StatusCodes.Status400BadRequest
            : StatusCodes.Status409Conflict;

        return Results.Json(response, statusCode: status);
    }

    private static IResult Problem(string title, string detail, int statusCode, string code) =>
        Results.Problem(
            title: title,
            detail: detail,
            statusCode: statusCode,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    private static Guid? PlayerId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub");

        return Guid.TryParse(value, out var id) ? id : null;
    }
}
