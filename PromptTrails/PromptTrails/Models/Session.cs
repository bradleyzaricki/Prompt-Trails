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

    public ICollection<PromptEntry> Prompts { get; set; } = new List<PromptEntry>();
}
