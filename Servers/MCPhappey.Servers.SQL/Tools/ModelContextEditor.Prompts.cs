using System.ComponentModel;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Servers.SQL.Extensions;
using MCPhappey.Servers.SQL.Repositories;
using MCPhappey.Servers.SQL.Tools.Models;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Servers.SQL.Tools;

public static partial class ModelContextEditor
{

    [Description("Adds a prompt to a MCP-server")]
    [McpServerTool(Title = "Add a prompt to an MCP-server",
        Destructive = true,
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = false)]
    public static async Task<CallToolResult?> ModelContextEditor_AddPrompt(
        [Description("Name of the server")]
            string serverName,
        [Description("The name of the prompt to add")]
            string promptName,
        [Description("The prompt to add. You can use {argument} style placeholders for prompt arguments.")]
            string prompt,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional title of the prompt.")]
        string? title = null,
        [Description("Optional description of the prompt.")]
        string? description = null,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
    {
        var server = await serviceProvider.GetServer(serverName, cancellationToken);
        await requestContext.Server.SendMessageNotificationAsync($"Found server: {server.Name}", LoggingLevel.Info, cancellationToken);

        var (typed, notAccepted, result) = await requestContext.Server.TryElicit(new AddMcpPrompt()
        {
            Name = promptName.Slugify().ToLowerInvariant(),
            Prompt = prompt,
            Title = title,
            Description = description
        }, cancellationToken);

        var serverRepository = serviceProvider.GetRequiredService<ServerRepository>();
        await requestContext.Server.SendMessageNotificationAsync($"Creating prompt: {typed.Name}", LoggingLevel.Info, cancellationToken);

        var item = await serverRepository.AddServerPrompt(server.Id, typed.Prompt,
            typed.Name,
            typed.Description,
            typed.Title,
            arguments: typed.Prompt.ExtractPromptArguments().Select(a => new SQL.Models.PromptArgument()
            {
                Name = a,
                Required = true
            }));

        await requestContext.Server.SendMessageNotificationAsync($"Prompt created. Id: {item.Id}", LoggingLevel.Info, cancellationToken);

        return item.ToPromptTemplate();
    }));

    [Description("Updates a resource of a MCP-server")]
    [McpServerTool(Title = "Update a prompt of an MCP-server",
        Destructive = true,
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = false)]
    public static async Task<CallToolResult?> ModelContextEditor_UpdatePrompt(
        [Description("Name of the server")] string serverName,
        [Description("Name of the prompt to update")] string promptName,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("New value for the prompt property")] string? newPrompt = null,
        [Description("New value for the title property")] string? newTitle = null,
        [Description("New value for the description property")] string? newDescription = null,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
    {
        var serverRepository = serviceProvider.GetRequiredService<ServerRepository>();
        var server = await serviceProvider.GetServer(serverName, cancellationToken);
        var prompt = server.Prompts.FirstOrDefault(a => a.Name == promptName) ?? throw new ArgumentNullException();
        var (typed, notAccepted, result) = await requestContext.Server.TryElicit(new UpdateMcpPrompt()
        {
            Prompt = newPrompt ?? prompt.PromptTemplate,
            Description = newDescription ?? prompt.Description,
            Name = prompt.Name,
            Title = newTitle ?? prompt.Title,
        }, cancellationToken);

        prompt.Description = typed.Description;
        prompt.Title = typed.Title;

        if (!string.IsNullOrEmpty(typed.Prompt))
        {
            prompt.PromptTemplate = typed.Prompt;
        }

        if (!string.IsNullOrEmpty(typed.Name))
        {
            prompt.Name = typed.Name.Slugify().ToLowerInvariant();
        }

        var usedArguments = prompt.PromptTemplate.ExtractPromptArguments();
        var toRemove = prompt.Arguments
            .Where(a => !usedArguments.Contains(a.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();

        foreach (var arg in toRemove)
        {
            await serverRepository.DeletePromptArgument(arg.Id);
        }

        var existingNames = prompt.Arguments.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var name in usedArguments)
        {
            if (!existingNames.Contains(name))
            {
                prompt.Arguments.Add(new SQL.Models.PromptArgument
                {
                    Name = name,
                    Required = true // default
                });
            }
        }

        var updated = await serverRepository.UpdatePrompt(prompt);

        return updated.ToPromptTemplate();
    }));

    [Description("Updates a prompt argument of a MCP-server")]
    [McpServerTool(Title = "Update a prompt argument of an MCP-server",
        Destructive = true,
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = false)]
    public static async Task<CallToolResult?> ModelContextEditor_UpdatePromptArgument(
       [Description("Name of the server")] string serverName,
       [Description("Name of the prompt to update")] string promptName,
       [Description("Name of the prompt argument to update")] string promptArgumentName,
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       [Description("New value for the prompt required property")] bool? required = null,
       [Description("New value for the prompt description property")] string? newDescription = null,
       CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
    {
        var server = await serviceProvider.GetServer(serverName, cancellationToken);
        var prompt = server.Prompts.FirstOrDefault(a => a.Name == promptName) ?? throw new ArgumentNullException(nameof(promptName));
        var promptArgument = prompt.Arguments.FirstOrDefault(a => a.Name == promptArgumentName) ?? throw new ArgumentNullException(nameof(promptArgumentName));
        var (typed, notAccepted, result) = await requestContext.Server.TryElicit(new UpdateMcpPromptArgument()
        {
            Required = required ?? promptArgument.Required,
            Description = newDescription ?? promptArgument.Description
        }, cancellationToken);

        promptArgument.Required = typed.Required;
        promptArgument.Description = typed.Description;

        var serverRepository = serviceProvider.GetRequiredService<ServerRepository>();
        var updated = await serverRepository.UpdatePromptArgument(promptArgument);

        return updated.ToPromptArgument();
    }));

    [Description("Deletes a prompt from a MCP-server")]
    [McpServerTool(Title = "Delete a prompt from an MCP-server",
        Destructive = true,
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = false)]
    public static async Task<CallToolResult?> ModelContextEditor_DeletePrompt(
        [Description("Name of the server")] string serverName,
        [Description("Name of the prompt to delete")] string promptName,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
    {
        var serverRepository = serviceProvider.GetRequiredService<ServerRepository>();
        var server = await serviceProvider.GetServer(serverName, cancellationToken);

        // One-liner – the helper does the rest
        return await requestContext.ConfirmAndDeleteAsync<ConfirmDeletePrompt>(
            promptName,
            async _ =>
            {
                var prompt = server.Prompts.First(z => z.Name == promptName);
                await serverRepository.DeletePrompt(prompt.Id);
            },
            $"Prompt {promptName} deleted.",
            cancellationToken);
    });

    [Description("Get a prompt from a MCP-server.")]
    [McpServerTool(
    Title = "Get a prompt",
    ReadOnly = true,
    Idempotent = true,
    Destructive = false,
    OpenWorld = false)]
    public static async Task<CallToolResult?> ModelContextEditor_GetPrompt(
    [Description("Name of the server")] string serverName,
    [Description("Name of the prompt")] string promptName,
    IServiceProvider serviceProvider,
    RequestContext<CallToolRequestParams> requestContext,
    CancellationToken cancellationToken = default) =>
    await requestContext.WithExceptionCheck(async () =>
    await requestContext.WithStructuredContent(async () =>
    {
        var server = await serviceProvider.GetServer(serverName, cancellationToken);

        var prompt =
            server.Prompts
                .FirstOrDefault(p =>
                    string.Equals(p.Name, promptName, StringComparison.OrdinalIgnoreCase))
            ?? throw new Exception($"Prompt '{promptName}' not found on server '{serverName}'.");

        return prompt.ToPromptTemplate();
    }));

    [Description("List prompts from a MCP-server.")]
    [McpServerTool(
        Title = "List prompts",
        ReadOnly = true,
        Idempotent = true,
        Destructive = false,
        OpenWorld = false)]
    public static async Task<CallToolResult?> ModelContextEditor_ListPrompts(
        [Description("Name of the server")] string serverName,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("When true, include full prompt templates and arguments.")]
    bool includeDetails = false,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var server = await serviceProvider.GetServer(serverName, cancellationToken);

            var prompts =
                server.Prompts
                    .OrderBy(p => p.Name)
                    .Select(p => new
                    {
                        p.Name,
                        p.Title,
                        p.Description,

                        Details = includeDetails
                            ? new
                            {
                                Prompt = p.PromptTemplate,
                                Arguments = p.Arguments?.Select(a => new
                                {
                                    a.Name,
                                    a.Description,
                                    a.Required
                                })
                            }
                            : null
                    });

            return await Task.FromResult(new
            {
                server = server.Name,
                includeDetails,
                prompts
            });
        }));

}

