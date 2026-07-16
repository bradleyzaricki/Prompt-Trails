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

        // pgvector lives in a Postgres extension; declaring it here makes the migration emit
        // `CREATE EXTENSION IF NOT EXISTS vector` before any vector column is created.
        b.HasPostgresExtension("vector");

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
            e.Property(s => s.ContextCard).HasColumnType("jsonb");   // Phase 2 session card
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

            // ── Enrichment columns ──────────────────────────────────────────────
            e.Property(p => p.Summary).HasColumnType("jsonb");
            // Fixed 768 dims (nomic-embed-text). Changing the embedding model means a new
            // migration for the column width — the dimension is not runtime-configurable.
            e.Property(p => p.ProblemEmbedding).HasColumnType("vector(768)");
            e.Property(p => p.SolutionEmbedding).HasColumnType("vector(768)");
            // Work queue: the enrichment worker scans `WHERE enriched_at IS NULL`. Partial
            // index keeps that scan tiny — it only holds the rows still needing work.
            e.HasIndex(p => p.EnrichedAt)
             .HasFilter("enriched_at IS NULL");

            // Approximate-nearest-neighbour index for cosine similarity search (Phase 3).
            // HNSW = fast queries, slower build; cosine matches how we'll rank.
            e.HasIndex(p => p.ProblemEmbedding)
                .HasMethod("hnsw")
                .HasOperators("vector_cosine_ops");

            e.HasIndex(p => p.SolutionEmbedding)
                .HasMethod("hnsw")
                .HasOperators("vector_cosine_ops");

            // Full-text half of hybrid search. STORED generated column over the denoised embedding
            // texts + raw prompt, so it stays in sync whenever the worker writes those fields — no
            // application code maintains it. GIN index makes @@ lookups fast. The two embedding-text
            // columns are nullable; the helper coalesces nulls before to_tsvector.
            e.HasGeneratedTsVectorColumn(
                    p => p.SearchVector!,
                    "english",
                    p => new { p.ProblemEmbeddingText, p.SolutionEmbeddingText, p.TermsText, p.PromptText })
                .HasIndex(p => p.SearchVector)
                .HasMethod("GIN");

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
