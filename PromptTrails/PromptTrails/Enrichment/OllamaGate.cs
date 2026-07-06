using Microsoft.Extensions.Options;

namespace PromptTrails.Enrichment;

/// <summary>
/// Throttles access to the single local Ollama server. On modest hardware (e.g. a 16 GB laptop),
/// running several 7B generations plus embeddings at once thrashes memory and everything times out —
/// so all local traffic, summaries AND embeddings, funnels through this shared gate
/// (<c>Enrichment:OllamaMaxConcurrency</c>, default 2). Two permits let one generation and one
/// embedding proceed together without a stampede.
///
/// The permit is acquired BEFORE the HTTP request is sent, so time spent queued here is NOT counted
/// against the caller's <c>HttpClient.Timeout</c>. Cloud (Haiku) calls never pass through this gate,
/// so overflow still runs fully in parallel with the local pipeline.
/// </summary>
public sealed class OllamaGate : IDisposable
{
    private readonly SemaphoreSlim _sem;

    public OllamaGate(IOptions<EnrichmentOptions> opts)
        => _sem = new SemaphoreSlim(Math.Max(1, opts.Value.OllamaMaxConcurrency));

    public async Task<T> RunAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        await _sem.WaitAsync(ct);
        try { return await action(); }
        finally { _sem.Release(); }
    }

    public void Dispose() => _sem.Dispose();
}
