using Pgvector;

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

    // ── Enrichment (written server-side by the enrichment worker, not by ingest) ──────────
    // Everything below is null until the worker processes the row. EnrichedAt is the gate:
    // `WHERE enriched_at IS NULL` is the work queue, and setting it marks the row done. This
    // keeps enrichment idempotent and cheap — a row is embedded/summarized exactly once.

    /// <summary>
    /// Haiku-generated structured summary as raw JSON (jsonb). Shape is defined by
    /// <c>Enrichment.PromptSummary</c>: { problem, solution, terms, rejected, outcome }.
    /// Stored as text and mapped to jsonb — the app reads/writes it as a serialized blob.
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>
    /// The exact text that was fed to the embedding model. Synthesized by Haiku (not the raw
    /// prompt) so noise is stripped before it hits vector space. Kept so we can re-embed
    /// without re-summarizing when the embedding model changes.
    /// </summary>
    public string? ProblemEmbeddingText { get; set; }
    
    public string? SolutionEmbeddingText { get; set; }


    /// <summary>
    /// Vector embedding of <see cref="EmbeddingText"/>. Dimension is fixed by the embedding
    /// model (nomic-embed-text = 768) and must match the migration's vector(N) column.
    /// </summary>
    public Vector? ProblemEmbedding { get; set; }
    
    public Vector? SolutionEmbedding { get; set; }


    /// <summary>Null = not yet enriched (work queue). Set once the worker finishes the row.</summary>
    public DateTimeOffset? EnrichedAt { get; set; }

    public ICollection<PromptResponse> Responses { get; set; } = new List<PromptResponse>();
}
