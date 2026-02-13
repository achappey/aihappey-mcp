using System.ComponentModel;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.AI302;

public static class AI302AnswerMachinePlugin
{
    [Description("Generate an answer for question text, image URL, or base64 image input.")]
    [McpServerTool(Title = "302.AI answer machine", Name = "302ai_answer_generate", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> AI302_Answer_Generate(
        [Description("Question text, image URL, or base64 data URL starting with data:image.")] string content,
        [Description("Model name, e.g. gpt-4o.")] string model,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Language code, e.g. zh, en, ja.")] string lang = "zh",
        [Description("Whether to request streaming output.")] bool stream = false,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var client = serviceProvider.GetRequiredService<AI302Client>();

                var body = new JsonObject
                {
                    ["content"] = content,
                    ["model"] = model,
                    ["lang"] = lang,
                    ["stream"] = stream
                };

                JsonNode? response = await client.PostAsync("302/answer/generate", body, cancellationToken);
                return response;
            }));
}

