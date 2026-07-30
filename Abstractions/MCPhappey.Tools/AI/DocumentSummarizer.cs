using System.ComponentModel;
using System.Runtime.Serialization;
using System.Text.Json;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.AI;

public static class DocumentSummarizer
{
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
       await ModelContextToolExtensions.WithExceptionCheck(async () =>
       await requestContext.WithStructuredContent(async () =>
    {
        var mcpServer = requestContext.Server;
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

        var tasks = DirectPromptRunner.DocumentModels.Select(async target =>
            {
                try
                {
                    var markdown = $"{target.Model}\n{additionalCommand}";
                    var result = await DirectPromptRunner.RunAsync(serviceProvider, mcpServer, target.Provider,
                        "ai-doc-summarizer", promptArgs, 8192, "low", 1024, cancellationToken);

                    progressToken = await requestContext.Server.SendProgressNotificationAsync(
                        requestContext,
                        progressToken,
                        markdown,
                        DirectPromptRunner.DocumentModels.Length,
                        cancellationToken
                    );

                    return result;
                }
                catch (Exception)
                {
                  
                    return null; // Failure → skip
                }
            });

        var results = await Task.WhenAll(tasks);

        // Return only successful results
        return new MessageResults()
        {
            Results = results.OfType<ProviderMessageResult>()
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

