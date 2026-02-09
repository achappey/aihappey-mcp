using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.OpenAI.Research;

public static class OpenAIFinancialResearch
{
    // === Prompt names (vul je later concreet) ===
    private const string PlannerPrompt = "financial-planner";
    private const string WebSearchPrompt = "financial-web-search";
    private const string FundamentalsPrompt = "fundamentals-analyzer";
    private const string RiskPrompt = "risk-analyzer";
    private const string WriterPrompt = "financial-writer";
    private const string VerifierPrompt = "financial-verifier";

    // === Model defaults (pas aan naar wens) ===
    private const string PlannerModel = "gpt-5.1";
    private const string SearchModel = "gpt-5-mini";
    private const string AnalystsModel = "gpt-5-mini";
    private const string WriterModel = "gpt-5.1";
    private const string VerifierModel = "gpt-5-mini";

    [Description("Perform financial research on a topic. Before you use this tool, always ask the user first for more details so you can craft a detailed research topic for maximum accuracy.")]
    [McpServerTool(Title = "Perform financial research", ReadOnly = true, OpenWorld = false)]
    public static async Task<CallToolResult> FinancialResearch_Run(
       [Description("Research subject or question (e.g. 'Is ASML undervalued after Q3 2025?')")]
        string topic,
        IServiceProvider services,
        RequestContext<CallToolRequestParams> ctx,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        var sampling = services.GetRequiredService<SamplingService>();

        if (ctx.Server.ClientCapabilities?.Sampling == null)
            return "Sampling is required for this tool".ToErrorCallToolResponse();

        var progressToken = ctx.Params?.ProgressToken;
        var step = 1;

        // 1) Planning
        await Progress(ctx.Server, progressToken, step++, null, $"Planning searches for: {topic}", cancellationToken);

        var planArgs = new Dictionary<string, JsonElement>
        {
            ["query"] = JsonSerializer.SerializeToElement(topic)
        };

        var plan = await sampling.GetPromptSample<WebSearchPlan>(
            services, ctx.Server, PlannerPrompt, planArgs, PlannerModel,
            maxTokens: 4096 * 4,
            metadata: new Dictionary<string, object> {
                { "openai", new { reasoning = new { effort = "medium" } } }
            },
            cancellationToken: cancellationToken
        );

        var searches = plan?.Searches ?? [];
        var totalSteps = (searches.Count) + 4; // searches + analysts(2) + write + verify

        await Progress(ctx.Server, progressToken, step++, totalSteps,
            $"Expanded to {searches.Count} queries:\n{string.Join("\n", searches.Select(q => "- " + q.Query))}",
            cancellationToken);

        // 2) Searching (parallel)
        await Progress(ctx.Server, progressToken, step, totalSteps, $"Starting web searches...", cancellationToken);

        var searchTasks = searches.Select((s, i) =>
            GetWebResearch(ctx.Server, sampling, progressToken, step + i, totalSteps, services, s.Query, s.Reason, cancellationToken)
        );

        var searchResults = (await Task.WhenAll(searchTasks)).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        step += searches.Count;

        // 3) Analysts (pre-run calls; single call per analyst met geaggregeerde context)
        await Progress(ctx.Server, progressToken, step++, totalSteps, "Analyzing fundamentals...", cancellationToken);
        var fundamentalsText = await GetFundamentalsSummary(ctx.Server, sampling, services, topic, searchResults!, cancellationToken);

        await Progress(ctx.Server, progressToken, step++, totalSteps, "Analyzing risks...", cancellationToken);
        var riskText = await GetRiskSummary(ctx.Server, sampling, services, topic, searchResults!, cancellationToken);

        // 4) Writing
        await Progress(ctx.Server, progressToken, step++, totalSteps, "Writing report...", cancellationToken);

        var writerArgs = new Dictionary<string, JsonElement>
        {
            ["query"] = JsonSerializer.SerializeToElement(topic),
            ["searchResults"] = JsonSerializer.SerializeToElement(string.Join("\n\n", searchResults)),
            ["fundamentalsSummary"] = JsonSerializer.SerializeToElement(fundamentalsText ?? string.Empty),
            ["riskSummary"] = JsonSerializer.SerializeToElement(riskText ?? string.Empty)
        };

        var writerSample = await sampling.GetPromptSample(
            services, ctx.Server, WriterPrompt, writerArgs, WriterModel,
            maxTokens: 4096 * 4,
            metadata: new Dictionary<string, object> {
                { "openai", new { reasoning = new { effort = "low" } } }
            },
            cancellationToken: cancellationToken
        );

        // 5) Verification
        await Progress(ctx.Server, progressToken, step++, totalSteps, "Verifying report...", cancellationToken);

        var verifyArgs = new Dictionary<string, JsonElement>
        {
            ["report_markdown"] = JsonSerializer.SerializeToElement(writerSample.ToText()),
            ["query"] = JsonSerializer.SerializeToElement(topic)
        };

        var verification = await sampling.GetPromptSample(
            services, ctx.Server, VerifierPrompt, verifyArgs, VerifierModel,
            metadata: new Dictionary<string, object> {
                { "openai", new { reasoning = new { effort = "medium" } } }
            },
            cancellationToken: cancellationToken
        );

        // Output
        var finalText = BuildFinalText(writerSample?.ToText(),
            verification?.ToText(), fundamentalsText, riskText, searchResults!);

        return (string.IsNullOrWhiteSpace(finalText)
            ? string.Join("\n\n", searchResults)
            : finalText).ToTextCallToolResponse();
    }

    // ----------------- Helpers -----------------

    private static async Task<string?> GetWebResearch(
        McpServer server,
        SamplingService sampling,
        ProgressToken? progressToken,
        int step,
        int? total,
        IServiceProvider services,
        string searchTerm,
        string searchReason,
        CancellationToken cancellationToken)
    {
        await Progress(server, progressToken, step, total, $"Searching: {searchTerm}\nReason: {searchReason}", cancellationToken);

        var args = new Dictionary<string, JsonElement>
        {
            ["searchTerm"] = JsonSerializer.SerializeToElement(searchTerm),
            ["searchReason"] = JsonSerializer.SerializeToElement(searchReason)
        };

        var sample = await sampling.GetPromptSample(
            services, server, WebSearchPrompt, args, SearchModel,
            metadata: new Dictionary<string, object> {
                { "openai", new {
                    reasoning = new { effort = "low" },
                    web_search = new { search_context_size = "medium" }
                } }
            },
            cancellationToken: cancellationToken
        );

        return sample.ToText();
    }

    private static async Task<string?> GetFundamentalsSummary(
        McpServer server,
        SamplingService sampling,
        IServiceProvider services,
        string topic,
        IEnumerable<string> searchResults,
        CancellationToken cancellationToken)
    {
        var args = new Dictionary<string, JsonElement>
        {
            ["query"] = JsonSerializer.SerializeToElement(topic),
            ["evidence"] = JsonSerializer.SerializeToElement(string.Join("\n\n", searchResults))
        };

        var sample = await sampling.GetPromptSample(
            services, server, FundamentalsPrompt, args, AnalystsModel,
            metadata: new Dictionary<string, object> {
                { "openai", new { reasoning = new { effort = "low" } } }
            },
            cancellationToken: cancellationToken
        );

        return sample.ToText();
    }

    private static async Task<string?> GetRiskSummary(
        McpServer server,
        SamplingService sampling,
        IServiceProvider services,
        string topic,
        IEnumerable<string> searchResults,
        CancellationToken cancellationToken)
    {
        var args = new Dictionary<string, JsonElement>
        {
            ["query"] = JsonSerializer.SerializeToElement(topic),
            ["evidence"] = JsonSerializer.SerializeToElement(string.Join("\n\n", searchResults))
        };

        var sample = await sampling.GetPromptSample(
            services, server, RiskPrompt, args, AnalystsModel,
            metadata: new Dictionary<string, object> {
                { "openai", new { reasoning = new { effort = "low" } } }
            },
            cancellationToken: cancellationToken
        );

        return sample.ToText();
    }

    private static async Task Progress(
        McpServer server,
        ProgressToken? token,
        float? progress,
        int? total,
        string message,
        CancellationToken ct)
    {
        if (token is null) return;
        await server.SendNotificationAsync(
            "notifications/progress",
            new ProgressNotificationParams
            {
                ProgressToken = token.Value,
                Progress = new ProgressNotificationValue
                {
                    Progress = progress ?? 0,
                    Total = total,
                    Message = message
                }
            },
            cancellationToken: ct
        );
    }

    private static string BuildFinalText(
        string? report,
        string? verification,
        string? fundamentals,
        string? risks,
        IReadOnlyCollection<string> searchResults)
    {
        return
$@"Report

===== REPORT =====

{report}

{verification}

===== FUNDAMENTAL =====
{fundamentals}

===== RISKS =====
{risks}

===== SOURCED SEARCH NOTES =====

{string.Join("\n\n", searchResults)}";
    }

    // ----------------- DTOs -----------------

    public class WebSearchItem
    {
        [JsonPropertyName("reason")]
        public string Reason { get; set; } = null!;

        [JsonPropertyName("query")]
        public string Query { get; set; } = null!;
    }

    public class WebSearchPlan
    {
        [JsonPropertyName("queries")]
        public List<WebSearchItem> Searches { get; set; } = [];
    }

}

