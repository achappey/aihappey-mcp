using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Core.Services;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.OpenAI.Responses;

internal static class OpenAIResponsesPromptExtensions
{
    public static async Task<string> CreatePromptTextResponseAsync(
        this OpenAIResponsesClient client,
        PromptService promptService,
        IServiceProvider serviceProvider,
        McpServer server,
        string promptName,
        IReadOnlyDictionary<string, JsonElement>? arguments,
        string? model,
        string reasoningEffort,
        JsonArray? tools = null,
        CancellationToken cancellationToken = default)
    {
        var prompt = await promptService.GetServerPrompt(
            serviceProvider,
            server,
            promptName,
            arguments,
            cancellationToken: cancellationToken);

        var request = new JsonObject
        {
            ["model"] = OpenAIResponsesClient.ResolveModel(model),
            ["input"] = string.Join("\n\n", prompt.Messages.Select(GetMessageText)),
            ["reasoning"] = new JsonObject { ["effort"] = reasoningEffort }
        };

        if (tools is { Count: > 0 })
            request["tools"] = tools;

        var response = await client.CreateResponseAsync(request, cancellationToken);
        return OpenAIResponsesClient.GetOutputText(response)
            ?? throw new InvalidOperationException("The OpenAI Responses API returned no text output.");
    }

    private static string GetMessageText(PromptMessage message) => message.Content switch
    {
        TextContentBlock text => text.Text,
        _ => message.Content.ToString() ?? string.Empty
    };
}
