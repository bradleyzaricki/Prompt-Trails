namespace PromptTrails.Models;

/// <summary>
/// Personal access token (PAT) for non-interactive callers — the CLI and the MCP client.
/// Only the SHA-256 hash is stored; the raw "pt_..." value is shown once at creation.
/// One per machine; revoke individually via <see cref="RevokedAt"/>.
/// </summary>
public class UserToken
{
    public long Id { get; set; }

    public long UserId { get; set; }
    public User User { get; set; } = null!;

    public string TokenHash { get; set; } = null!;
    public string? Label { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
