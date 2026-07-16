using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PromptTrails.Data;

namespace PromptTrails.Endpoints;

/// <summary>
/// Dashboard aggregate for the contribution-graph landing page. Everything is computed in SQL and
/// scoped to the caller (optionally to one project) — the client never pulls rows to aggregate.
/// </summary>
public static class StatsEndpoints
{
    public static IEndpointRouteBuilder MapStatsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/stats", async (long? projectId, ClaimsPrincipal me, AppDbContext db, CancellationToken ct) =>
        {
            var userId = me.UserId();

            var baseq = db.PromptEntries.Where(p => p.Session.UserId == userId);
            if (projectId is not null) baseq = baseq.Where(p => p.Session.ProjectId == projectId);

            var totalPrompts = await baseq.CountAsync(ct);
            var enrichedCount = await baseq.CountAsync(p => p.EnrichedAt != null, ct);
            var totalSessions = await baseq.Select(p => p.SessionId).Distinct().CountAsync(ct);
            var linesAdded = await baseq.SumAsync(p => p.LinesAdded, ct);
            var linesRemoved = await baseq.SumAsync(p => p.LinesRemoved, ct);
            var filesChanged = await baseq.SumAsync(p => p.FilesChanged, ct);

            var categories = await baseq
                .GroupBy(p => p.Category)
                .Select(g => new { category = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .ToListAsync(ct);

            // languages is a Postgres text[]; unnest to histogram it. projectId is an optional filter
            // (NULL param -> the cast-guarded OR short-circuits to "no project filter").
            var languages = await db.Database
                .SqlQuery<LangCount>($"""
                    SELECT lang AS "Language", count(*) AS "Count"
                    FROM prompt_entries p
                    JOIN sessions s ON s.id = p.session_id, unnest(p.languages) AS lang
                    WHERE s.user_id = {userId}
                      AND (CAST({projectId} AS bigint) IS NULL OR s.project_id = CAST({projectId} AS bigint))
                    GROUP BY lang
                    ORDER BY count(*) DESC
                    LIMIT 25
                    """)
                .ToListAsync(ct);

            // Daily prompt counts for the last 53 weeks, bucketed by UTC date — feeds the heatmap.
            var activity = await db.Database
                .SqlQuery<DayCount>($"""
                    SELECT (p.submitted_at AT TIME ZONE 'UTC')::date AS "Day", count(*) AS "Count"
                    FROM prompt_entries p
                    JOIN sessions s ON s.id = p.session_id
                    WHERE s.user_id = {userId}
                      AND (CAST({projectId} AS bigint) IS NULL OR s.project_id = CAST({projectId} AS bigint))
                      AND p.submitted_at >= now() - interval '371 days'
                    GROUP BY 1
                    ORDER BY 1
                    """)
                .ToListAsync(ct);

            return Results.Ok(new
            {
                totalPrompts,
                totalSessions,
                enrichedCount,
                enrichmentCoverage = totalPrompts == 0 ? 0.0 : Math.Round((double)enrichedCount / totalPrompts, 3),
                linesAdded,
                linesRemoved,
                filesChanged,
                categories,
                languages = languages.Select(l => new { language = l.Language, count = l.Count }),
                activity = activity.Select(d => new { day = d.Day, count = d.Count }),
            });
        }).RequireAuthorization();

        return app;
    }

    private record LangCount(string Language, long Count);
    private record DayCount(DateOnly Day, long Count);
}
