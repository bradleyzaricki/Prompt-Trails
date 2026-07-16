using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PromptTrails.Data;

namespace PromptTrails.Endpoints;

/// <summary>
/// Session list endpoint for the history sidebar. Keyset-paginated, newest first.
/// Each item carries a prompt/enriched count so the UI can show a progress ring without
/// a second request.
/// </summary>
public static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/sessions", async (
            long? projectId, string? cursor, int? limit,
            ClaimsPrincipal me, AppDbContext db, CancellationToken ct) =>
        {
            var userId = me.UserId();
            var take = Math.Clamp(limit ?? 30, 1, 100);

            var q = db.Sessions.Where(s => s.UserId == userId);
            if (projectId is not null) q = q.Where(s => s.ProjectId == projectId);

            var cur = Cursor.Decode(cursor);
            if (cur is { } c
                && DateTimeOffset.TryParse(c.sortValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ts))
                q = q.Where(s => s.StartedAt < ts || (s.StartedAt == ts && s.Id < c.id));

            var rows = await q
                .OrderByDescending(s => s.StartedAt)
                .ThenByDescending(s => s.Id)
                .Take(take + 1)
                .Select(s => new
                {
                    s.Id,
                    s.ProjectId,
                    s.AgentSessionId,
                    s.StartedAt,
                    s.EndedAt,
                    HasContextCard = s.ContextCard != null,
                    PromptCount = s.Prompts.Count,
                    EnrichedCount = s.Prompts.Count(p => p.EnrichedAt != null),
                })
                .ToListAsync(ct);

            var hasMore = rows.Count > take;
            var page = rows.Take(take).ToList();

            string? nextCursor = null;
            if (hasMore && page.Count > 0)
            {
                var last = page[^1];
                nextCursor = Cursor.Encode(last.StartedAt.ToString("O"), last.Id);
            }

            return Results.Ok(new
            {
                items = page.Select(r => new
                {
                    r.Id,
                    r.ProjectId,
                    r.AgentSessionId,
                    r.StartedAt,
                    r.EndedAt,
                    r.HasContextCard,
                    r.PromptCount,
                    r.EnrichedCount,
                }),
                nextCursor,
            });
        }).RequireAuthorization();

        return app;
    }
}
