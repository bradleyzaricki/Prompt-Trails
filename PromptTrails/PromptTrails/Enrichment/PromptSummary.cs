using System.Text.Json.Serialization;

namespace PromptTrails.Enrichment;

/// <summary>
/// The structured summary Haiku produces for one prompt. Stored as jsonb in
/// <c>prompt_entries.summary</c>. Each field earns its place:
/// <list type="bullet">
///   <item><b>Problem</b> — what the user was trying to do (intent, not transcript).</item>
///   <item><b>Solution</b> — what was actually done to solve it.</item>
///   <item><b>Terms</b> — concrete jargon: function/file/class/symbol names. This is the
///     vocabulary-mismatch fix for full-text search — the exact identifiers a future query
///     would grep for, extracted even if the prose never repeats them.</item>
///   <item><b>Rejected</b> — approaches considered and discarded, so "why not X" is searchable.</item>
///   <item><b>Outcome</b> — did it land / get accepted / get reverted.</item>
///   <item><b>EmbeddingText</b> — the denoised text we actually embed (Haiku synthesizes this;
///     we embed it rather than the raw prompt so the vector reflects meaning, not noise).</item>
/// </list>
/// </summary>
public class PromptSummary
{
    [JsonPropertyName("solution")]
    public string Solution { get; set; } = "";

    [JsonPropertyName("terms")]
    public List<string> Terms { get; set; } = new();

    [JsonPropertyName("rejected")]
    public string Rejected { get; set; } = "";

    [JsonPropertyName("outcome")]
    public string Outcome { get; set; } = "";

    [JsonPropertyName("problem")]
    public string Problem { get; set; } = "";
}
