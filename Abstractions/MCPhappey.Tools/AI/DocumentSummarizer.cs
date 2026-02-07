using System.ComponentModel;
using System.Runtime.Serialization;
using System.Text.Json;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.AI;

public static class DocumentSummarizer
{
    private static readonly string[] ModelNames = ["gpt-5-mini", 
        "gemini-2.5-flash-lite", "claude-haiku-4-5-20251001", "mistral-small-latest"];
    //command-a-03-2025

    [Description("Parallel document summarize across multiple AI models.")]
    [McpServerTool(Title = "Document summarizer",
        Name = "document_summarizer_summarize",
        ReadOnly = true)]
    public static async Task<CallToolResult?> DocumentSummarizer_Summarize(
       [Description("Url of the document you would like to ask. Protected SharePoint and OneDive links are supported.")] string fileUrl,
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       [Description("Indicates the approximate length of the summary.")] SummarizeLength length = SummarizeLength.Medium,
       [Description("Indicates the style in which the summary will be delivered - in a free form paragraph or in bullet points.")] SummarizeFormat format = SummarizeFormat.paragraph,
       [Description("Controls how close to the original text the summary is.")] SummarizeExtractiveness extractiveness = SummarizeExtractiveness.low,
       [Description("A free-form instruction for modifying how the summaries get generated.")] string? additionalCommand = null,
       CancellationToken cancellationToken = default) =>
       await requestContext.WithExceptionCheck(async () =>
       await requestContext.WithStructuredContent(async () =>
    {
        var mcpServer = requestContext.Server;
        var samplingService = serviceProvider.GetRequiredService<SamplingService>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        var contents = string.Join("\n\n", files.GetTextFiles().Select(z => z.Contents.ToString()));

        var promptArgs = new Dictionary<string, JsonElement>
        {
            ["documentContents"] = JsonSerializer.SerializeToElement(contents),
            ["extractiveness"] = JsonSerializer.SerializeToElement(extractiveness),
            ["format"] = JsonSerializer.SerializeToElement(format),
        };

        if (length != SummarizeLength.Auto)
        {
            promptArgs.Add("length", JsonSerializer.SerializeToElement(length));
        }

        if (!string.IsNullOrEmpty(additionalCommand))
        {
            promptArgs.Add("additionalCommand", JsonSerializer.SerializeToElement(additionalCommand));
        }

        int? progressToken = 1;

        var markdown = $"{string.Join(", ", ModelNames)}\n{additionalCommand}";
        await requestContext.Server.SendMessageNotificationAsync(markdown, LoggingLevel.Debug, cancellationToken: CancellationToken.None);

        var tasks = ModelNames.Select(async modelName =>
            {
                try
                {
                    var markdown = $"{modelName}\n{additionalCommand}";
                    var startTime = DateTime.UtcNow;
                    var result = await samplingService.GetPromptSample(
                        serviceProvider,
                        mcpServer,
                        "ai-doc-summarizer",
                        promptArgs,
                        modelName,
                        metadata: new Dictionary<string, object>
                        {
                            { "google", new {
                                thinkingConfig = new {
                                    thinkingBudget = -1
                                }
                            } },
                            { "openai", new {
                                reasoning = new {
                                    effort = "low"
                                }
                            } },
                            { "mistral", new {
                            } },
                            { "anthropic", new {
                                thinking = new {
                                    budget_tokens = 1024
                                }
                            } },
                        },
                        cancellationToken: cancellationToken
                    );

                    var endTime = DateTime.UtcNow;
                    result.Meta?.Add("duration", (endTime - startTime).ToString());

                    progressToken = await requestContext.Server.SendProgressNotificationAsync(
                        requestContext,
                        progressToken,
                        markdown,
                        ModelNames.Length,
                        cancellationToken
                    );

                    return result;
                }
                catch (Exception ex)
                {
                    await requestContext.Server.SendMessageNotificationAsync(
                        $"{modelName} failed: {ex.Message}",
                        LoggingLevel.Error
                    );
                    return null; // Failure → skip
                }
            });

        var results = await Task.WhenAll(tasks);

        // Return only successful results
        return new MessageResults()
        {
            Results = results.OfType<CreateMessageResult>()
        };
    }));


    public enum SummarizeLength
    {
        [EnumMember(Value = "auto")]
        Auto,
        [EnumMember(Value = "short")]
        Short,
        [EnumMember(Value = "medium")]
        Medium,
        [EnumMember(Value = "long")]
        Long
    }

    public enum SummarizeFormat
    {
        [EnumMember(Value = "auto")]
        auto,
        [EnumMember(Value = "paragraph")]
        paragraph,
        [EnumMember(Value = "bullets")]
        bullets
    }


    public enum SummarizeExtractiveness
    {
        [EnumMember(Value = "auto")]
        auto,
        [EnumMember(Value = "low")]
        low,
        [EnumMember(Value = "medium")]
        medium,
        [EnumMember(Value = "high")]
        high
    }


}

