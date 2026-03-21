using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Olostep;

public static class OlostepAnswers
{
    [Description("Create an Olostep answer task that searches and browses the web to complete the requested task and return structured results.")]
    [McpServerTool(Title = "Olostep create answer", Name = "olostep_answer_create", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> Olostep_Answer_Create(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Task or question Olostep should answer using web research.")] string task,
        [Description("Optional JSON schema string or plain text description describing the desired result format.")] string? json_format = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, _, _) = await requestContext.Server.TryElicit(
                new OlostepCreateAnswerRequest
                {
                    Task = task,
                    JsonFormat = json_format
                },
                cancellationToken);

            ArgumentException.ThrowIfNullOrWhiteSpace(typed.Task);

            var payload = new JsonObject
            {
                ["task"] = typed.Task
            };

            var jsonFormatNode = OlostepHelpers.ParseJsonObjectOrString(typed.JsonFormat);
            if (jsonFormatNode is not null)
                payload["json_format"] = jsonFormatNode;

            var client = serviceProvider.GetRequiredService<OlostepClient>();
            var response = await client.PostJsonAsync("v1/answers", payload, cancellationToken) ?? new JsonObject();
            var answerId = OlostepHelpers.GetString(response, "id");
            var summary = $"Olostep answer completed. Id={answerId ?? "unknown"}.";

            return new CallToolResult
            {
                Meta = await requestContext.GetToolMeta(),
                StructuredContent = OlostepHelpers.CreateStructuredResponse(
                    "/v1/answers",
                    payload,
                    response,
                    ("id", answerId),
                    ("task", typed.Task),
                    ("hasResult", response["result"] is not null)),
                Content = [summary.ToTextContentBlock()]
            };
        });

    [Description("Retrieve a previously completed Olostep answer by answer id.")]
    [McpServerTool(Title = "Olostep get answer", Name = "olostep_answer_get", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> Olostep_Answer_Get(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Answer identifier returned by Olostep answer creation.")] string answer_id,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, _, _) = await requestContext.Server.TryElicit(
                new OlostepGetAnswerRequest
                {
                    AnswerId = answer_id
                },
                cancellationToken);

            ArgumentException.ThrowIfNullOrWhiteSpace(typed.AnswerId);

            var client = serviceProvider.GetRequiredService<OlostepClient>();
            var response = await client.GetJsonAsync($"v1/answers/{Uri.EscapeDataString(typed.AnswerId)}", null, cancellationToken) ?? new JsonObject();
            var sourcesCount = OlostepHelpers.CountArray(response["result"]?["sources"]);
            var summary = $"Olostep answer retrieved. Id={typed.AnswerId}. Sources={sourcesCount}.";

            return new CallToolResult
            {
                Meta = await requestContext.GetToolMeta(),
                StructuredContent = OlostepHelpers.CreateStructuredResponse(
                    "/v1/answers/{answer_id}",
                    new { answer_id = typed.AnswerId },
                    response,
                    ("id", OlostepHelpers.GetString(response, "id") ?? typed.AnswerId),
                    ("task", OlostepHelpers.GetString(response, "task")),
                    ("sourcesCount", sourcesCount)),
                Content = [summary.ToTextContentBlock()]
            };
        });
}

public sealed class OlostepCreateAnswerRequest
{
    [JsonPropertyName("task")]
    [Required]
    [Description("Task or question Olostep should answer using web research.")]
    public string Task { get; set; } = string.Empty;

    [JsonPropertyName("json_format")]
    [Description("Optional JSON schema string or plain text description describing the desired result format.")]
    public string? JsonFormat { get; set; }
}

public sealed class OlostepGetAnswerRequest
{
    [JsonPropertyName("answer_id")]
    [Required]
    [Description("Answer identifier returned by Olostep answer creation.")]
    public string AnswerId { get; set; } = string.Empty;
}
