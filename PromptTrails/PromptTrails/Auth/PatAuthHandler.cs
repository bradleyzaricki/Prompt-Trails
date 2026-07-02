using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PromptTrails.Data;

namespace PromptTrails.Auth;

/// <summary>
/// Authenticates the CLI and MCP client by their personal access token (PAT).
/// Resolves "Authorization: Bearer pt_..." back to the owning user.
/// </summary>
public class PatAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AppDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer pt_", StringComparison.Ordinal))
            return AuthenticateResult.NoResult();

        var raw = header["Bearer ".Length..];
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

        var token = await db.UserTokens
            .Include(t => t.User)
            .SingleOrDefaultAsync(t => t.TokenHash == hash && t.RevokedAt == null);

        if (token is null)
            return AuthenticateResult.Fail("Invalid or revoked token.");

        token.LastUsedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        Claim[] claims =
        [
            new("sub", token.UserId.ToString()),
            new("name", token.User.DisplayName),
        ];
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}
