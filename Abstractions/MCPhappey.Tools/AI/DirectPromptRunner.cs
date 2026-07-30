using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Common.Models;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Anthropic.Messages;
using MCPhappey.Tools.Google.Interactions;
using MCPhappey.Tools.OpenAI.Responses;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.AI;

internal static class DirectPromptRunner
{
    internal const string OpenAIModel = OpenAIResponsesClient.DefaultModel;
    internal const string GoogleModel = GoogleInteractionsClient.DefaultModel;
    internal const string AnthropicModel = AnthropicMessagesClient.DefaultModel;

    internal static readonly (string Provider, string Model)[] DocumentModels =
    [
        ("openai", OpenAIModel),
        ("google", GoogleModel),
        ("anthropic", AnthropicModel)
    ];

    public static async Task<ProviderMessageResult> RunAsync(
        IServiceProvider serviceProvider,
        McpServer server,
        string provider,
        string promptName,
        IReadOnlyDictionary<string, JsonElement>? arguments,
        int maxTokens,
        string reasoningEffort,
        int? thinkingBudget,
        CancellationToken cancellationToken)
    {
        var promptService = serviceProvider.GetRequiredService<PromptService>();
        var prompt = await promptService.GetServerPrompt(
            serviceProvider, server, promptName, arguments, cancellationToken: cancellationToken);
        var input = string.Join("\n\n", prompt.Messages.Select(GetMessageText));
        var started = DateTime.UtcNow;

        var result = provider switch
        {
            "openai" => await RunOpenAIAsync(serviceProvider, input, maxTokens, reasoningEffort, cancellationToken),
            "google" => await RunGoogleAsync(serviceProvider, input, maxTokens, cancellationToken),
            "anthropic" => await RunAnthropicAsync(serviceProvider, input, maxTokens, thinkingBudget, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unsupported direct AI provider.")
        };

        return new ProviderMessageResult
        {
            Provider = provider,
            Model = result.Model,
            Content = result.Text,
            Duration = (DateTime.UtcNow - started).ToString(),
            Metadata = result.Metadata
        };
    }

    public static async Task<T> RunOpenAITypedAsync<T>(
        IServiceProvider serviceProvider,
        McpServer server,
        string promptName,
        IReadOnlyDictionary<string, JsonElement>? arguments,
        string? model,
        string reasoningEffort,
        int maxTokens,
        CancellationToken cancellationToken)
    {
        var promptService = serviceProvider.GetRequiredService<PromptService>();
        var prompt = await promptService.GetServerPrompt(
            serviceProvider, server, promptName, arguments, cancellationToken: cancellationToken);
        var input = string.Join("\n\n", prompt.Messages.Select(GetMessageText));
        var client = serviceProvider.GetRequiredService<OpenAIResponsesClient>();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var request = new JsonObject
            {
                ["model"] = OpenAIResponsesClient.ResolveModel(model),
                ["input"] = attempt == 0
                    ? input
                    : input + "\n\nReturn only valid JSON matching the requested shape. Do not use Markdown fences.",
                ["max_output_tokens"] = maxTokens,
                ["reasoning"] = new JsonObject { ["effort"] = reasoningEffort },
                ["text"] = new JsonObject
                {
                    ["format"] = new JsonObject { ["type"] = "json_object" }
                }
            };

            var response = await client.CreateResponseAsync(request, cancellationToken);
            var text = OpenAIResponsesClient.GetOutputText(response)
                ?? throw new InvalidOperationException("The OpenAI Responses API returned no text output.");
            try
            {
                return JsonSerializer.Deserialize<T>(StripCodeFence(text), JsonOptions)
                    ?? throw new JsonException("The JSON response was null.");
            }
            catch (JsonException) when (attempt == 0) { }
        }

        throw new JsonException("OpenAI returned invalid JSON after one retry.");
    }

    private static async Task<(string Model, string Text, object Metadata)> RunOpenAIAsync(
        IServiceProvider serviceProvider, string input, int maxTokens, string reasoningEffort, CancellationToken cancellationToken)
    {
        var client = serviceProvider.GetRequiredService<OpenAIResponsesClient>();
        var response = await client.CreateResponseAsync(new JsonObject
        {
            ["model"] = OpenAIModel,
            ["input"] = input,
            ["max_output_tokens"] = maxTokens,
            ["reasoning"] = new JsonObject { ["effort"] = reasoningEffort }
        }, cancellationToken);
        return (OpenAIModel, OpenAIResponsesClient.GetOutputText(response)
            ?? throw new InvalidOperationException("The OpenAI Responses API returned no text output."), response["usage"]?.DeepClone() ?? new JsonObject());
    }

    private static async Task<(string Model, string Text, object Metadata)> RunGoogleAsync(
        IServiceProvider serviceProvider, string input, int maxTokens, CancellationToken cancellationToken)
    {
        var client = serviceProvider.GetRequiredService<GoogleInteractionsClient>();
        var response = await client.CreateInteractionAsync(new GoogleInteractionRequest
        {
            Model = GoogleModel,
            Input = GoogleInteractionInput.Text(input),
            GenerationConfig = new JsonObject { ["max_output_tokens"] = maxTokens }
        }, cancellationToken);
        return (GoogleModel, GoogleInteractionResponse.GetText(response)
            ?? throw new InvalidOperationException("Google Interactions API returned no text output."), response["usage"]?.DeepClone() ?? new JsonObject());
    }

    private static async Task<(string Model, string Text, object Metadata)> RunAnthropicAsync(
        IServiceProvider serviceProvider, string input, int maxTokens, int? thinkingBudget, CancellationToken cancellationToken)
    {
        var request = new JsonObject
        {
            ["model"] = AnthropicModel,
            ["max_tokens"] = maxTokens,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "user", ["content"] = input }
            }
        };
        if (thinkingBudget is > 0)
            request["thinking"] = new JsonObject { ["type"] = "enabled", ["budget_tokens"] = thinkingBudget.Value };

        var client = serviceProvider.GetRequiredService<AnthropicMessagesClient>();
        var response = await client.CreateMessageAsync(request, cancellationToken: cancellationToken);
        return (response["model"]?.GetValue<string>() ?? AnthropicModel, AnthropicMessagesClient.GetText(response)
            ?? throw new InvalidOperationException("Anthropic Messages API returned no text output."), response["usage"]?.DeepClone() ?? new JsonObject());
    }

    private static string GetMessageText(PromptMessage message) => message.Content switch
    {
        TextContentBlock text => text.Text,
        _ => message.Content.ToString() ?? string.Empty
    };

    private static string StripCodeFence(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal)) return trimmed;
        var firstNewline = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewline >= 0 && lastFence > firstNewline
            ? trimmed[(firstNewline + 1)..lastFence].Trim()
            : trimmed;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
}
