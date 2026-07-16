namespace PromptTrails.Enrichment;

/// <summary>
/// Turns text into a vector. One implementation per backend (Ollama today; Anthropic embeddings
/// or another local model later) — swap via <c>Enrichment:Embedding:Provider</c> without touching
/// callers. <see cref="Dimensions"/> must equal the vector(N) column width.
/// </summary>
public interface IEmbeddingProvider
{
    int Dimensions { get; }

    /// <summary>The concrete model that produces the vectors — e.g. "nomic-embed-text". Persisted
    /// per row (<c>prompt_entries.embedding_model</c>) so a row's embedding provenance is readable.</summary>
    string ModelName { get; }

    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
}
