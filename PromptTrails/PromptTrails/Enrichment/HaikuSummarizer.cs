using System.Text;
using Anthropic;
using Anthropic.Helpers;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Options;

namespace PromptTrails.Enrichment;

/// <summary>
/// Summarizes one prompt turn with Claude Haiku (cheapest model, $1/$5 per MTok) via the official
/// Anthropic C# SDK. Uses structured output so the response is guaranteed to match
/// <see cref="PromptSummary"/> — no hand-parsing of model text. The instruction lives in an
/// external markdown file (<c>Enrichment:Summarizer:PromptTemplatePath</c>) so it can be tuned
/// without a recompile. Inputs are truncated per <see cref="EnrichmentOptions"/> to cap token cost.
///
/// The API key comes from the ANTHROPIC_API_KEY environment variable (seeded from config in
/// Program.cs when <c>Enrichment:Summarizer:ApiKey</c> is set) — the SDK reads it automatically.
/// </summary>
public class HaikuSummarizer : ISummarizer
{
    private readonly AnthropicClient _client;
    private readonly EnrichmentOptions _opts;
    private readonly string _systemPrompt;
    private readonly ILogger<HaikuSummarizer> _log;

    public HaikuSummarizer(
        IOptions<EnrichmentOptions> opts,
        IHostEnvironment env,
        ILogger<HaikuSummarizer> log)
    {
        _opts = opts.Value;
        _log = log;
        _client = new AnthropicClient();   // reads ANTHROPIC_API_KEY

        var path = Path.Combine(env.ContentRootPath, _opts.Summarizer.PromptTemplatePath);
        _systemPrompt = File.Exists(path)
            ? File.ReadAllText(path)
            : throw new FileNotFoundException(
                $"Haiku prompt template not found at '{path}'. Set Enrichment:Summarizer:PromptTemplatePath.");
    }

    public string ModelName => _opts.Summarizer.Model;

    public async Task<PromptSummary> SummarizeAsync(SummarizerInput input, CancellationToken ct = default)
    {
        var userContent = SummarizerContent.BuildUserContent(input, _opts);

        var parameters = new MessageCreateParams
        {
            Model = _opts.Summarizer.Model,
            MaxTokens = _opts.Summarizer.MaxTokens,
            System = _systemPrompt,
            Messages =
            [
                new MessageParam { Role = Role.User, Content = userContent },
            ],
            // Constrain the response to our schema. Haiku 4.5 supports structured outputs.
            OutputConfig = new OutputConfig
            {
                Format = StructuredOutput.CreateJsonFormat<PromptSummary>(),
            },
        };

        var message = await _client.Messages.Create(parameters, ct);
        var json = ExtractText(message);

        return StructuredOutput.Parse<PromptSummary>(json);
    }

    /// <summary>Concatenate every text block in the response (structured output arrives as JSON text).</summary>
    private static string ExtractText(Message message)
    {
        var sb = new StringBuilder();
        foreach (var block in message.Content)
        {
            if (block.TryPickText(out var text))
                sb.Append(text.Text);
        }
        return sb.ToString();
    }
}
