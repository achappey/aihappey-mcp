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

public static class OpenAIMicrosoftResearch
{
    [Description("Perform Microsoft research across SharePoint, Teams, and Outlook. Before using this tool, ask for enough detail to craft a precise research topic.")]
    [McpServerTool(Title = "Perform Microsoft research", ReadOnly = true)]
    public static async Task<CallToolResult> OpenAIResearch_PerformMicrosoftResearch(
        [Description("Topic for the research")] string researchTopic,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional OpenAI model override.")] string? model = OpenAIResponsesClient.DefaultModel,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(researchTopic);

        var prompts = serviceProvider.GetRequiredService<PromptService>();
        var responses = serviceProvider.GetRequiredService<OpenAIResponsesClient>();
        var planningArguments = new Dictionary<string, JsonElement> { ["query"] = JsonSerializer.SerializeToElement(researchTopic) };
        var planText = await responses.CreatePromptTextResponseAsync(prompts, serviceProvider, requestContext.Server,
            "microsoft-search-planner", planningArguments, model, "medium", cancellationToken: cancellationToken);
        var plan = JsonSerializer.Deserialize<WebSearchPlan>(planText.CleanJson()) ?? new WebSearchPlan();

        var counter = 1;
        var total = plan.Searches.Count + 2;
        await Progress(requestContext, counter, total,
            $"Expanded to {plan.Searches.Count} queries:\n{string.Join("\n", plan.Searches.Select(search => search.Query))}", cancellationToken);

        var oboToken = await serviceProvider.GetOboGraphToken(requestContext.Server);
        var searchTasks = plan.Searches.Select(search => GetMicrosoftResearch(
            responses, prompts, serviceProvider, requestContext, oboToken, counter++, total, search.Query, search.Reason, model, cancellationToken));
        var searchResults = await Task.WhenAll(searchTasks);

        await Progress(requestContext, counter++, total, "Writing report", cancellationToken);
        var reportArguments = new Dictionary<string, JsonElement>
        {
            ["query"] = JsonSerializer.SerializeToElement(researchTopic),
            ["searchResults"] = JsonSerializer.SerializeToElement(string.Join("\n\n", searchResults))
        };
        var report = await responses.CreatePromptTextResponseAsync(prompts, serviceProvider, requestContext.Server,
            "write-report", reportArguments, model, "medium", cancellationToken: cancellationToken);

        return (string.IsNullOrWhiteSpace(report) ? string.Join("\n\n", searchResults) : report).ToTextCallToolResponse();
    }

    private static async Task<string> GetMicrosoftResearch(
        OpenAIResponsesClient responses,
        PromptService prompts,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string oboToken,
        int counter,
        int total,
        string topic,
        string reason,
        string? model,
        CancellationToken cancellationToken)
    {
        await Progress(requestContext, counter, total, $"Searching: {topic}\nReason: {reason}", cancellationToken);
        var arguments = new Dictionary<string, JsonElement>
        {
            ["searchTerm"] = JsonSerializer.SerializeToElement(topic),
            ["searchReason"] = JsonSerializer.SerializeToElement(reason)
        };

        return await responses.CreatePromptTextResponseAsync(prompts, serviceProvider, requestContext.Server,
            "microsoft-research", arguments, model, "low", CreateMicrosoftTools(oboToken), cancellationToken);
    }

    private static JsonArray CreateMicrosoftTools(string authorization) =>
    [
        CreateMicrosoftTool("microsoft_teams", "connector_microsoftteams", authorization),
        CreateMicrosoftTool("outlook_email", "connector_outlookemail", authorization),
        CreateMicrosoftTool("sharepoint", "connector_sharepoint", authorization)
    ];

    private static JsonObject CreateMicrosoftTool(string serverLabel, string connectorId, string authorization) => new()
    {
        ["type"] = "mcp",
        ["server_label"] = serverLabel,
        ["connector_id"] = connectorId,
        ["authorization"] = authorization,
        ["require_approval"] = "never"
    };

    private static async Task Progress(RequestContext<CallToolRequestParams> context, int progress, int total, string message, CancellationToken cancellationToken)
    {
        if (context.Params?.ProgressToken is not { } token)
            return;

        await context.Server.SendNotificationAsync("notifications/progress", new ProgressNotificationParams
        {
            ProgressToken = token,
            Progress = new ProgressNotificationValue { Progress = progress, Total = total, Message = message }
        }, cancellationToken: cancellationToken);
    }

    public sealed class WebSearchItem
    {
        [JsonPropertyName("reason")]
        public string Reason { get; set; } = null!;

        [JsonPropertyName("query")]
        public string Query { get; set; } = null!;
    }

    public sealed class WebSearchPlan
    {
        [JsonPropertyName("queries")]
        public List<WebSearchItem> Searches { get; set; } = [];
    }
}
