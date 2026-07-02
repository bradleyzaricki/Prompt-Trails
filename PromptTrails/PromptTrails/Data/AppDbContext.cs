using Microsoft.EntityFrameworkCore;
using PromptTrails.Models;

namespace PromptTrails.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserToken> UserTokens => Set<UserToken>();

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
    }
}
