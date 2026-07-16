using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace PromptTrails.Enrichment;

/// <summary>
/// Summarizes one prompt turn with a LOCAL model served by Ollama (default: qwen2.5-coder:7b) via
/// <c>POST {LocalBaseUrl}/api/chat</c>. Free and private — the cost win over Haiku — at the price of
/// lower faithfulness, so the worker only routes the head of the backlog here and spills the overflow
/// to Haiku (see <see cref="EnrichmentWorker"/>). Ollama's structured-output <c>format</c> field pins
/// the response to the <see cref="PromptSummary"/> schema, so the JSON stays reliable even though the
/// model is weaker than Haiku.
///
/// Shares the same system-instruction file and <see cref="SummarizerContent"/> user-content builder as
/// <see cref="HaikuSummarizer"/> so the two backends emit comparably-shaped summaries.
/// </summary>
public class OllamaSummarizer : ISummarizer
{
    private readonly HttpClient _http;
    private readonly EnrichmentOptions _opts;
    private readonly OllamaGate _gate;
    private readonly string _model;
    private readonly string _systemPrompt;

    public OllamaSummarizer(
        IOptions<EnrichmentOptions> opts,
        IHostEnvironment env,
        OllamaGate gate,
        ILogger<OllamaSummarizer> log)
    {
        _opts = opts.Value;
        _gate = gate;
        _model = _opts.Summarizer.LocalModel;

        // One long-lived HttpClient (the recommended reuse pattern). Local model calls can be slow,
        // so the timeout is its own knob (Summarizer:LocalTimeoutSeconds), separate from Haiku's.
        _http = new HttpClient
        {
            BaseAddress = new Uri(_opts.Summarizer.LocalBaseUrl),
            Timeout = TimeSpan.FromSeconds(_opts.Summarizer.LocalTimeoutSeconds),
        };

        var path = Path.Combine(env.ContentRootPath, _opts.Summarizer.PromptTemplatePath);
        _systemPrompt = File.Exists(path)
            ? File.ReadAllText(path)
            : throw new FileNotFoundException(
                $"Summarizer prompt template not found at '{path}'. Set Enrichment:Summarizer:PromptTemplatePath.");
    }

    public string ModelName => _model;

    public async Task<PromptSummary> SummarizeAsync(SummarizerInput input, CancellationToken ct = default)
    {
        var userContent = SummarizerContent.BuildUserContent(input, _opts);

        var request = new OllamaChatRequest(
            Model: _model,
            Stream: false,
            Format: SummarySchema,            // force the response to match PromptSummary
            Options: new OllamaOptions(Temperature: 0),
            Messages:
            [
                new OllamaMessage("system", _systemPrompt),
                new OllamaMessage("user", userContent),
            ]);

        // Gate BEFORE sending so queue-wait isn't counted against HttpClient.Timeout.
        var body = await _gate.RunAsync(async () =>
        {
            var res = await _http.PostAsJsonAsync("/api/chat", request, ct);
            res.EnsureSuccessStatusCode();
            return await res.Content.ReadFromJsonAsync<OllamaChatResponse>(ct);
        }, ct)
        ?? throw new InvalidOperationException("Ollama returned an empty chat response.");

        var json = body.Message?.Content;
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("Ollama returned no summary content.");

        return JsonSerializer.Deserialize<PromptSummary>(json)
               ?? throw new InvalidOperationException("Ollama summary did not parse into PromptSummary.");
    }

    // JSON schema Ollama enforces on the response — the local mirror of Haiku's structured output.
    // Serialized as its runtime (anonymous) shape by System.Text.Json.
    // Field names MUST match PromptSummary's [JsonPropertyName]s exactly — Ollama emits whatever keys
    // this schema names, and the deserializer binds by those keys. They drifted apart once (the schema
    // said "solution_embedding_text" while the DTO reads "solution"), which silently left Solution
    // empty on every row. Keep this list in lockstep with PromptSummary.
    private static readonly object SummarySchema = new
    {
        type = "object",
        properties = new
        {
            problem = new { type = "string" },
            solution = new { type = "string" },
            terms = new { type = "array", items = new { type = "string" } },
            rejected = new { type = "string" },
            outcome = new { type = "string" },
            problem_useful = new { type = "number" },
            solution_useful = new { type = "number" },
        },
        required = new[] { "problem", "solution", "terms", "rejected", "outcome", "problem_useful", "solution_useful" },
    };

    private record OllamaChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("format")] object Format,
        [property: JsonPropertyName("options")] OllamaOptions Options,
        [property: JsonPropertyName("messages")] IReadOnlyList<OllamaMessage> Messages);

    private record OllamaOptions(
        [property: JsonPropertyName("temperature")] double Temperature);

    private record OllamaMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private record OllamaChatResponse(
        [property: JsonPropertyName("message")] OllamaChatMessage? Message);

    private record OllamaChatMessage(
        [property: JsonPropertyName("content")] string? Content);
}
