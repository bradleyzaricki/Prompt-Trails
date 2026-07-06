namespace PromptTrails.Enrichment;

/// <summary>
/// Phase-1 placeholder so DI and the future search endpoint have a seam to bind against. Real
/// hybrid retrieval (vector + full-text + RRF) lands in Phase 3, replacing this registration.
/// </summary>
public class NullSearchService : ISearchService
{
    public Task<IReadOnlyList<SearchHit>> SearchAsync(
        string query, long userId, long? projectId, CancellationToken ct = default) =>
        throw new NotImplementedException("Hybrid search is implemented in Phase 3.");
}
