namespace PromptTrails.Models;

/// <summary>
/// A tracked codebase. The server deliberately stores NO filesystem path — paths are
/// machine-local and live only in the CLI. The CLI maps a local folder to this project's
/// <see cref="Id"/> and sends that id on every push.
/// </summary>
public class Project
{
    public long Id { get; set; }

    // Who created the project. Not necessarily the author of every prompt once sharing exists.
    public long OwnerId { get; set; }
    public User Owner { get; set; } = null!;

    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<Session> Sessions { get; set; } = new List<Session>();
}
