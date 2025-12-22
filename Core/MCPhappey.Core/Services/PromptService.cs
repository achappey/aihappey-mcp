using System.Text.Json;
using MCPhappey.Core.Extensions;
using MCPhappey.Common.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Microsoft.Extensions.DependencyInjection;
using MCPhappey.Common.Extensions;
using Microsoft.SemanticKernel;
using System.Text.Json.Serialization;

namespace MCPhappey.Core.Services;

public class PromptService(Kernel kernel, IServerDataProvider? dynamicDataService = null)
{

    public async Task<IEnumerable<McpServerPrompt>> GetServerToolPromptTemplates(ServerConfig serverConfig)
        => await Task.FromResult(serverConfig.Server.ToolPrompts == true ?
                serverConfig.Server.Plugins?.SelectMany(a => kernel.GetPromptsFromType(a, serverConfig.Server.ServerInfo.Icons ?? []) ?? []) : []) ?? [];

    public async Task<IEnumerable<PromptTemplate>> GetServerPromptTemplates(ServerConfig serverConfig,
             CancellationToken cancellationToken = default) => serverConfig.SourceType switch
             {
                 ServerSourceType.Static => (await Task.FromResult(serverConfig.PromptList?.Prompts)) ?? [],
                 ServerSourceType.Dynamic => await dynamicDataService!.GetPromptsAsync(serverConfig.Server.ServerInfo.Name, cancellationToken) ?? [],
                 _ => await Task.FromResult(serverConfig.PromptList?.Prompts) ?? [],
             };

    public async Task<ListPromptsResult> GetServerPrompts(ServerConfig serverConfig,
          CancellationToken cancellationToken = default) => new()
          {
              Prompts = [..(await GetServerPromptTemplates(serverConfig, cancellationToken))
                .Select(a => a.Template)
                .OrderBy(a => a.Name)
                .ToList() ?? [], ..(await GetServerToolPromptTemplates(serverConfig))
                .Select(a => a.ProtocolPrompt)
                .OrderBy(a => a.Name)
                .ToList() ?? []]
          };

    public async Task<GetPromptResult> GetServerPrompt(
        IServiceProvider serviceProvider,
        McpServer mcpServer,
        string name,
        IReadOnlyDictionary<string, JsonElement>? arguments = null,
        RequestContext<GetPromptRequestParams>? requestContext = null,
        CancellationToken cancellationToken = default)
    {
        var serverConfig = serviceProvider.GetServerConfig(mcpServer) ?? throw new Exception();
        var allServers = serviceProvider.GetService<IReadOnlyList<ServerConfig>>() ?? throw new Exception();
        var prompts = await GetServerPromptTemplates(serverConfig, cancellationToken);
        var prompt = prompts?.FirstOrDefault(a => a.Template.Name == name)
            ?? allServers.Where(a => a.SourceType == ServerSourceType.Static)
                .SelectMany(z => z.PromptList?.Prompts?.Where(a => a.Template?.Name == name) ?? []).FirstOrDefault();

        if (prompt == null && requestContext != null)
        {
            var toolPrompts = await GetServerToolPromptTemplates(serverConfig);
            var toolPrompt = toolPrompts.FirstOrDefault(a => a.ProtocolPrompt.Name == name);

            if (toolPrompt != null)
            {
                return await Task.FromResult(new GetPromptResult
                {
                    Description = prompt?.Template.Description,
                    Messages = [new PromptMessage() {
                        Role = Role.User,
                        Content = (toolPrompt.ProtocolPrompt.Name + $"<br>{toolPrompt.ProtocolPrompt.Description}\n\n```json\n"
                                + JsonSerializer.Serialize(requestContext.Params?.Arguments, WriteIndented) + "\n```").ToTextContentBlock()
                    }]
                });
            }
        }

        ArgumentNullException.ThrowIfNull(prompt);
        prompt.Template.ValidatePrompt(arguments);

        var resourceService = serviceProvider.GetRequiredService<ResourceService>();
        var promptMessage = new PromptMessage
        {
            Role = Role.User,
            Content = prompt?.Prompt
                .FormatPrompt(prompt.Template, arguments)!
                .ToTextContentBlock()!
        };

        return await Task.FromResult(new GetPromptResult
        {
            Description = prompt?.Template.Description,
            Messages = [promptMessage]
        });
    }
    private static readonly JsonSerializerOptions WriteIndented = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

}
