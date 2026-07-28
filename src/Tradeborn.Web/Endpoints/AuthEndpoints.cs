using Microsoft.AspNetCore.Mvc;
using Tradeborn.Infrastructure.Identity;

namespace Tradeborn.Web.Endpoints;

public static class AuthEndpoints
{
    private const string RefreshCookie = "tb_refresh";

    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth").AllowAnonymous().RequireRateLimiting("auth");

        group.MapPost("/register", async (
            RegisterRequest request,
            AuthService auth,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            if (request.Password.Length < 8)
            {
                return Results.Problem(
                    title: "Password too short",
                    detail: "Use at least 8 characters.",
                    statusCode: StatusCodes.Status400BadRequest,
                    extensions: new Dictionary<string, object?> { ["code"] = "PASSWORD_TOO_SHORT" });
            }

            var result = await auth.RegisterAsync(
                request.Email, request.Password, request.DisplayName, cancellationToken);

            return Complete(result, http);
        });

        group.MapPost("/login", async (
            LoginRequest request,
            AuthService auth,
            HttpContext http,
            CancellationToken cancellationToken) =>
            Complete(await auth.LoginAsync(request.Email, request.Password, cancellationToken), http));

        group.MapPost("/refresh", async (
            AuthService auth,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            // The refresh token lives only in an HttpOnly cookie, so JavaScript — and
            // therefore any XSS — cannot read it (ADR-007).
            var token = http.Request.Cookies[RefreshCookie];
            if (string.IsNullOrEmpty(token))
            {
                return Results.Problem(
                    title: "No session",
                    statusCode: StatusCodes.Status401Unauthorized,
                    extensions: new Dictionary<string, object?> { ["code"] = "NO_SESSION" });
            }

            return Complete(await auth.RefreshAsync(token, cancellationToken), http);
        });

        group.MapPost("/logout", async (
            AuthService auth,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var token = http.Request.Cookies[RefreshCookie];
            if (!string.IsNullOrEmpty(token))
            {
                await auth.LogoutAsync(token, cancellationToken);
            }

            http.Response.Cookies.Delete(RefreshCookie);
            return Results.NoContent();
        });
    }

    private static IResult Complete(AuthResult result, HttpContext http)
    {
        if (!result.Succeeded)
        {
            return Results.Problem(
                title: "Authentication failed",
                detail: result.Error,
                statusCode: StatusCodes.Status401Unauthorized,
                extensions: new Dictionary<string, object?> { ["code"] = "AUTH_FAILED" });
        }

        http.Response.Cookies.Append(RefreshCookie, result.RefreshToken!, new CookieOptions
        {
            HttpOnly = true,
            // SameSite=Strict costs nothing here because the SPA is same-origin by design,
            // and it removes classic CSRF from the threat model entirely.
            SameSite = SameSiteMode.Strict,
            Secure = http.Request.IsHttps,
            Path = "/api/auth",
            MaxAge = TimeSpan.FromDays(30),
        });

        // The access token is returned in the body and held in memory by the client — never
        // in localStorage, where an XSS would turn into a durable account compromise.
        return Results.Ok(new AuthResponse(result.AccessToken!, result.PlayerId));
    }
}

public sealed record RegisterRequest([Required] string Email, [Required] string Password, string DisplayName = "");
public sealed record LoginRequest([Required] string Email, [Required] string Password);
public sealed record AuthResponse(string AccessToken, Guid PlayerId);

[AttributeUsage(AttributeTargets.Parameter)]
internal sealed class RequiredAttribute : Attribute;
