using System.Security.Claims;
using Tradeborn.Application.Abstractions;
using Tradeborn.Application.Admin;
using Tradeborn.Application.Contracts;

namespace Tradeborn.Web.Endpoints;

/// <summary>
/// The admin panel's API.
/// </summary>
/// <remarks>
/// <para>
/// Split by policy, not by resource. Reads require <c>admin.read</c> (Admin or Support);
/// anything that changes the world requires <c>admin.write</c> (Admin only). Most support work
/// is reading, and read access should not carry the ability to hand out money.
/// </para>
/// <para>
/// In production these should additionally sit behind an IP allow-list (SECURITY_MODEL.md
/// §10). That is deployment configuration rather than application code, and it is documented
/// in docs/operations/DEPLOYMENT.md.
/// </para>
/// </remarks>
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        var read = app.MapGroup("/api/admin").RequireAuthorization("admin.read");
        var write = app.MapGroup("/api/admin").RequireAuthorization("admin.write");

        // ---- Inspection ----------------------------------------------------------------

        read.MapGet("/system", async (IAdminStore store, CancellationToken ct) =>
            Results.Ok(await store.ReadSystemAsync(ct)));

        read.MapGet("/players", async (
            IAdminStore store,
            CancellationToken ct,
            int page = 1,
            int pageSize = 25,
            string? search = null) =>
            Results.Ok(await store.ListPlayersAsync(page, pageSize, search, ct)));

        read.MapGet("/players/{playerId:guid}/city", async (
            Guid playerId,
            IAdminStore store,
            CancellationToken ct) =>
        {
            var city = await store.InspectCityAsync(playerId, ct);
            return city is null ? NotFound("That player has no city.") : Results.Ok(city);
        });

        read.MapGet("/audit", async (
            IAdminStore store,
            CancellationToken ct,
            Guid? playerId = null,
            int page = 1,
            int pageSize = 50) =>
            Results.Ok(await store.ReadAuditAsync(playerId, page, pageSize, ct)));

        // ---- Economy tuning ------------------------------------------------------------

        read.MapGet("/economy", async (IAdminStore store, CancellationToken ct) =>
            Results.Ok(await store.ReadTuningAsync(ct)));

        write.MapPut("/economy", async (
            EconomyTuningDto tuning,
            IAdminStore store,
            CancellationToken ct) =>
            Results.Ok(await store.ApplyTuningAsync(tuning, ct)));

        // ---- Feature flags -------------------------------------------------------------

        read.MapGet("/flags", async (IAdminStore store, CancellationToken ct) =>
            Results.Ok(await store.ListFlagsAsync(ct)));

        write.MapPut("/flags/{key}", async (
            string key,
            SetFeatureFlagRequest request,
            IAdminStore store,
            CancellationToken ct) =>
            Results.Ok(await store.SetFlagAsync(key, request.Enabled, request.Description, ct)));

        // ---- Operator actions ----------------------------------------------------------

        write.MapPost("/players/{playerId:guid}/grant", async (
            Guid playerId,
            GrantRequest request,
            ClaimsPrincipal user,
            HttpContext http,
            AdminHandler handler,
            CancellationToken ct) =>
        {
            var actorId = user.PlayerId();
            if (actorId is null)
            {
                return Results.Unauthorized();
            }

            var result = await handler.GrantAsync(
                actorId.Value, playerId, request, http.TraceIdentifier, ct);

            return ToResult(result);
        });

        write.MapPost("/players/{playerId:guid}/reset", async (
            Guid playerId,
            ResetRequest request,
            ClaimsPrincipal user,
            AdminHandler handler,
            CancellationToken ct) =>
        {
            var actorId = user.PlayerId();
            if (actorId is null)
            {
                return Results.Unauthorized();
            }

            var result = await handler.ResetEconomyAsync(actorId.Value, playerId, request.Reason, ct);
            return ToResult(result);
        });
    }

    private static IResult ToResult(AdminActionResponse? result)
    {
        if (result is null)
        {
            return NotFound("That player has no city.");
        }

        // A rejected action is a 400: unlike a player refusal, this means the operator asked
        // for something malformed rather than the world saying no.
        return result.Accepted
            ? Results.Ok(result)
            : Results.Json(result, statusCode: StatusCodes.Status400BadRequest);
    }

    private static IResult NotFound(string detail) =>
        Results.Problem(
            title: "Not found",
            detail: detail,
            statusCode: StatusCodes.Status404NotFound,
            extensions: new Dictionary<string, object?> { ["code"] = "NOT_FOUND" });

    private static Guid? PlayerId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub");

        return Guid.TryParse(value, out var id) ? id : null;
    }
}

public sealed record ResetRequest(string Reason);
