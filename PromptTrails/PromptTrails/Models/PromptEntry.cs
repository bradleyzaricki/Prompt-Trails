namespace PromptTrails.Models;

/// <summary>
/// One finalized prompt: the user's text, the assistant's response, the resulting diff, and
/// the tool breadcrumb trail (<see cref="Responses"/>). Written once by the CLI at ingest and
/// treated as immutable raw data — enrichment (category, ratings, embeddings) hangs off it in
/// separate tables. Deduped by <see cref="PromptUuid"/>.
///
/// A prompt belongs to a <see cref="Session"/>; its project and author are reached through the
/// session (Session.Project / Session.User) — not duplicated here.
/// </summary>
public class PromptEntry
{
    public long Id { get; set; }

    public long SessionId { get; set; }
    public Session Session { get; set; } = null!;

    public string PromptUuid { get; set; } = null!;   // idempotency key (globally unique)

    public string PromptText { get; set; } = null!;
    public string AssistantResponse { get; set; } = "";

    public DateTimeOffset SubmittedAt { get; set; }

    public string Category { get; set; } = "other";   // question/code_change/command/other

    public string? Diff { get; set; }
    public int FilesChanged { get; set; }
    public int LinesAdded { get; set; }
    public int LinesRemoved { get; set; }

    public List<string> FileExtensions { get; set; } = new();
    public List<string> Languages { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<PromptResponse> Responses { get; set; } = new List<PromptResponse>();
}
