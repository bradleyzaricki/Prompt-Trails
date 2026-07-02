namespace PromptTrails.Models;

public class User
{
    public long Id { get; set; }

    // The GitHub account IS the identity — no password.
    public string GithubId { get; set; } = null!;   
    public string? GithubLogin { get; set; }         
    public string? Email { get; set; }         
    public string DisplayName { get; set; } = null!;
    public string? AvatarUrl { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<UserToken> Tokens { get; set; } = new List<UserToken>();
}
