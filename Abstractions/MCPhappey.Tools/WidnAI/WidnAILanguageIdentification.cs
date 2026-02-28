using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.WidnAI;

public static class WidnAILanguageIdentification
{
    [Description("Identify the language of input text using WidnAI POST /language-identification and return ranked language candidates with confidence scores.")]
    [McpServerTool(
        Name = "widnai_language_identification",
        Title = "WidnAI language identification",
        ReadOnly = true,
        OpenWorld = true)]
    public static async Task<CallToolResult?> WidnAI_Language_Identification(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Text to identify language for.")] string text,
        [Description("Maximum number of language candidates to return. Default: 1.")] int topResults = 1,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(text);

            if (topResults <= 0)
                topResults = 1;

            var body = new
            {
                text,
                top_results = topResults
            };

            var widn = serviceProvider.GetRequiredService<WidnAIClient>();
            var raw = await widn.PostJsonAsync("language-identification", body, cancellationToken) ?? new JsonArray();

            var languages = new JsonArray();
            if (raw is JsonArray arr)
            {
                foreach (var node in arr)
                {
                    if (node is not JsonObject item)
                        continue;

                    languages.Add(new JsonObject
                    {
                        ["languageCode"] = item["language_code"]?.GetValue<string>(),
                        ["language"] = item["language"]?.GetValue<string>(),
                        ["score"] = item["score"]?.GetValue<double?>()
                    });
                }
            }

            var structured = new JsonObject
            {
                ["operation"] = "language-identification",
                ["input"] = new JsonObject
                {
                    ["text"] = text,
                    ["topResults"] = topResults
                },
                ["result"] = new JsonObject
                {
                    ["languages"] = languages
                }
            };

            return new CallToolResult
            {
                StructuredContent = structured,
                Content =
                [
                    JsonSerializer.Serialize(structured, new JsonSerializerOptions { WriteIndented = true }).ToTextContentBlock()
                ]
            };
        });
}

