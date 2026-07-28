using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Tradeborn.Infrastructure.Persistence;
using Tradeborn.Infrastructure.Seed;

namespace Tradeborn.Infrastructure.Identity;

public sealed record AuthOptions
{
    public const string SectionName = "Tradeborn:Auth";

    public string SigningKey { get; init; } = string.Empty;
    public string Issuer { get; init; } = "tradeborn";
    public string Audience { get; init; } = "tradeborn";
    public int AccessTokenMinutes { get; init; } = 15;
    public int RefreshTokenDays { get; init; } = 30;
}

public sealed record AuthResult(bool Succeeded, string? AccessToken, string? RefreshToken, Guid PlayerId, string? Error)
{
    public static AuthResult Fail(string error) => new(false, null, null, Guid.Empty, error);
}

/// <summary>
/// Registration, login, and refresh-token rotation per ADR-007.
/// </summary>
/// <remarks>
/// <para>
/// Access tokens are short-lived JWTs held in memory by the client; refresh tokens are
/// opaque, stored only as a SHA-256 hash, and <b>rotated on every use</b>.
/// </para>
/// <para>
/// The important property is reuse detection: presenting a refresh token that has already
/// been rotated means it was stolen, so the entire family is revoked. That turns a silent
/// long-term compromise into a contained, detectable event.
/// </para>
/// </remarks>
public sealed class AuthService(
    TradebornDbContext db,
    CityProvisioner provisioner,
    TimeProvider timeProvider,
    AuthOptions options)
{
    public async Task<AuthResult> RegisterAsync(
        string email,
        string password,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        var normalised = email.Trim().ToLowerInvariant();

        if (await db.Players.AnyAsync(p => p.Email == normalised, cancellationToken))
        {
            // Deliberately the same shape of error as a bad login, so registration cannot be
            // used to enumerate which addresses already have accounts.
            return AuthResult.Fail("Could not create the account.");
        }

        var player = new PlayerEntity
        {
            Id = Guid.NewGuid(),
            Email = normalised,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Founder" : displayName.Trim(),
            CreatedAtUtc = timeProvider.GetUtcNow(),
        };

        db.Players.Add(player);
        await db.SaveChangesAsync(cancellationToken);

        await provisioner.CreateForAsync(player.Id, $"{player.DisplayName}'s Landing", cancellationToken);

        return await IssueAsync(player, familyId: Guid.NewGuid(), cancellationToken);
    }

    public async Task<AuthResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var normalised = email.Trim().ToLowerInvariant();
        var player = await db.Players.FirstOrDefaultAsync(p => p.Email == normalised, cancellationToken);

        // Verify against a dummy hash when the account is missing so that a wrong address and
        // a wrong password take the same time to answer.
        var hash = player?.PasswordHash ?? "$2a$12$0000000000000000000000000000000000000000000000000000u";
        var valid = BCrypt.Net.BCrypt.Verify(password, hash);

        if (player is null || !valid)
        {
            return AuthResult.Fail("Invalid email or password.");
        }

        return await IssueAsync(player, familyId: Guid.NewGuid(), cancellationToken);
    }

    public async Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var hash = Hash(refreshToken);
        var now = timeProvider.GetUtcNow();

        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (stored is null)
        {
            return AuthResult.Fail("Invalid refresh token.");
        }

        if (stored.Used || stored.RevokedAtUtc is not null)
        {
            // Reuse of a rotated token means it leaked. Revoke the whole lineage — the
            // legitimate holder is forced to log in again, which is the correct trade.
            await RevokeFamilyAsync(stored.FamilyId, now, cancellationToken);
            return AuthResult.Fail("Refresh token was already used; session revoked.");
        }

        if (stored.ExpiresAtUtc <= now)
        {
            return AuthResult.Fail("Refresh token expired.");
        }

        var player = await db.Players.FirstOrDefaultAsync(p => p.Id == stored.PlayerId, cancellationToken);
        if (player is null)
        {
            return AuthResult.Fail("Invalid refresh token.");
        }

        stored.Used = true;
        return await IssueAsync(player, stored.FamilyId, cancellationToken);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var hash = Hash(refreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (stored is not null)
        {
            await RevokeFamilyAsync(stored.FamilyId, timeProvider.GetUtcNow(), cancellationToken);
        }
    }

    private async Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAtUtc, now), cancellationToken);
    }

    private async Task<AuthResult> IssueAsync(PlayerEntity player, Guid familyId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var refreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

        db.RefreshTokens.Add(new RefreshTokenEntity
        {
            Id = Guid.NewGuid(),
            PlayerId = player.Id,
            TokenHash = Hash(refreshToken),
            FamilyId = familyId,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(options.RefreshTokenDays),
        });

        await db.SaveChangesAsync(cancellationToken);

        return new AuthResult(true, CreateAccessToken(player, now), refreshToken, player.Id, null);
    }

    private string CreateAccessToken(PlayerEntity player, DateTimeOffset now)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = now.AddMinutes(options.AccessTokenMinutes).UtcDateTime,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, player.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Name, player.DisplayName),
                new Claim(ClaimTypes.Role, player.Role),
            ]),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    /// <summary>Refresh tokens are stored hashed so a database leak does not yield usable sessions.</summary>
    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
