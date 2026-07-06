namespace PromptTrails.Enrichment;

/// <summary>One ranked search result. Kept minimal — callers hydrate full rows by id as needed.</summary>
public record SearchHit(
    long PromptEntryId,
    long SessionId,
    double Score,
    string? Summary);

/// <summary>
/// Hybrid retrieval over enriched prompts (Phase 3): vector similarity + full-text, fused with
/// RRF and adjusted by recency/project. Defined now so the MCP + search endpoint can be built
/// against a stable seam; the Phase-1 stub throws NotImplemented.
/// </summary>
public interface ISearchService
{
    Task<IReadOnlyList<SearchHit>> SearchAsync(
        string query,
        long userId,
        long? projectId,
        CancellationToken ct = default);
}
