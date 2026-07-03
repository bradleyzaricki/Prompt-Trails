using Microsoft.EntityFrameworkCore;
using PromptTrails.Models;

namespace PromptTrails.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserToken> UserTokens => Set<UserToken>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<PromptEntry> PromptEntries => Set<PromptEntry>();
    public DbSet<PromptResponse> PromptResponses => Set<PromptResponse>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Column/table names are snake_cased by UseSnakeCaseNamingConvention() in Program.cs.

        b.Entity<User>(e =>
        {
            e.Property(u => u.CreatedAt).HasDefaultValueSql("now()");
            e.HasIndex(u => u.GithubId).IsUnique();
        });

        b.Entity<UserToken>(e =>
        {
            e.Property(t => t.CreatedAt).HasDefaultValueSql("now()");
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasOne(t => t.User)
             .WithMany(u => u.Tokens)
             .HasForeignKey(t => t.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Project>(e =>
        {
            e.Property(p => p.CreatedAt).HasDefaultValueSql("now()");
            e.HasOne(p => p.Owner)
             .WithMany()
             .HasForeignKey(p => p.OwnerId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => p.OwnerId);
        });

        b.Entity<Session>(e =>
        {
            e.Property(s => s.StartedAt).HasDefaultValueSql("now()");
            e.HasIndex(s => s.AgentSessionId).IsUnique();     // upsert key
            e.HasOne(s => s.User)
             .WithMany()
             .HasForeignKey(s => s.UserId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(s => s.Project)
             .WithMany(p => p.Sessions)
             .HasForeignKey(s => s.ProjectId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PromptEntry>(e =>
        {
            e.Property(p => p.CreatedAt).HasDefaultValueSql("now()");
            e.Property(p => p.AssistantResponse).HasDefaultValue("");
            e.Property(p => p.Category).HasDefaultValue("other");
            // List<string> -> Postgres text[] (native array mapping, no extra config needed)
            e.HasIndex(p => p.PromptUuid).IsUnique();          // idempotency / dedup
            e.HasOne(p => p.Session)
             .WithMany(s => s.Prompts)
             .HasForeignKey(p => p.SessionId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PromptResponse>(e =>
        {
            e.Property(r => r.CreatedAt).HasDefaultValueSql("now()");
            e.Property(r => r.ToolInput).HasColumnType("jsonb").HasDefaultValue("{}");
            e.Property(r => r.ToolOutput).HasColumnType("jsonb");
            e.Property(r => r.Status).HasDefaultValue("pending");
            e.HasIndex(r => r.PromptEntryId);
            e.HasOne(r => r.PromptEntry)
             .WithMany(p => p.Responses)
             .HasForeignKey(r => r.PromptEntryId)
             .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
