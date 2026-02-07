using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.AI;

public static class OpenDeepResearch
{
    [Description("Creates a detailed research brief from a user topic and related documents. The tool reviews uploaded or linked sources, checks if clarification is needed, and produces a clear, structured research question ready for deep analysis.")]
    [McpServerTool(Title = "Create Research Brief", Name = "open_deepresearch_create_brief", ReadOnly = true)]
    public static async Task<CallToolResult?> OpenDeepResearch_CreateBrief(
           [Description("Research prompt")] string query,
       //    [Description("List of background document URLs for the research. Max 5 urls.")] List<string> fileUrls,
           IServiceProvider serviceProvider,
           RequestContext<CallToolRequestParams> requestContext,
           CancellationToken cancellationToken = default) =>
           await requestContext.WithExceptionCheck(async () =>
           await requestContext.WithOboGraphClient(async graphClient =>
           await requestContext.WithStructuredContent(async () =>
        {
            var mcpServer = requestContext.Server;
            var samplingService = serviceProvider.GetRequiredService<SamplingService>();
            var downloadService = serviceProvider.GetRequiredService<DownloadService>();
            var semaphore = new SemaphoreSlim(3);

            var documents = new List<FileItem>();

            var promptArgs = new Dictionary<string, JsonElement>
            {
                ["documents"] = JsonSerializer.SerializeToElement(string.Empty),
                ["researchTopic"] = JsonSerializer.SerializeToElement(query),
                ["date"] = JsonSerializer.SerializeToElement(DateTime.UtcNow.ToLongDateString())
            };

            var clarification = await samplingService.GetPromptSample<ClarifyWithUserResponse>(
                serviceProvider,
                mcpServer,
                "clarify-with-user-instructions",
                promptArgs,
                "gpt-5.1",
                maxTokens: 4096 * 4,
                metadata: new Dictionary<string, object>
                {
                            { "openai", new {
                                reasoning = new {
                                    effort = "low"
                                }
                            } },
                },
                cancellationToken: cancellationToken
            );

            if (clarification?.NeedClarification == true)
            {
                throw new Exception(JsonSerializer.Serialize(clarification));
            }

            var researchQuestion = await samplingService.GetPromptSample(
                       serviceProvider,
                       mcpServer,
                       "transform-messages-into-research-topic",
                       promptArgs,
                       "gpt-5.1",
                        maxTokens: 4096 * 4,
                       metadata: new Dictionary<string, object>
                       {
                            { "openai", new {
                                reasoning = new {
                                    effort = "low"
                                }
                            } },
                       },
                       cancellationToken: cancellationToken
                   );

            var uploaded = await graphClient.Upload(
                $"{nameof(OpenDeepResearch_CreateBrief).ToOutputFileName()}.md",
                BinaryData.FromString(researchQuestion.ToText() ?? string.Empty),
                cancellationToken);

            return uploaded?.ToCallToolResult();
        })));


    public class ClarifyWithUserResponse
    {
        [JsonPropertyName("need_clarification")]
        public bool NeedClarification { get; set; }

        [JsonPropertyName("question")]
        public string? Question { get; set; }

        [JsonPropertyName("verification")]
        public string? Verification { get; set; }
    }
}

