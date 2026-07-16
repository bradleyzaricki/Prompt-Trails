using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PromptTrails.Data;
using PromptTrails.Models;

namespace PromptTrails.Endpoints;

/// <summary>
/// Read endpoints that back the web history feed and the prompt detail page. Split out of Program.cs
/// to keep the composition root readable. All queries are scoped to the caller through the session
/// (session.user_id) — a user can only ever read their own prompts.
/// </summary>
public static class PromptReadEndpoints
{
    public static IEndpointRouteBuilder MapPromptReadEndpoints(this IEndpointRouteBuilder app)
    {
        // ── History feed ─────────────────────────────────────────────────────────
        // Keyset-paginated list of the caller's prompts as GitHub-style "commit rows".
        // Sort: "recent" (default, by submittedAt) | "solutionUseful" | "problemUseful".
        // Filters: projectId, sessionId, category, enriched. Returns { items, nextCursor }.
        app.MapGet("/api/prompts", async (
            long? projectId, long? sessionId, string? category, bool? enriched, string? sort,
            string? cursor, int? limit, ClaimsPrincipal me, AppDbContext db, CancellationToken ct) =>
        {
            var userId = me.UserId();
            var take = Math.Clamp(limit ?? 30, 1, 100);
            sort ??= "recent";

            var q = db.PromptEntries.Where(p => p.Session.UserId == userId);
            if (projectId is not null) q = q.Where(p => p.Session.ProjectId == projectId);
            if (sessionId is not null) q = q.Where(p => p.SessionId == sessionId);
            if (!string.IsNullOrWhiteSpace(category)) q = q.Where(p => p.Category == category);
            if (enriched is true) q = q.Where(p => p.EnrichedAt != null);
            else if (enriched is false) q = q.Where(p => p.EnrichedAt == null);

            var cur = Cursor.Decode(cursor);
            IOrderedQueryable<PromptEntry> ordered;

            if (sort is "solutionUseful" or "problemUseful")
            {
                var problemAxis = sort == "problemUseful";
                // Usefulness sort is only meaningful over scored rows; keyset needs a non-null key.
                q = problemAxis ? q.Where(p => p.ProblemUseful != null) : q.Where(p => p.SolutionUseful != null);
                if (cur is { } c && double.TryParse(c.sortValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var uv))
                    q = problemAxis
                        ? q.Where(p => p.ProblemUseful < uv || (p.ProblemUseful == uv && p.Id < c.id))
                        : q.Where(p => p.SolutionUseful < uv || (p.SolutionUseful == uv && p.Id < c.id));
                ordered = problemAxis
                    ? q.OrderByDescending(p => p.ProblemUseful).ThenByDescending(p => p.Id)
                    : q.OrderByDescending(p => p.SolutionUseful).ThenByDescending(p => p.Id);
            }
            else
            {
                if (cur is { } c && DateTimeOffset.TryParse(c.sortValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ts))
                    q = q.Where(p => p.SubmittedAt < ts || (p.SubmittedAt == ts && p.Id < c.id));
                ordered = q.OrderByDescending(p => p.SubmittedAt).ThenByDescending(p => p.Id);
            }

            // Pull take+1 to detect whether another page exists. Excerpt is bounded in SQL so a giant
            // pasted prompt never bloats the feed payload.
            var rows = await ordered.Take(take + 1).Select(p => new PromptListRow(
                p.Id,
                p.PromptText.Substring(0, 200),
                p.ProblemEmbeddingText,
                p.Category,
                p.FilesChanged, p.LinesAdded, p.LinesRemoved,
                p.Languages,
                p.SubmittedAt,
                p.SessionId, p.Session.ProjectId,
                p.EnrichedAt, p.ProblemUseful, p.SolutionUseful,
                p.Responses.Any(r => r.Status == "rejected"),
                p.Responses.Count
            )).ToListAsync(ct);

            var hasMore = rows.Count > take;
            var page = rows.Take(take).ToList();

            string? nextCursor = null;
            if (hasMore && page.Count > 0)
            {
                var last = page[^1];
                var sortValue = sort switch
                {
                    "solutionUseful" => (last.SolutionUseful ?? 0).ToString(CultureInfo.InvariantCulture),
                    "problemUseful" => (last.ProblemUseful ?? 0).ToString(CultureInfo.InvariantCulture),
                    _ => last.SubmittedAt.ToString("O"),
                };
                nextCursor = Cursor.Encode(sortValue, last.Id);
            }

            var items = page.Select(r => new
            {
                id = r.Id,
                title = ApiHelpers.FirstLine(string.IsNullOrWhiteSpace(r.ProblemSummary) ? r.Excerpt : r.ProblemSummary),
                excerpt = ApiHelpers.FirstLine(r.Excerpt, 200),
                category = r.Category,
                filesChanged = r.FilesChanged,
                linesAdded = r.LinesAdded,
                linesRemoved = r.LinesRemoved,
                languages = r.Languages,
                submittedAt = r.SubmittedAt,
                sessionId = r.SessionId,
                projectId = r.ProjectId,
                enriched = r.EnrichedAt != null,
                problemUseful = r.ProblemUseful,
                solutionUseful = r.SolutionUseful,
                hasRejected = r.HasRejected,
                toolCount = r.ToolCount,
            });

            return Results.Ok(new { items, nextCursor });
        }).RequireAuthorization();

        // ── Prompt detail ────────────────────────────────────────────────────────
        // Everything the detail page needs in one round trip: the prompt + assistant response,
        // the unified diff, the ordered tool trail (with accept/reject status), and the enrichment.
        app.MapGet("/api/prompts/{id:long}", async (long id, ClaimsPrincipal me, AppDbContext db, CancellationToken ct) =>
        {
            var userId = me.UserId();
            var p = await db.PromptEntries
                .Where(e => e.Id == id && e.Session.UserId == userId)
                .Select(e => new
                {
                    e.Id,
                    e.SessionId,
                    ProjectId = e.Session.ProjectId,
                    e.PromptText,
                    e.AssistantResponse,
                    e.Category,
                    e.Diff,
                    e.FilesChanged,
                    e.LinesAdded,
                    e.LinesRemoved,
                    e.FileExtensions,
                    e.Languages,
                    e.SubmittedAt,
                    e.CreatedAt,
                    e.EnrichedAt,
                    e.ProblemUseful,
                    e.SolutionUseful,
                    e.EmbeddingModel,
                    e.Summary,
                    Responses = e.Responses.OrderBy(r => r.Id).Select(r => new
                    {
                        r.Id, r.ToolName, r.ToolInput, r.ToolOutput, r.Status, r.ToolUseId, r.CreatedAt, r.ResolvedAt,
                    }).ToList(),
                })
                .SingleOrDefaultAsync(ct);

            if (p is null) return Results.NotFound();

            return Results.Ok(new
            {
                p.Id,
                p.SessionId,
                p.ProjectId,
                p.PromptText,
                p.AssistantResponse,
                p.Category,
                p.Diff,
                p.FilesChanged,
                p.LinesAdded,
                p.LinesRemoved,
                p.FileExtensions,
                p.Languages,
                p.SubmittedAt,
                p.CreatedAt,
                enrichment = new
                {
                    enriched = p.EnrichedAt != null,
                    enrichedAt = p.EnrichedAt,
                    problemUseful = p.ProblemUseful,
                    solutionUseful = p.SolutionUseful,
                    embeddingModel = p.EmbeddingModel,
                    summary = ApiHelpers.AsJson(p.Summary),
                },
                responses = p.Responses.Select(r => new
                {
                    r.Id,
                    r.ToolName,
                    toolInput = ApiHelpers.AsJson(r.ToolInput),
                    toolOutput = ApiHelpers.AsJson(r.ToolOutput),
                    r.Status,
                    r.ToolUseId,
                    r.CreatedAt,
                    r.ResolvedAt,
                }),
            });
        }).RequireAuthorization();

        return app;
    }
}

/// <summary>Flat row shape materialized from the feed query before it's shaped into the response.</summary>
internal record PromptListRow(
    long Id,
    string Excerpt,
    string? ProblemSummary,
    string Category,
    int FilesChanged,
    int LinesAdded,
    int LinesRemoved,
    List<string> Languages,
    DateTimeOffset SubmittedAt,
    long SessionId,
    long ProjectId,
    DateTimeOffset? EnrichedAt,
    double? ProblemUseful,
    double? SolutionUseful,
    bool HasRejected,
    int ToolCount);
