namespace PromptTrails.Enrichment;

/// <summary>
/// Every tunable knob for the enrichment + search layer, bound from the "Enrichment" section
/// of appsettings. This is deliberately the ONE place to tweak behaviour — the design brief is
/// "easy to fine-tune later", so provider choice, cost caps, and search weights all live here
/// rather than being scattered as constants. Search weights are read through IOptionsMonitor so
/// they can be changed without a restart (Phase 3).
/// </summary>
public class EnrichmentOptions
{
    public const string SectionName = "Enrichment";

    /// <summary>Master switch. Off = the background worker never runs (e.g. in tests/CI).</summary>
    public bool Enabled { get; set; } = true;

    // ── Worker cadence ────────────────────────────────────────────────────────
    /// <summary>Seconds the worker sleeps when it finds no un-enriched rows.</summary>
    public int PollSeconds { get; set; } = 15;

    /// <summary>How many un-enriched rows to claim per pass. Keeps memory + API bursts bounded.</summary>
    public int BatchSize { get; set; } = 10;

    // ── Cost controls (inputs are truncated before they reach Haiku) ───────────
    public int MaxPromptChars { get; set; } = 4000;
    public int MaxResponseChars { get; set; } = 4000;
    public int MaxDiffChars { get; set; } = 6000;

    /// <summary>Substance threshold for the empty-turn skip. A turn is only skipped (no model call)
    /// when it has no diff, no tool calls, AND a response shorter than this. Nothing here keys off the
    /// prompt — a short "yes" that produced a diff is summarized like any other turn.</summary>
    public int MinResponseChars { get; set; } = 200;

    /// <summary>Max concurrent requests to the local Ollama server (shared across summaries AND
    /// embeddings). Low by design: one local model instance can't handle a batch stampede on modest
    /// hardware. Raise it only if Ollama runs on a bigger/dedicated box.</summary>
    public int OllamaMaxConcurrency { get; set; } = 2;

    public EmbeddingOptions Embedding { get; set; } = new();
    public SummarizerOptions Summarizer { get; set; } = new();
    public SearchOptions Search { get; set; } = new();
}

/// <summary>Embedding provider config. Default = local Ollama (free), swappable by Provider.</summary>
public class EmbeddingOptions
{
    public string Provider { get; set; } = "ollama";
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "nomic-embed-text";

    /// <summary>Must match the vector(N) column width in the migration. Changing this needs a
    /// schema migration, so it's here for visibility, not casual tweaking.</summary>
    public int Dimensions { get; set; } = 768;

    /// <summary>Only counts the in-flight request — the wait for an <see cref="OllamaGate"/> permit
    /// happens before the timeout clock starts — so this can stay modest even under batch load.</summary>
    public int TimeoutSeconds { get; set; } = 60;
}

/// <summary>
/// Summarizer config. <c>Provider</c> selects the backend:
/// <list type="bullet">
///   <item><b>haiku</b> — every row goes to Claude Haiku (cloud, best quality, per-call cost).</item>
///   <item><b>ollama</b> — every row goes to the local model (free, weaker).</item>
///   <item><b>hybrid</b> — local by default; when the un-enriched backlog stacks past
///     <see cref="LocalQueueThreshold"/> the overflow spills to <see cref="OverflowProvider"/> so the
///     queue drains instead of bottlenecking on the slower local model. This is the cost/throughput
///     sweet spot: local absorbs steady load for free, Haiku only kicks in under a burst.</item>
/// </list>
/// </summary>
public class SummarizerOptions
{
    public string Provider { get; set; } = "hybrid";
    public string Model { get; set; } = "claude-haiku-4-5";
    public int MaxTokens { get; set; } = 1024;

    // ── Hybrid routing (Provider = "hybrid") ───────────────────────────────────
    /// <summary>Backlog depth the local model is allowed to "stack" before overflow is shed to the
    /// cloud. With the default 5: while ≤5 rows await enrichment they all run locally (free); the 6th
    /// and beyond in a pass are routed to <see cref="OverflowProvider"/>. Raise it to lean harder on
    /// the free local model (deeper queues, more latency); lower it to spend sooner for freshness.</summary>
    public int LocalQueueThreshold { get; set; } = 5;

    /// <summary>Where overflow goes once the local stack is full. "haiku" by default; could be another
    /// cloud tier later.</summary>
    public string OverflowProvider { get; set; } = "haiku";

    /// <summary>Local (Ollama) summarizer endpoint + model. Used when Provider is "ollama" or "hybrid".
    /// The model must be pulled (<c>ollama pull {LocalModel}</c>) or local calls fail and those rows
    /// stay un-enriched for the next pass.</summary>
    public string LocalBaseUrl { get; set; } = "http://localhost:11434";
    public string LocalModel { get; set; } = "qwen2.5-coder:7b";

    /// <summary>Local generation can be slow on modest hardware, so it gets its own timeout separate
    /// from Haiku's <see cref="TimeoutSeconds"/>.</summary>
    public int LocalTimeoutSeconds { get; set; } = 120;

    /// <summary>The Haiku instruction lives in an external markdown file so the prompt can be
    /// iterated without recompiling. Path is relative to the app content root.</summary>
    public string PromptTemplatePath { get; set; } = "Enrichment/Prompts/summary.md";

    /// <summary>Anthropic API key. Prefer user-secrets / the ANTHROPIC_API_KEY env var; the SDK
    /// falls back to that env var when this is blank.</summary>
    public string? ApiKey { get; set; }

    public int TimeoutSeconds { get; set; } = 60;
}

/// <summary>Hybrid-search weights (Phase 3). Defined now so the shape is stable; read via
/// IOptionsMonitor so tuning these needs no restart.</summary>
public class SearchOptions
{
    public double VectorWeight { get; set; } = 1.0;
    public double TextWeight { get; set; } = 1.0;

    /// <summary>Reciprocal Rank Fusion constant. Larger = flatter fusion.</summary>
    public int RrfK { get; set; } = 60;

    public double RecencyHalfLifeDays { get; set; } = 30;
    public double ProjectBoost { get; set; } = 1.5;
    public int MaxResults { get; set; } = 8;

    /// <summary>Off by default (a Haiku call per query is a cost/latency hit). Turn on to expand
    /// a query into synonyms/jargon before searching.</summary>
    public bool QueryExpansion { get; set; } = false;
}
