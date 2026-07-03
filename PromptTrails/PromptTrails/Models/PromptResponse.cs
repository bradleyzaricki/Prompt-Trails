namespace PromptTrails.Models;

/// <summary>
/// One entry in a prompt's tool breadcrumb trail (Read, Edit, Bash, AskUserQuestion, ...).
/// Insert-only children of a <see cref="PromptEntry"/> — the CLI sends the finalized trail
/// once with accept/reject status already resolved from the conversation log.
/// </summary>
public class PromptResponse
{
    public long Id { get; set; }

    public long PromptEntryId { get; set; }
    public PromptEntry PromptEntry { get; set; } = null!;

    public string ToolName { get; set; } = null!;
    public string ToolInput { get; set; } = "{}";     // jsonb
    public string? ToolOutput { get; set; }           // jsonb

    public string Status { get; set; } = "pending";   // pending/accepted/rejected
    public string ToolUseId { get; set; } = "";       // links back to the conversation log

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}
