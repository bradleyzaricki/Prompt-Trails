using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using PromptTrails.Data;

namespace PromptTrails.Enrichment;

/// <summary>
/// Hybrid retrieval over enriched prompts (replaces <see cref="NullSearchService"/>). Fuses three
/// ranked candidate lists with Reciprocal Rank Fusion:
/// <list type="number">
///   <item>vector similarity of the query against each row's <c>problem_embedding</c> (HNSW),</item>
///   <item>vector similarity against each row's <c>solution_embedding</c> (HNSW),</item>
///   <item>Postgres full-text match against the generated <c>search_vector</c> (GIN).</item>
/// </list>
/// Each list's contribution is gated by the matching usefulness score — the problem list by
/// <c>problem_useful</c>, the solution list by <c>solution_useful</c> — so a turn is retrieved through
/// the axis it is actually reusable on (a vague question with a strong answer surfaces as a solution,
/// never as a problem). The fused score is then decayed by recency and boosted for the caller's active
/// project. All weights/knobs live in <see cref="SearchOptions"/> and are read live via IOptionsMonitor.
///
/// Scoping: results are always hard-filtered to the caller's own prompts (<c>session.user_id</c>) and
/// to enriched rows. <paramref name="projectId"/> is a soft boost, not a filter — cross-project history
/// is still surfaced, which is what the RAG/MCP use case wants.
///
/// Degradation: if the embedder is unavailable, the two vector lists are skipped and the query runs
/// full-text-only rather than failing.
/// </summary>
public class PgVectorSearchService(
    AppDbContext db,
    IEmbeddingProvider embedder,
    IOptionsMonitor<EnrichmentOptions> options,
    ILogger<PgVectorSearchService> log) : ISearchService
{
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(
        string query, long userId, long? projectId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var opts = options.CurrentValue.Search;
        // Pull a wider candidate pool from each modality than we return, so fusion has room to work.
        var candidateK = Math.Max(opts.MaxResults * 5, 20);

        // Embed the query for the vector legs. A dead embedder degrades to full-text only.
        Vector? queryVector = null;
        try
        {
            queryVector = new Vector(await embedder.EmbedAsync(query, ct));
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Query embedding failed; falling back to full-text-only search.");
        }

        // Base scope: the caller's own enriched prompts.
        var scoped = db.PromptEntries.Where(p => p.Session.UserId == userId && p.EnrichedAt != null);

        // ── Candidate list 1 & 2: vector similarity (ordered nearest-first → list position = rank) ──
        var problemIds = queryVector is null
            ? new List<long>()
            : await scoped
                .Where(p => p.ProblemEmbedding != null)
                .OrderBy(p => p.ProblemEmbedding!.CosineDistance(queryVector))
                .Take(candidateK)
                .Select(p => p.Id)
                .ToListAsync(ct);

        var solutionIds = queryVector is null
            ? new List<long>()
            : await scoped
                .Where(p => p.SolutionEmbedding != null)
                .OrderBy(p => p.SolutionEmbedding!.CosineDistance(queryVector))
                .Take(candidateK)
                .Select(p => p.Id)
                .ToListAsync(ct);

        // ── Candidate list 3: full-text (raw SQL — ts_rank ordering, index-backed via GIN) ──
        // Interpolated params are parameterized by EF, so this is injection-safe.
        var textIds = await db.Database
            .SqlQuery<long>($"""
                SELECT p.id AS "Value"
                FROM prompt_entries p
                JOIN sessions s ON s.id = p.session_id
                WHERE s.user_id = {userId}
                  AND p.enriched_at IS NOT NULL
                  AND p.search_vector @@ websearch_to_tsquery('english', {query})
                ORDER BY ts_rank(p.search_vector, websearch_to_tsquery('english', {query})) DESC
                LIMIT {candidateK}
                """)
            .ToListAsync(ct);

        // Nothing matched on any axis.
        var allIds = problemIds.Concat(solutionIds).Concat(textIds).Distinct().ToList();
        if (allIds.Count == 0)
            return [];

        // Fetch the ranking metadata for every candidate in one round-trip.
        var meta = await db.PromptEntries
            .Where(p => allIds.Contains(p.Id))
            .Select(p => new CandidateMeta(
                p.Id, p.SessionId, p.Session.ProjectId, p.SubmittedAt,
                p.ProblemUseful, p.SolutionUseful, p.Summary))
            .ToListAsync(ct);
        var byId = meta.ToDictionary(m => m.Id);

        // ── Reciprocal Rank Fusion, gated by usefulness ──
        // A row at 0-based position i in a list contributes weight * gate / (rrfK + i + 1).
        // A missing (null) usefulness score is treated as neutral (1.0) — we only penalize a row when
        // we have positive evidence it is not useful, so pre-scored legacy rows are not buried.
        var fused = new Dictionary<long, double>();

        void Fuse(List<long> ordered, double weight, Func<CandidateMeta, double> gate)
        {
            for (var i = 0; i < ordered.Count; i++)
            {
                if (!byId.TryGetValue(ordered[i], out var m)) continue;
                var contribution = weight * gate(m) / (opts.RrfK + i + 1);
                fused[m.Id] = fused.GetValueOrDefault(m.Id) + contribution;
            }
        }

        Fuse(problemIds, opts.VectorWeight, m => m.ProblemUseful ?? 1.0);
        Fuse(solutionIds, opts.VectorWeight, m => m.SolutionUseful ?? 1.0);
        // Full-text spans both texts, so gate it by whichever axis this row is more reusable on.
        Fuse(textIds, opts.TextWeight, m => Math.Max(m.ProblemUseful ?? 1.0, m.SolutionUseful ?? 1.0));

        // ── Recency decay + project boost ──
        var now = DateTimeOffset.UtcNow;
        var ranked = fused.Select(kv =>
            {
                var m = byId[kv.Key];
                var ageDays = Math.Max(0, (now - m.SubmittedAt).TotalDays);
                var recency = opts.RecencyHalfLifeDays > 0
                    ? Math.Pow(2, -ageDays / opts.RecencyHalfLifeDays)
                    : 1.0;
                var boost = projectId is not null && m.ProjectId == projectId ? opts.ProjectBoost : 1.0;
                return new SearchHit(m.Id, m.SessionId, kv.Value * recency * boost, m.Summary);
            })
            .OrderByDescending(h => h.Score)
            .Take(opts.MaxResults)
            .ToList();

        return ranked;
    }

    /// <summary>Per-candidate fields needed to rank; hydration of display fields is left to the caller.</summary>
    private sealed record CandidateMeta(
        long Id,
        long SessionId,
        long ProjectId,
        DateTimeOffset SubmittedAt,
        double? ProblemUseful,
        double? SolutionUseful,
        string? Summary);
}
