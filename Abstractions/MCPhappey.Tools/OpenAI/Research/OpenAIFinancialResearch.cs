using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.OpenAI.Responses;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.OpenAI.Research;

public static class OpenAIFinancialResearch
{
    private const string PlannerPrompt = "financial-planner";
    private const string WebSearchPrompt = "financial-web-search";
    private const string FundamentalsPrompt = "fundamentals-analyzer";
    private const string RiskPrompt = "risk-analyzer";
    private const string WriterPrompt = "financial-writer";
    private const string VerifierPrompt = "financial-verifier";

    [Description("Perform financial research on a topic. Before using this tool, ask for enough detail to craft a precise research topic.")]
    [McpServerTool(Title = "Perform financial research", ReadOnly = true, OpenWorld = false)]
    public static async Task<CallToolResult> FinancialResearch_Run(
        [Description("Research subject or question (for example: 'Is ASML undervalued after Q3 2025?')")] string topic,
        IServiceProvider services,
        RequestContext<CallToolRequestParams> context,
        [Description("Optional OpenAI model override.")] string? model = OpenAIResponsesClient.DefaultModel,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        var prompts = services.GetRequiredService<PromptService>();
        var responses = services.GetRequiredService<OpenAIResponsesClient>();
        var progressToken = context.Params?.ProgressToken;
        var step = 1;
        await Progress(context.Server, progressToken, step++, null, $"Planning searches for: {topic}", cancellationToken);

        var plan = await CreateStructuredResponse<WebSearchPlan>(responses, prompts, services, context.Server, PlannerPrompt,
            new Dictionary<string, JsonElement> { ["query"] = JsonSerializer.SerializeToElement(topic) }, model, "medium", cancellationToken);
        var searches = plan.Searches;
        var totalSteps = searches.Count + 4;
        await Progress(context.Server, progressToken, step++, totalSteps,
            $"Expanded to {searches.Count} queries:\n{string.Join("\n", searches.Select(search => "- " + search.Query))}", cancellationToken);

        var searchTasks = searches.Select((search, index) => GetWebResearch(
            responses, prompts, services, context.Server, progressToken, step + index, totalSteps, search.Query, search.Reason, model, cancellationToken));
        var searchResults = (await Task.WhenAll(searchTasks)).Where(result => !string.IsNullOrWhiteSpace(result)).ToList();
        step += searches.Count;

        await Progress(context.Server, progressToken, step++, totalSteps, "Analyzing fundamentals...", cancellationToken);
        var fundamentals = await CreateResponse(responses, prompts, services, context.Server, FundamentalsPrompt, topic, searchResults, model, "low", cancellationToken);

        await Progress(context.Server, progressToken, step++, totalSteps, "Analyzing risks...", cancellationToken);
        var risks = await CreateResponse(responses, prompts, services, context.Server, RiskPrompt, topic, searchResults, model, "low", cancellationToken);

        await Progress(context.Server, progressToken, step++, totalSteps, "Writing report...", cancellationToken);
        var report = await responses.CreatePromptTextResponseAsync(prompts, services, context.Server, WriterPrompt,
            new Dictionary<string, JsonElement>
            {
                ["query"] = JsonSerializer.SerializeToElement(topic),
                ["searchResults"] = JsonSerializer.SerializeToElement(string.Join("\n\n", searchResults)),
                ["fundamentalsSummary"] = JsonSerializer.SerializeToElement(fundamentals),
                ["riskSummary"] = JsonSerializer.SerializeToElement(risks)
            }, model, "low", cancellationToken: cancellationToken);

        await Progress(context.Server, progressToken, step++, totalSteps, "Verifying report...", cancellationToken);
        var verification = await responses.CreatePromptTextResponseAsync(prompts, services, context.Server, VerifierPrompt,
            new Dictionary<string, JsonElement>
            {
                ["report_markdown"] = JsonSerializer.SerializeToElement(report),
                ["query"] = JsonSerializer.SerializeToElement(topic)
            }, model, "medium", cancellationToken: cancellationToken);

        var result = BuildFinalText(report, verification, fundamentals, risks, searchResults);
        return (string.IsNullOrWhiteSpace(result) ? string.Join("\n\n", searchResults) : result).ToTextCallToolResponse();
    }

    private static async Task<WebSearchPlan> CreateStructuredResponse<T>(
        OpenAIResponsesClient responses,
        PromptService prompts,
        IServiceProvider services,
        McpServer server,
        string promptName,
        IReadOnlyDictionary<string, JsonElement> arguments,
        string? model,
        string effort,
        CancellationToken cancellationToken) where T : WebSearchPlan, new()
    {
        var text = await responses.CreatePromptTextResponseAsync(prompts, services, server, promptName, arguments, model, effort,
            cancellationToken: cancellationToken);
        return JsonSerializer.Deserialize<T>(text.CleanJson()) ?? new T();
    }

    private static async Task<string> GetWebResearch(
        OpenAIResponsesClient responses,
        PromptService prompts,
        IServiceProvider services,
        McpServer server,
        ProgressToken? progressToken,
        int step,
        int total,
        string searchTerm,
        string searchReason,
        string? model,
        CancellationToken cancellationToken)
    {
        await Progress(server, progressToken, step, total, $"Searching: {searchTerm}\nReason: {searchReason}", cancellationToken);
        return await responses.CreatePromptTextResponseAsync(prompts, services, server, WebSearchPrompt,
            new Dictionary<string, JsonElement>
            {
                ["searchTerm"] = JsonSerializer.SerializeToElement(searchTerm),
                ["searchReason"] = JsonSerializer.SerializeToElement(searchReason)
            }, model, "low", 
            new JsonArray { new JsonObject { ["type"] = "web_search" } }, cancellationToken);
    }

    private static async Task<string> CreateResponse(
        OpenAIResponsesClient responses,
        PromptService prompts,
        IServiceProvider services,
        McpServer server,
        string promptName,
        string topic,
        IEnumerable<string> evidence,
        string? model,
        string effort,
        CancellationToken cancellationToken) =>
        await responses.CreatePromptTextResponseAsync(prompts, services, server, promptName,
            new Dictionary<string, JsonElement>
            {
                ["query"] = JsonSerializer.SerializeToElement(topic),
                ["evidence"] = JsonSerializer.SerializeToElement(string.Join("\n\n", evidence))
            }, model, effort, cancellationToken: cancellationToken);

    private static async Task Progress(McpServer server, ProgressToken? token, float? progress, int? total, string message, CancellationToken cancellationToken)
    {
        if (token is null)
            return;

        await server.SendNotificationAsync("notifications/progress", new ProgressNotificationParams
        {
            ProgressToken = token.Value,
            Progress = new ProgressNotificationValue { Progress = progress ?? 0, Total = total, Message = message }
        }, cancellationToken: cancellationToken);
    }

    private static string BuildFinalText(string report, string verification, string fundamentals, string risks, IReadOnlyCollection<string> searchResults) =>
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

    public sealed class WebSearchItem
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
