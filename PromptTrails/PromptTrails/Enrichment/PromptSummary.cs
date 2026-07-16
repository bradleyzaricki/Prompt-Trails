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
///   <item><b>ProblemUseful</b> / <b>SolutionUseful</b> — 0.0–1.0 scores of how reusable the
///     <i>problem</i> and the <i>solution</i> are, judged independently on the specificity/durability
///     of each summary. Scored apart because a turn can have a weak problem but a strong solution:
///     "what did you implement last" is a vague, time-varying problem (→0) with a specific, reusable
///     solution (→high). Operational commands that carry no design decision ("start the server") score
///     ~0 on both. Copied to <c>prompt_entries.problem_useful</c> / <c>solution_useful</c>.</item>
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

    /// <summary>How reusable the <b>problem</b> is, 0.0 (vague/operational/time-varying) to 1.0
    /// (a specific, durable problem someone would genuinely search for again). Judged on the
    /// specificity of the <see cref="Problem"/> summary. Defaults to 0.0 so empty turns score zero.</summary>
    [JsonPropertyName("problem_useful")]
    public double ProblemUseful { get; set; }

    /// <summary>How reusable the <b>solution</b> is, 0.0 (no real solution / trivial / operational) to
    /// 1.0 (a concrete, durable technique or answer that generalizes and could be applied again).
    /// Judged on the specificity of the <see cref="Solution"/> summary, independently of the problem —
    /// a vague question can still yield a highly reusable solution. Defaults to 0.0.</summary>
    [JsonPropertyName("solution_useful")]
    public double SolutionUseful { get; set; }
}
