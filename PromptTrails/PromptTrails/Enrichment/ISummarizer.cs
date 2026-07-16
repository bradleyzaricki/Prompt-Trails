namespace PromptTrails.Enrichment;

/// <summary>Everything the summarizer gets to see for one prompt. The diff is included so Haiku
/// grounds its summary in what actually changed rather than confabulating from the prose.</summary>
public record SummarizerInput(
    string PromptText,
    string AssistantResponse,
    string? Diff,
    IReadOnlyList<string> Tools);

/// <summary>
/// Produces a <see cref="PromptSummary"/> from a prompt + its context. Default implementation is
/// Haiku; swap via <c>Enrichment:Summarizer:Provider</c>. The interface returns the structured
/// object so callers never parse model output themselves.
/// </summary>
public interface ISummarizer
{
    /// <summary>The concrete model that produces the summary (and therefore the embed text) — e.g.
    /// "claude-haiku-4-5" or "qwen2.5-coder:7b". Logged per row so it's visible which backend wrote
    /// each summary, especially under hybrid routing where a batch is split across models.</summary>
    string ModelName { get; }

    Task<PromptSummary> SummarizeAsync(SummarizerInput input, CancellationToken ct = default);
}

