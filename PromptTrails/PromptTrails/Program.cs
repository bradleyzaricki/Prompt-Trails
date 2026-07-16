using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Pgvector.EntityFrameworkCore;
using PromptTrails.Auth;
using PromptTrails.Data;
using PromptTrails.Endpoints;
using PromptTrails.Enrichment;
using PromptTrails.Models;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

// Composition root: services are registered below, then endpoints are mapped inline (Minimal API style).

// Where the SPA lives. Post-login redirects are pinned to this origin so a caller-supplied
// returnUrl can never send the JWT to an off-site host (open-redirect / token theft).
const string FrontendBaseUrl = "http://localhost:2324";

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(cfg.GetConnectionString("Postgres"), o => o.UseVector())
       .UseSnakeCaseNamingConvention());

builder.Services.AddScoped<JwtTokenService>();

// ── Enrichment layer (async summaries + embeddings; see Enrichment/) ───────────
builder.Services.Configure<EnrichmentOptions>(cfg.GetSection(EnrichmentOptions.SectionName));

// The Anthropic SDK reads ANTHROPIC_API_KEY from the environment. Let config/user-secrets seed it
// (Enrichment:Summarizer:ApiKey) without forcing an env var in dev.
var anthropicKey = cfg[$"{EnrichmentOptions.SectionName}:Summarizer:ApiKey"];
if (!string.IsNullOrWhiteSpace(anthropicKey))
    Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", anthropicKey);

builder.Services.AddSingleton<OllamaGate>();                                    // throttles local Ollama traffic
builder.Services.AddHttpClient<IEmbeddingProvider, OllamaEmbeddingProvider>();  // typed client → Ollama

// Two summarizer backends, resolved by key. The worker routes between them (Enrichment:Summarizer:
// Provider): local Ollama by default, spilling overflow to Haiku once the backlog stacks past the
// threshold. Keyed + lazily resolved so an Ollama-only deployment never constructs the Haiku client
// (which needs an API key), and vice-versa.
builder.Services.AddKeyedSingleton<ISummarizer, HaikuSummarizer>("haiku");
builder.Services.AddKeyedSingleton<ISummarizer, OllamaSummarizer>("ollama");
// Convenience default = the primary provider, for any non-worker caller.
builder.Services.AddSingleton<ISummarizer>(sp =>
{
    var provider = sp.GetRequiredService<IOptions<EnrichmentOptions>>().Value.Summarizer.Provider;
    var key = provider.Equals("haiku", StringComparison.OrdinalIgnoreCase) ? "haiku" : "ollama";
    return sp.GetRequiredKeyedService<ISummarizer>(key);
});
builder.Services.AddScoped<ISearchService, PgVectorSearchService>();           // hybrid vector + full-text retrieval
builder.Services.AddHostedService<EnrichmentWorker>();

// Swagger / OpenAPI — a browsable test page at /swagger with an Authorize button.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new() { Title = "Prompt Trail API", Version = "v1" });

    // One "Bearer" scheme covers both credentials — paste a PAT (pt_...) or a JWT.
    var scheme = new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Paste a PAT (pt_...) or a JWT. Swagger adds the 'Bearer ' prefix for you.",
        Reference = new() { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" },
    };
    o.AddSecurityDefinition("Bearer", scheme);
    o.AddSecurityRequirement(new() { [scheme] = Array.Empty<string>() });
});

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

// Serve the OpenAPI doc + Swagger UI (dev only).
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(o => o.SwaggerEndpoint("/swagger/v1/swagger.json", "Prompt Trail API v1"));
}

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

// ── Ingest ───────────────────────────────────────────────────────────────────
// Works for both JWT (web) and PAT (CLI). userId always comes from the token, never the body.
static long CurrentUserId(ClaimsPrincipal me) =>
    long.Parse(me.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

// List MY projects.
app.MapGet("/api/projects", async (ClaimsPrincipal me, AppDbContext db) =>
{
    var userId = CurrentUserId(me);
    var projects = await db.Projects
        .Where(p => p.OwnerId == userId)
        .OrderByDescending(p => p.CreatedAt)
        .Select(p => new { p.Id, p.Name, p.Description, p.CreatedAt })
        .ToListAsync();
    return Results.Ok(projects);
}).RequireAuthorization();

// Create a project. The CLI calls this the first time it sees a new local folder, stores the
// returned id locally, and sends it on every push. No path is ever sent to the server.
app.MapPost("/api/projects", async (CreateProjectRequest req, ClaimsPrincipal me, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(req.Name))
        return Results.BadRequest(new { error = "name is required" });

    var project = new Project
    {
        OwnerId = CurrentUserId(me),
        Name = req.Name.Trim(),
        Description = req.Description,
    };
    db.Projects.Add(project);
    await db.SaveChangesAsync();
    return Results.Ok(new { project.Id, project.Name, project.Description, project.CreatedAt });
}).RequireAuthorization();

// Ingest one finalized prompt + its tool trail. Idempotent on promptUuid so a CLI retry
// can't double-write. Session is upserted by agentSessionId.
app.MapPost("/api/prompts", async (IngestPromptRequest req, ClaimsPrincipal me, AppDbContext db) =>
{
    var userId = CurrentUserId(me);

    if (string.IsNullOrWhiteSpace(req.PromptUuid))
        return Results.BadRequest(new { error = "promptUuid is required" });
    if (string.IsNullOrWhiteSpace(req.AgentSessionId))
        return Results.BadRequest(new { error = "agentSessionId is required" });

    // Idempotency: a row with this uuid already exists → no-op, return it.
    var existing = await db.PromptEntries
        .Where(p => p.PromptUuid == req.PromptUuid)
        .Select(p => new { p.Id })
        .SingleOrDefaultAsync();
    if (existing is not null)
        return Results.Ok(new { id = existing.Id, deduped = true });

    // The project must belong to the caller (prevents writing into someone else's project).
    var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == req.ProjectId && p.OwnerId == userId);
    if (project is null)
        return Results.BadRequest(new { error = "unknown or unauthorized projectId" });

    // Upsert the session by its agent-supplied id.
    var session = await db.Sessions.SingleOrDefaultAsync(s => s.AgentSessionId == req.AgentSessionId);
    if (session is null)
    {
        session = new Session
        {
            UserId = userId,
            ProjectId = project.Id,
            AgentSessionId = req.AgentSessionId,
            StartedAt = req.SubmittedAt,
        };
        db.Sessions.Add(session);
    }
    else if (session.UserId != userId)
    {
        return Results.BadRequest(new { error = "session belongs to another user" });
    }

    var entry = new PromptEntry
    {
        Session = session,
        PromptUuid = req.PromptUuid,
        PromptText = req.PromptText ?? "",
        AssistantResponse = req.AssistantResponse ?? "",
        SubmittedAt = req.SubmittedAt,
        Category = string.IsNullOrWhiteSpace(req.Category) ? "other" : req.Category,
        Diff = req.Diff,
        FilesChanged = req.FilesChanged,
        LinesAdded = req.LinesAdded,
        LinesRemoved = req.LinesRemoved,
        FileExtensions = req.FileExtensions ?? new(),
        Languages = req.Languages ?? new(),
        Responses = (req.Responses ?? new()).Select(r => new PromptResponse
        {
            ToolName = r.ToolName,
            ToolInput = r.ToolInput?.GetRawText() ?? "{}",   // JSON object on the wire -> jsonb text
            ToolOutput = r.ToolOutput?.GetRawText(),
            Status = string.IsNullOrWhiteSpace(r.Status) ? "pending" : r.Status,
            ToolUseId = r.ToolUseId ?? "",
            ResolvedAt = r.ResolvedAt,
        }).ToList(),
    };
    db.PromptEntries.Add(entry);
    await db.SaveChangesAsync();

    return Results.Ok(new { id = entry.Id, sessionId = session.Id, deduped = false });
}).RequireAuthorization();

// Inspect one prompt's enrichment (for `prompt-trail enrich --show <id>` and debugging the
// pipeline). Scoped to the caller through the session — you can only read your own prompts.
app.MapGet("/api/prompts/{id:long}/enrichment", async (long id, ClaimsPrincipal me, AppDbContext db) =>
{
    var userId = CurrentUserId(me);

    var entry = await db.PromptEntries
        .Where(p => p.Id == id && p.Session.UserId == userId)
        .Select(p => new
        {
            p.Id,
            p.Category,
            p.ProblemUseful,
            p.SolutionUseful,
            p.EnrichedAt,
            enriched = p.EnrichedAt != null,
            p.Summary,          // raw jsonb string ({problem, solution, terms, rejected, outcome, problem_useful, solution_useful})
            p.ProblemEmbeddingText,
            p.SolutionEmbeddingText,
            p.EmbeddingModel,
            hasEmbedding = p.ProblemEmbedding != null && p.SolutionEmbedding != null,

        })
        .SingleOrDefaultAsync();

    if (entry is null) return Results.NotFound();

    // Re-parse the jsonb summary so it comes back as a JSON object, not an escaped string.
    JsonElement? summary = string.IsNullOrWhiteSpace(entry.Summary)
        ? null
        : JsonSerializer.Deserialize<JsonElement>(entry.Summary);

    return Results.Ok(new
    {
        entry.Id,
        entry.Category,
        entry.ProblemUseful,
        entry.SolutionUseful,
        entry.EnrichedAt,
        entry.enriched,
        summary,
        entry.ProblemEmbeddingText,
        entry.SolutionEmbeddingText,
        entry.EmbeddingModel,
        entry.hasEmbedding,
    });
}).RequireAuthorization();

// ── Search ─────────────────────────────────────────────────────────────────────
// Hybrid retrieval over the caller's enriched prompts. The service ranks (vector + full-text, fused
// and gated by usefulness); the endpoint hydrates display fields for the top hits and re-parses the
// jsonb summary into an object. projectId is an optional soft boost, not a filter.
app.MapGet("/api/search", async (
    string q, long? projectId, ClaimsPrincipal me, ISearchService search, AppDbContext db, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(q))
        return Results.BadRequest(new { error = "q is required" });

    var userId = CurrentUserId(me);
    var hits = await search.SearchAsync(q, userId, projectId, ct);
    if (hits.Count == 0)
        return Results.Ok(Array.Empty<object>());

    var ids = hits.Select(h => h.PromptEntryId).ToList();
    var rows = await db.PromptEntries
        .Where(p => ids.Contains(p.Id))
        .Select(p => new
        {
            p.Id,
            p.PromptText,
            p.Category,
            p.ProblemUseful,
            p.SolutionUseful,
            p.SubmittedAt,
            ProjectId = p.Session.ProjectId,
        })
        .ToListAsync(ct);
    var byId = rows.ToDictionary(r => r.Id);

    // Preserve the service's score ordering; attach display fields + parsed summary.
    var results = hits.Select(h =>
    {
        var r = byId[h.PromptEntryId];
        JsonElement? summary = string.IsNullOrWhiteSpace(h.Summary)
            ? null
            : JsonSerializer.Deserialize<JsonElement>(h.Summary);
        return new
        {
            promptEntryId = h.PromptEntryId,
            sessionId = h.SessionId,
            projectId = r.ProjectId,
            score = h.Score,
            r.PromptText,
            r.Category,
            r.ProblemUseful,
            r.SolutionUseful,
            r.SubmittedAt,
            summary,
        };
    });

    return Results.Ok(results);
}).RequireAuthorization();

// ── Frontend read endpoints (history feed, prompt detail, sessions, project detail, stats) ─────
app.MapPromptReadEndpoints();
app.MapSessionEndpoints();
app.MapProjectEndpoints();
app.MapStatsEndpoints();

app.Run();

record CliTokenRequest(string? Label);

record CreateProjectRequest(string Name, string? Description);

record IngestPromptRequest(
    long ProjectId,
    string AgentSessionId,
    string PromptUuid,
    string? PromptText,
    string? AssistantResponse,
    DateTimeOffset SubmittedAt,
    string? Category,
    string? Diff,
    int FilesChanged,
    int LinesAdded,
    int LinesRemoved,
    List<string>? FileExtensions,
    List<string>? Languages,
    List<IngestResponseItem>? Responses);

record IngestResponseItem(
    string ToolName,
    JsonElement? ToolInput,
    JsonElement? ToolOutput,
    string? Status,
    string? ToolUseId,
    DateTimeOffset? ResolvedAt);
