namespace PromptTrails.Enrichment;

/// <summary>
/// Turns text into a vector. One implementation per backend (Ollama today; Anthropic embeddings
/// or another local model later) — swap via <c>Enrichment:Embedding:Provider</c> without touching
/// callers. <see cref="Dimensions"/> must equal the vector(N) column width.
/// </summary>
public interface IEmbeddingProvider
{
    int Dimensions { get; }

    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
}
