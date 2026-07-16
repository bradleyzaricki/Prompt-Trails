using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PromptTrails.Data;

namespace PromptTrails.Endpoints;

/// <summary>
/// Project detail endpoint — returns the project header plus aggregated KPIs in one round trip.
/// 404 if the project doesn't exist or doesn't belong to the caller.
/// </summary>
public static class ProjectEndpoints
{
    public static IEndpointRouteBuilder MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/projects/{id:long}", async (long id, ClaimsPrincipal me, AppDbContext db, CancellationToken ct) =>
        {
            var userId = me.UserId();

            var project = await db.Projects
                .Where(p => p.Id == id && p.OwnerId == userId)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.Description,
                    p.CreatedAt,
                    SessionCount = p.Sessions.Count,
                    PromptCount = p.Sessions.SelectMany(s => s.Prompts).Count(),
                    EnrichedCount = p.Sessions.SelectMany(s => s.Prompts).Count(e => e.EnrichedAt != null),
                    LinesAdded = p.Sessions.SelectMany(s => s.Prompts).Sum(e => (int?)e.LinesAdded) ?? 0,
                    LinesRemoved = p.Sessions.SelectMany(s => s.Prompts).Sum(e => (int?)e.LinesRemoved) ?? 0,
                    FilesChanged = p.Sessions.SelectMany(s => s.Prompts).Sum(e => (int?)e.FilesChanged) ?? 0,
                })
                .SingleOrDefaultAsync(ct);

            if (project is null) return Results.NotFound();
            return Results.Ok(project);
        }).RequireAuthorization();

        return app;
    }
}
