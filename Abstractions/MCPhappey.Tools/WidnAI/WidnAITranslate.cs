using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.WidnAI;

public static class WidnAITranslate
{
    [Description("Translate text with WidnAI POST /translate using primitive parameters and return normalized structured translation output.")]
    [McpServerTool(
        Name = "widnai_translate_text",
        Title = "WidnAI translate text",
        ReadOnly = true,
        OpenWorld = true)]
    public static async Task<CallToolResult?> WidnAI_Translate_Text(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("JSON array of source strings. Example: [\"Hello\",\"How are you?\"]")]
        string sourceTextJson,
        [Description("Source locale, e.g., en.")]
        string sourceLocale,
        [Description("Target locale, e.g., pt-PT.")]
        string targetLocale,
        [Description("Model: anthill, sugarloaf, vesuvius, sugarloaf-3.1, sugarloaf-4.0. Default: sugarloaf.")]
        string model = "sugarloaf",
        [Description("Tone: formal, informal, automatic. Default: automatic.")]
        string tone = "automatic",
        [Description("Optional instructions for translation context.")]
        string? instructions = null,
        [Description("Optional glossary id.")]
        string? glossaryId = null,
        [Description("Optional JSON array of glossary objects: [{\"term\":\"...\",\"translation\":\"...\"}]")]
        string? glossaryJson = null,
        [Description("Optional JSON array of few-shot examples: [{\"source\":\"...\",\"target\":\"...\"}]")]
        string? fewshotExamplesJson = null,
        [Description("Optional maximum token count.")]
        int? maxTokens = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceTextJson);
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceLocale);
            ArgumentException.ThrowIfNullOrWhiteSpace(targetLocale);

            var sourceText = ParseStringArray(sourceTextJson, "sourceTextJson");
            if (sourceText.Count == 0)
                throw new ArgumentException("sourceTextJson must contain at least one string item.", nameof(sourceTextJson));

            var glossary = ParseObjectArray(glossaryJson);
            var fewshot = ParseObjectArray(fewshotExamplesJson);

            var body = new JsonObject
            {
                ["sourceText"] = sourceText,
                ["config"] = new JsonObject
                {
                    ["sourceLocale"] = sourceLocale,
                    ["targetLocale"] = targetLocale,
                    ["tone"] = NormalizeOptional(tone) ?? "automatic",
                    ["model"] = string.IsNullOrWhiteSpace(model) ? "sugarloaf" : model,
                    ["instructions"] = NormalizeOptional(instructions),
                    ["glossaryId"] = NormalizeOptional(glossaryId),
                    ["glossary"] = glossary,
                    ["fewshotExamples"] = fewshot,
                    ["maxTokens"] = maxTokens
                }
            };

            var widn = serviceProvider.GetRequiredService<WidnAIClient>();
            var raw = await widn.PostJsonAsync("translate", body, cancellationToken) ?? new JsonObject();

            var targetText = raw["targetText"] as JsonArray ?? [];
            var structured = new JsonObject
            {
                ["operation"] = "translate",
                ["input"] = body,
                ["result"] = new JsonObject
                {
                    ["targetText"] = targetText,
                    ["inputCharacters"] = raw["inputCharacters"]?.GetValue<double?>(),
                    ["inputTokens"] = raw["inputTokens"]?.GetValue<double?>(),
                    ["outputTokens"] = raw["outputTokens"]?.GetValue<double?>(),
                    ["raw"] = raw
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

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private static JsonArray ParseStringArray(string rawJson, string argName)
    {
        try
        {
            var node = JsonNode.Parse(rawJson);
            if (node is not JsonArray arr)
                throw new ArgumentException($"{argName} must be a JSON array of strings.", argName);

            var result = new JsonArray();
            foreach (var item in arr)
            {
                var value = item?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                result.Add(value);
            }

            return result;
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"{argName} is not valid JSON.", argName, ex);
        }
    }

    private static JsonArray? ParseObjectArray(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            return null;

        try
        {
            var node = JsonNode.Parse(rawJson);
            if (node is not JsonArray arr)
                return null;

            var result = new JsonArray();
            foreach (var item in arr)
            {
                if (item is JsonObject obj)
                    result.Add(obj);
            }

            return result.Count == 0 ? null : result;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

