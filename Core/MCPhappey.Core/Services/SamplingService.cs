using System.Text.Json;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Core.Services;

public class SamplingService(PromptService promptService)
{
    public async Task<CreateMessageResult> GetPromptSample(IServiceProvider serviceProvider,
       McpServer mcpServer, string name,
       IReadOnlyDictionary<string, JsonElement>? arguments = null,
       string? modelHint = null,
       float? temperature = null,
       string? systemPrompt = null,
       ContextInclusion includeContext = ContextInclusion.None,
       int? maxTokens = 4096,
       Dictionary<string, object>? metadata = null,
       IEnumerable<SamplingMessage>? messages = null,
       CancellationToken cancellationToken = default) =>
       await GetPromptSample(serviceProvider, mcpServer, name, arguments, modelHint != null ? [modelHint] : null,
           temperature, systemPrompt, includeContext, maxTokens, metadata, messages, cancellationToken);

    public async Task<CreateMessageResult> GetPromptSample(IServiceProvider serviceProvider,
        McpServer mcpServer, string name,
        IReadOnlyDictionary<string, JsonElement>? arguments = null,
        IEnumerable<string>? modelHints = null,
        float? temperature = null,
        string? systemPrompt = null,
        ContextInclusion includeContext = ContextInclusion.None,
        int? maxTokens = 4096,
        Dictionary<string, object>? metadata = null,
        IEnumerable<SamplingMessage>? messages = null,
        CancellationToken cancellationToken = default)
    {
        var prompt = await promptService.GetServerPrompt(serviceProvider, mcpServer, name,
            arguments, cancellationToken: cancellationToken);

        return await mcpServer.SampleAsync(new CreateMessageRequestParams()
        {
            Messages = [..messages ?? [], .. prompt.Messages.Select(a =>  new SamplingMessage()
            {
                Role = a.Role,
                Content = [a.Content]
            })],
            IncludeContext = includeContext,
            MaxTokens = maxTokens ?? 4096,
            SystemPrompt = systemPrompt,
            ModelPreferences = modelHints?.ToModelPreferences(),
            Temperature = temperature,
            Metadata = metadata != null ? JsonSerializer.SerializeToElement(metadata) : null
        }, cancellationToken);
    }

    public async Task<T?> GetPromptSample<T>(IServiceProvider serviceProvider,
        McpServer mcpServer,
        string name,
        IReadOnlyDictionary<string, JsonElement> arguments,
        string? modelHint = null,
        float? temperature = null,
        string? systemPrompt = null,
        ContextInclusion includeContext = ContextInclusion.None,
        int? maxTokens = 4096,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var promptSample = await GetPromptSample(serviceProvider, mcpServer, name, arguments, modelHint,
            temperature, systemPrompt,
            includeContext,
            metadata: metadata,
            maxTokens: maxTokens,
            cancellationToken: cancellationToken);

        try
        {
            return JsonSerializer.Deserialize<T>(promptSample.ToText()?.CleanJson()!);

        }
        catch (JsonException exception)
        {

            var prompt = await promptService.GetServerPrompt(serviceProvider, mcpServer, name,
                arguments, cancellationToken: cancellationToken);

            var newResult = await mcpServer.SampleAsync(new CreateMessageRequestParams()
            {
                Messages = [.. prompt.Messages.Select(a => new SamplingMessage()
            {
                Role = a.Role,
                Content = [a.Content]
            }),
            $"Your last answer failed to JsonSerializer.Deserialize. Error message is included. Please try again.\n\n{exception.Message}".ToUserSamplingMessage()],
                IncludeContext = includeContext,
                MaxTokens = maxTokens ?? 4096,
                SystemPrompt = systemPrompt,
                ModelPreferences = modelHint?.ToModelPreferences(),
                Temperature = temperature
            }, cancellationToken);

            return JsonSerializer.Deserialize<T>(newResult.ToText()?.CleanJson()!);
        }

    }
}
