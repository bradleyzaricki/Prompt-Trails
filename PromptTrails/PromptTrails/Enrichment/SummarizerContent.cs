using System.Text;

namespace PromptTrails.Enrichment;

/// <summary>
/// Builds the user-content block fed to any summarizer backend (Haiku or local Ollama), applying the
/// per-field truncation caps from <see cref="EnrichmentOptions"/>. Shared so both backends see
/// identically-shaped input and the two summaries are directly comparable during tuning.
/// </summary>
internal static class SummarizerContent
{
    public static string BuildUserContent(SummarizerInput input, EnrichmentOptions opts)
    {
        var sb = new StringBuilder();
        sb.Append("## Developer prompt\n").Append(Truncate(input.PromptText, opts.MaxPromptChars)).Append("\n\n");

        if (!string.IsNullOrWhiteSpace(input.AssistantResponse))
            sb.Append("## Agent response\n").Append(Truncate(input.AssistantResponse, opts.MaxResponseChars)).Append("\n\n");

        if (input.Tools.Count > 0)
            sb.Append("## Tools used\n").Append(string.Join(", ", input.Tools)).Append("\n\n");

        if (!string.IsNullOrWhiteSpace(input.Diff))
            sb.Append("## Code diff\n```diff\n").Append(Truncate(input.Diff!, opts.MaxDiffChars)).Append("\n```\n");

        return sb.ToString();
    }

    public static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + $"\n… [truncated {s.Length - max} chars]";
}
