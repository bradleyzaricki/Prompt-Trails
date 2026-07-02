using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PromptTrails.Auth;
using PromptTrails.Data;
using PromptTrails.Models;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

// Where the SPA lives. Post-login redirects are pinned to this origin so a caller-supplied
// returnUrl can never send the JWT to an off-site host (open-redirect / token theft).
const string FrontendBaseUrl = "http://localhost:2324";

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(cfg.GetConnectionString("Postgres"))
       .UseSnakeCaseNamingConvention());

builder.Services.AddScoped<JwtTokenService>();

var jwt = cfg.GetSection("Jwt");
builder.Services.AddAuthentication(o =>
    {
        o.DefaultScheme = "MultiAuth";                                       // API: JWT or PAT
        o.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme; // temp cookie for the GitHub handshake
    })
    .AddCookie()
    .AddGitHub(o =>
    {
        o.ClientId = cfg["GitHub:ClientId"]!;
        o.ClientSecret = cfg["GitHub:ClientSecret"]!;
        o.CallbackPath = "/signin-github";
        o.Scope.Add("user:email");
        o.ClaimActions.MapJsonKey("urn:github:avatar", "avatar_url");
        o.ClaimActions.MapJsonKey("urn:github:name", "name");
    })
    .AddJwtBearer("Bearer", o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!)),
        };
    })
    .AddScheme<AuthenticationSchemeOptions, PatAuthHandler>("Pat", _ => { })
    .AddPolicyScheme("MultiAuth", "JWT or PAT", o =>
    {
        o.ForwardDefaultSelector = ctx =>
            ctx.Request.Headers.Authorization.ToString().StartsWith("Bearer pt_", StringComparison.Ordinal)
                ? "Pat"
                : "Bearer";
    });

builder.Services.AddAuthorization();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

var auth = app.MapGroup("/api/auth");

// 1. Kick off GitHub login (the SPA navigates here).
auth.MapGet("/github/login", (string? returnUrl) =>
    Results.Challenge(
        new AuthenticationProperties
        {
            RedirectUri = $"/api/auth/github/callback?returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}",
        },
        ["GitHub"]));

// 2. GitHub redirects back here after the handshake; find-or-create the user, mint our JWT.
auth.MapGet("/github/callback", async (HttpContext ctx, AppDbContext db, JwtTokenService tokens, string returnUrl) =>
{
    var result = await ctx.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    if (!result.Succeeded || result.Principal is null)
        return Results.Unauthorized();

    var gh = result.Principal;
    var githubId = gh.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (githubId is null) return Results.Unauthorized();

    var user = await db.Users.SingleOrDefaultAsync(u => u.GithubId == githubId);
    if (user is null)
    {
        user = new User
        {
            GithubId = githubId,
            GithubLogin = gh.FindFirst(ClaimTypes.Name)?.Value,
            Email = gh.FindFirst(ClaimTypes.Email)?.Value,
            DisplayName = gh.FindFirst("urn:github:name")?.Value
                          ?? gh.FindFirst(ClaimTypes.Name)?.Value
                          ?? "GitHub user",
            AvatarUrl = gh.FindFirst("urn:github:avatar")?.Value,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
    }

    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

    var token = tokens.CreateToken(user);

    // returnUrl is treated as a path on OUR front-end only — never an absolute URL.
    // The origin is hardcoded so the token can never be redirected to an attacker-supplied host.
    var safePath = Uri.IsWellFormedUriString(returnUrl, UriKind.Relative) ? returnUrl : "/";
    // Hand the JWT back to the SPA via fragment (kept out of server/proxy logs).
    return Results.Redirect($"{FrontendBaseUrl}{safePath}#token={token}");
});

// 3. Mint a CLI/MCP PAT (called from the website while logged in).
auth.MapPost("/cli-token", async (CliTokenRequest req, ClaimsPrincipal me, AppDbContext db) =>
{
    var userId = long.Parse(me.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    var raw = "pt_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    db.UserTokens.Add(new UserToken { UserId = userId, TokenHash = hash, Label = req.Label });
    await db.SaveChangesAsync();

    return Results.Ok(new { token = raw }); // shown once — the CLI/MCP store this
}).RequireAuthorization();

// 3b. List MY tokens — user id comes from the token (sub), never the URL. Hashes are never returned.
auth.MapGet("/tokens", async (ClaimsPrincipal me, AppDbContext db) =>
{
    var userId = long.Parse(me.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
    var tokens = await db.UserTokens
        .Where(t => t.UserId == userId)
        .OrderByDescending(t => t.CreatedAt)
        .Select(t => new
        {
            t.Id,
            t.Label,
            t.CreatedAt,
            t.LastUsedAt,
            t.RevokedAt,
            revoked = t.RevokedAt != null,
        })
        .ToListAsync();
    return Results.Ok(tokens);
}).RequireAuthorization();

// 3c. Revoke one of MY tokens. The id is in the URL, but the query is scoped to the
// authenticated user, so you can only ever revoke your own (a foreign id just 404s).
auth.MapDelete("/tokens/{id:long}", async (long id, ClaimsPrincipal me, AppDbContext db) =>
{
    var userId = long.Parse(me.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
    var token = await db.UserTokens.SingleOrDefaultAsync(t => t.Id == id && t.UserId == userId);
    if (token is null) return Results.NotFound();

    if (token.RevokedAt is null)
    {
        token.RevokedAt = DateTimeOffset.UtcNow; // soft-revoke: keep the row for audit; the handler rejects it
        await db.SaveChangesAsync();
    }
    return Results.NoContent();
}).RequireAuthorization();

// 4. Whoami — works for both JWT (web) and PAT (CLI/MCP) callers.
app.MapGet("/api/me", (ClaimsPrincipal me) => Results.Ok(new
{
    id = me.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
    email = me.FindFirst(JwtRegisteredClaimNames.Email)?.Value,
    name = me.FindFirst("name")?.Value,
})).RequireAuthorization();

app.Run();

record CliTokenRequest(string? Label);
