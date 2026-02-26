using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Mixedbread;

public static class MixedbreadParsing
{
    [Description("Create a Mixedbread parsing job.")]
    [McpServerTool(
        Title = "Mixedbread Create Parsing Job",
        Name = "mixedbread_parsing_create_job",
        OpenWorld = true,
        ReadOnly = false,
        Destructive = false)]
    public static async Task<CallToolResult?> MixedbreadParsing_CreateJob(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("File ID to parse.")] string fileId,
        [Description("Optional element_types JSON array string.")] string? elementTypesJson = null,
        [Description("Optional chunking_strategy (page).")]
        string? chunkingStrategy = null,
        [Description("Optional return_format (html, markdown, plain).")]
        string? returnFormat = null,
        [Description("Optional mode (fast, high_quality).")]
        string? mode = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(fileId);

                var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                    new MixedbreadParsingCreateRequest
                    {
                        FileId = fileId,
                        ElementTypesJson = elementTypesJson,
                        ChunkingStrategy = chunkingStrategy,
                        ReturnFormat = returnFormat,
                        Mode = mode
                    },
                    cancellationToken);
             
                var payload = new JsonObject
                {
                    ["file_id"] = typed.FileId
                };

                AddJson(payload, "element_types", typed.ElementTypesJson);
                if (!string.IsNullOrWhiteSpace(typed.ChunkingStrategy)) payload["chunking_strategy"] = typed.ChunkingStrategy;
                if (!string.IsNullOrWhiteSpace(typed.ReturnFormat)) payload["return_format"] = typed.ReturnFormat;
                if (!string.IsNullOrWhiteSpace(typed.Mode)) payload["mode"] = typed.Mode;

                var settings = serviceProvider.GetRequiredService<MixedbreadSettings>();
                using var client = MixedbreadHttp.CreateClient(serviceProvider, settings);
                using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/parsing/jobs")
                {
                    Content = MixedbreadHttp.CreateJsonContent(payload)
                };

                var json = await MixedbreadHttp.SendAsync(client, request, cancellationToken);
                return json;
            }));

    [Description("Cancel a Mixedbread parsing job.")]
    [McpServerTool(
        Title = "Mixedbread cancel parsing job",
        Name = "mixedbread_parsing_cancel_job",
        OpenWorld = true,
        ReadOnly = false,
        Destructive = false)]
    public static async Task<CallToolResult?> MixedbreadParsing_CancelJob(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Parsing job ID.")] string jobId,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

                var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                    new MixedbreadParsingCancelRequest
                    {
                        JobId = jobId
                    },
                    cancellationToken);
             
                var settings = serviceProvider.GetRequiredService<MixedbreadSettings>();
                using var client = MixedbreadHttp.CreateClient(serviceProvider, settings);
                using var request = new HttpRequestMessage(new HttpMethod("PATCH"), $"/v1/parsing/jobs/{Uri.EscapeDataString(jobId)}");
                var json = await MixedbreadHttp.SendAsync(client, request, cancellationToken);
                return json;
            }));

    [Description("Delete a Mixedbread parsing job.")]
    [McpServerTool(
        Title = "Mixedbread Delete Parsing Job",
        Name = "mixedbread_parsing_delete_job",
        OpenWorld = true,
        ReadOnly = false,
        Destructive = true)]
    public static async Task<CallToolResult?> MixedbreadParsing_DeleteJob(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Parsing job ID.")] string jobId,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                if (string.IsNullOrWhiteSpace(jobId))
                    throw new ArgumentException("jobId is required.");

                var settings = serviceProvider.GetRequiredService<MixedbreadSettings>();

                return await requestContext.ConfirmAndDeleteAsync<ConfirmDeleteMixedbreadParsingJob>(
                    jobId,
                    async ct =>
                    {
                        using var client = MixedbreadHttp.CreateClient(serviceProvider, settings);
                        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/v1/parsing/jobs/{Uri.EscapeDataString(jobId)}");
                        _ = await MixedbreadHttp.SendAsync(client, request, ct);
                    },
                    $"Parsing job '{jobId}' deleted successfully.",
                    cancellationToken);
            }));

    private static void AddJson(JsonObject payload, string name, string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        var node = JsonNode.Parse(json);
        if (node != null) payload[name] = node;
    }
}

[Description("Please confirm the Mixedbread parsing job creation.")]
public sealed class MixedbreadParsingCreateRequest
{
    [JsonPropertyName("fileId")]
    [Required]
    [Description("File ID to parse.")]
    public string FileId { get; set; } = default!;

    [JsonPropertyName("elementTypesJson")]
    [Description("Element types JSON array string.")]
    public string? ElementTypesJson { get; set; }

    [JsonPropertyName("chunkingStrategy")]
    [Description("Chunking strategy.")]
    public string? ChunkingStrategy { get; set; }

    [JsonPropertyName("returnFormat")]
    [Description("Return format.")]
    public string? ReturnFormat { get; set; }

    [JsonPropertyName("mode")]
    [Description("Parser mode.")]
    public string? Mode { get; set; }

}

[Description("Please confirm the Mixedbread parsing job cancellation.")]
public sealed class MixedbreadParsingCancelRequest
{
    [JsonPropertyName("jobId")]
    [Required]
    [Description("Parsing job ID.")]
    public string JobId { get; set; } = default!;

  
}

[Description("Please confirm deletion of the parsing job ID: {0}")]
public sealed class ConfirmDeleteMixedbreadParsingJob : IHasName
{
    [JsonPropertyName("name")]
    [Required]
    [Description("The parsing job ID to delete (must match exactly).")]
    public string Name { get; set; } = default!;
}
