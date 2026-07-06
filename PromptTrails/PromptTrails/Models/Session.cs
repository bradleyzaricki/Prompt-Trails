namespace PromptTrails.Models;

/// <summary>
/// One coding-agent session on one machine. Upserted by <see cref="AgentSessionId"/> — the CLI
/// sends that id on every push and the server creates the row on first sight.
/// </summary>
public class Session
{
    public long Id { get; set; }

    public long UserId { get; set; }          // who ran the session
    public User User { get; set; } = null!;

    public long ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public string AgentSessionId { get; set; } = null!;   // session UUID from the coding agent (globally unique)

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }

    // ── Session context card (Phase 2 will inject this on the first prompt of a new session
    //    to restore working memory after a /clear). Columns land in Phase 0; the generation
    //    pass and the CLI injection hook come later. Null = no card generated yet.
    /// <summary>Haiku-generated session context card as raw JSON (jsonb).</summary>
    public string? ContextCard { get; set; }

    /// <summary>When the context card was generated. Null = not generated.</summary>
    public DateTimeOffset? CardAt { get; set; }

    public ICollection<PromptEntry> Prompts { get; set; } = new List<PromptEntry>();
}
