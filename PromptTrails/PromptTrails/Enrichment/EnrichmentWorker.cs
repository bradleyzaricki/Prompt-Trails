using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pgvector;
using PromptTrails.Data;
using PromptTrails.Models;

namespace PromptTrails.Enrichment;

/// <summary>
/// Async enrichment loop. Ingest stays fast (it just writes the raw row); this worker picks up
/// un-enriched rows out of band, summarizes + embeds them exactly once, and stamps
/// <c>enriched_at</c>. The <c>WHERE enriched_at IS NULL</c> gate + the partial index make this
/// idempotent and cheap — restart-safe, and a row is never processed twice.
///
/// Failure policy: a row that throws is logged and left un-enriched so the next pass retries it.
/// (A permanent-failure backoff/dead-letter can be added later; for now transient Ollama/Haiku
/// outages simply recover on the next sweep.)
/// </summary>
public class EnrichmentWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<EnrichmentOptions> options,
    ILogger<EnrichmentWorker> log) : BackgroundService
{
    private readonly EnrichmentOptions _opts = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_opts.Enabled)
        {
            log.LogInformation("Enrichment worker disabled (Enrichment:Enabled=false).");
            return;
        }

        log.LogInformation(
            "Enrichment worker started (summarizer={Provider}, local={LocalModel}, overflow>{Threshold}→{Overflow}, " +
            "embed={Model}/{Dims}d, batch={Batch}, poll={Poll}s).",
            _opts.Summarizer.Provider, _opts.Summarizer.LocalModel, _opts.Summarizer.LocalQueueThreshold,
            _opts.Summarizer.OverflowProvider, _opts.Embedding.Model, _opts.Embedding.Dimensions,
            _opts.BatchSize, _opts.PollSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            int processed;
            try
            {
                processed = await RunBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Enrichment batch failed; backing off.");
                processed = 0;
            }

            // Nothing to do → sleep. Full batch → loop straight through to drain the backlog.
            if (processed < _opts.BatchSize)
                await Task.Delay(TimeSpan.FromSeconds(_opts.PollSeconds), stoppingToken);
        }
    }

    private async Task<int> RunBatchAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var embedder = sp.GetRequiredService<IEmbeddingProvider>();

        var batch = await db.PromptEntries
            .Where(p => p.EnrichedAt == null)
            .OrderBy(p => p.CreatedAt)
            .Take(_opts.BatchSize)
            .Include(p => p.Responses)
            .ToListAsync(ct);

        if (batch.Count == 0) return 0;

        // Resolve summarizer backends lazily and only once each. Done here on the single loop thread
        // (not inside the concurrent tasks) so the cache is never touched from multiple threads.
        var summarizers = new Dictionary<string, ISummarizer>(StringComparer.Ordinal);
        ISummarizer Resolve(string key) =>
            summarizers.TryGetValue(key, out var s)
                ? s
                : summarizers[key] = sp.GetRequiredKeyedService<ISummarizer>(key);

        // The batch is oldest-first, so a row's index IS its position in the backlog. In hybrid mode
        // the first LocalQueueThreshold rows stay local and the rest overflow to the cloud — i.e.
        // overflow only happens once the backlog has stacked past the threshold. Summarize + embed are
        // network-bound and touch no DbContext, so we run the whole batch concurrently: overflow rows
        // drain in parallel with the slow local rows instead of queuing behind them.
        var routeTally = new Dictionary<string, int>(StringComparer.Ordinal);
        var entryModel = new Dictionary<long, string>();   // entry id → model that wrote its embed text
        var work = new List<Task<(PromptEntry Entry, EnrichmentResult? Result)>>(batch.Count);
        for (var i = 0; i < batch.Count; i++)
        {
            var entry = batch[i];
            var key = IsEmptyTurn(entry) ? "empty" : RouteKey(i);
            routeTally[key] = routeTally.GetValueOrDefault(key) + 1;
            var summarizer = key == "empty" ? null : Resolve(key);
            entryModel[entry.Id] = summarizer?.ModelName ?? "(empty-turn skip)";
            work.Add(ComputeSafeAsync(entry, summarizer, embedder, ct));
        }

        log.LogInformation("Enrichment pass: {Count} rows routed {Split}.",
            batch.Count, string.Join(", ", routeTally.Select(kv => $"{kv.Key}={kv.Value}")));

        var results = await Task.WhenAll(work);

        // Apply results sequentially on this thread — the DbContext is not thread-safe.
        foreach (var (entry, result) in results)
        {
            if (result is null) continue;  // failed → leave enriched_at null so the next pass retries
            entry.Summary = JsonSerializer.Serialize(result.Summary);
            // Store exactly what was embedded so text and vector never disagree. A blank field leaves
            // BOTH its text and vector null — the solution index only holds genuine solutions, never
            // problem-shaped filler.
            entry.ProblemEmbeddingText = result.ProblemText;
            entry.ProblemEmbedding = result.ProblemEmbedding is null ? null : new Vector(result.ProblemEmbedding);
            entry.SolutionEmbeddingText = result.SolutionText;
            entry.SolutionEmbedding = result.SolutionEmbedding is null ? null : new Vector(result.SolutionEmbedding);
            entry.TermsText = result.Summary.Terms.Count > 0
                ? string.Join(" ", result.Summary.Terms)
                : null;
            // Developer-facing provenance: stamp the embedding model only when a vector was actually
            // produced, so the column and the vectors never disagree (a row with no embedding stays null).
            var embedded = result.ProblemEmbedding is not null || result.SolutionEmbedding is not null;
            entry.EmbeddingModel = embedded ? embedder.ModelName : null;
            // Independent problem/solution usefulness from the summarizer (both 0.0 for empty turns,
            // which default the scores to 0.0).
            entry.ProblemUseful = result.Summary.ProblemUseful;
            entry.SolutionUseful = result.Summary.SolutionUseful;
            entry.EnrichedAt = DateTimeOffset.UtcNow;

            log.LogInformation(
                "Enriched prompt {Id}: embed text via {SummaryModel}, vectors via {EmbedModel} " +
                "(problem={HasProblem}, solution={HasSolution}).",
                entry.Id, entryModel[entry.Id], _opts.Embedding.Model,
                result.ProblemEmbedding is not null, result.SolutionEmbedding is not null);
        }

        await db.SaveChangesAsync(ct);
        return batch.Count;
    }

    /// <summary>Which summarizer key handles the row at <paramref name="backlogIndex"/> (0 = oldest).
    /// Hybrid keeps the head local and sheds the tail to the overflow provider.</summary>
    private string RouteKey(int backlogIndex)
    {
        var s = _opts.Summarizer;
        return s.Provider.ToLowerInvariant() switch
        {
            "haiku" => "haiku",
            "ollama" => "ollama",
            _ => backlogIndex < s.LocalQueueThreshold ? "ollama" : s.OverflowProvider.ToLowerInvariant(),
        };
    }

    // We decide whether to summarize on what the turn PRODUCED, never on prompt length: a short "yes"
    // that confirmed a big change is summarized from its diff like any other turn. The only skip is a
    // genuinely empty turn — no diff, no tool calls, no substantive response — where there is simply
    // nothing to normalize.
    private bool IsEmptyTurn(PromptEntry e) =>
        e.FilesChanged == 0
        && string.IsNullOrWhiteSpace(e.Diff)
        && e.Responses.Count == 0
        && (e.AssistantResponse?.Trim().Length ?? 0) < _opts.MinResponseChars;

    private async Task<(PromptEntry, EnrichmentResult?)> ComputeSafeAsync(
        PromptEntry entry, ISummarizer? summarizer, IEmbeddingProvider embedder, CancellationToken ct)
    {
        try
        {
            return (entry, await ComputeAsync(entry, summarizer, embedder, ct));
        }
        catch (Exception ex)
        {
            var via = summarizer?.GetType().Name ?? "trivial";
            log.LogWarning(ex, "Failed to enrich prompt {Id} (via {Via}); will retry next pass.", entry.Id, via);
            return (entry, null);
        }
    }

    /// <summary>Pure compute — summarize + embed, no entity mutation — so it's safe to run concurrently.
    /// A null <paramref name="summarizer"/> means an empty turn: skip the model, embed the raw text.</summary>
    private async Task<EnrichmentResult> ComputeAsync(
        PromptEntry entry, ISummarizer? summarizer, IEmbeddingProvider embedder, CancellationToken ct)
    {
        PromptSummary summary;
        if (summarizer is null)
        {
            // Empty turn: the prompt is still the user's intent (a usable problem vector), but there
            // is no solution to record — leave it blank so the solution vector ends up null.
            summary = new PromptSummary
            {
                Problem = entry.PromptText?.Trim() ?? "",
                Outcome = "Empty turn — no diff, tools, or substantive response.",
                Solution = "",
            };
        }
        else
        {
            var tools = entry.Responses
                .Select(r => r.ToolName)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct()
                .ToList();

            summary = await summarizer.SummarizeAsync(
                new SummarizerInput(entry.PromptText ?? "", entry.AssistantResponse, entry.Diff, tools), ct);
        }

        // Embed only genuine text — a blank problem/solution stays null (no fallback to the raw
        // prompt), so the vector and its text column always agree and the solution index never
        // fills with problem-shaped filler. Skipping the blank case also saves an embed call.
        var problemText = string.IsNullOrWhiteSpace(summary.Problem) ? null : summary.Problem.Trim();
        var solutionText = string.IsNullOrWhiteSpace(summary.Solution) ? null : summary.Solution.Trim();

        var problemVector = problemText is null ? null : await embedder.EmbedAsync(problemText, ct);
        var solutionVector = solutionText is null ? null : await embedder.EmbedAsync(solutionText, ct);
        return new EnrichmentResult(summary, problemText, problemVector, solutionText, solutionVector);
    }

    private sealed record EnrichmentResult(
        PromptSummary Summary,
        string? ProblemText, float[]? ProblemEmbedding,
        string? SolutionText, float[]? SolutionEmbedding);
}
