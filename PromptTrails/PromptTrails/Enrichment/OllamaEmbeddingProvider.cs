using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace PromptTrails.Enrichment;

/// <summary>
/// Embeds text with a local Ollama server (default model: nomic-embed-text, 768 dims). Local =
/// free and private, which is the cost-efficiency win; the trade-off is Ollama must be running.
/// Talks to <c>POST {BaseUrl}/api/embeddings</c> — the single-text endpoint that returns one
/// <c>embedding</c> array.
/// </summary>
public class OllamaEmbeddingProvider : IEmbeddingProvider
{
    private readonly HttpClient _http;
    private readonly EmbeddingOptions _opts;
    private readonly OllamaGate _gate;

    public OllamaEmbeddingProvider(HttpClient http, IOptions<EnrichmentOptions> opts, OllamaGate gate)
    {
        _opts = opts.Value.Embedding;
        _gate = gate;
        _http = http;
        _http.BaseAddress = new Uri(_opts.BaseUrl);
        _http.Timeout = TimeSpan.FromSeconds(_opts.TimeoutSeconds);
    }

    public int Dimensions => _opts.Dimensions;

    public string ModelName => _opts.Model;

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        // Gate BEFORE sending so queue-wait isn't counted against HttpClient.Timeout.
        var body = await _gate.RunAsync(async () =>
        {
            var res = await _http.PostAsJsonAsync(
                "/api/embeddings",
                new OllamaEmbedRequest(_opts.Model, text),
                ct);
            res.EnsureSuccessStatusCode();
            return await res.Content.ReadFromJsonAsync<OllamaEmbedResponse>(ct);
        }, ct)
        ?? throw new InvalidOperationException("Ollama returned an empty embedding response.");

        if (body.Embedding is not { Length: > 0 })
            throw new InvalidOperationException("Ollama returned no embedding vector.");

        if (body.Embedding.Length != _opts.Dimensions)
            throw new InvalidOperationException(
                $"Embedding dimension mismatch: model '{_opts.Model}' returned {body.Embedding.Length}, " +
                $"but the schema expects {_opts.Dimensions}. Fix Enrichment:Embedding:Dimensions and the vector(N) column.");

        return body.Embedding;
    }

    private record OllamaEmbedRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("prompt")] string Prompt);

    private record OllamaEmbedResponse(
        [property: JsonPropertyName("embedding")] float[]? Embedding);
}
