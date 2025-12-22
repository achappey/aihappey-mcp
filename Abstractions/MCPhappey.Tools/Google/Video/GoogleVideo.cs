using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Mscc.GenerativeAI;

namespace MCPhappey.Tools.Google.Video;

public static class GoogleVideo
{
    [Description("Prompt a YouTube video using Google Gemini AI.")]
    [McpServerTool(
        Title = "Prompt YouTube video with Gemini",
        ReadOnly = true)]
    public static async Task<CallToolResult?> GoogleVideo_PromptYouTube(
        [Description("Prompt or instruction for the Gemini model (e.g. 'Summarize the video', 'Extract action points', etc.)")]
        string prompt,
        [Description("YouTube video URL")]
        string url,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        {
            var googleAI = serviceProvider.GetRequiredService<GoogleAI>();
            var googleClient = googleAI.GenerativeModel("gemini-2.5-flash");

            var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
                        new GoogleVideoPromptYouTube
                        {
                            Prompt = prompt,
                            YouTubeUrl = url
                        },
                        cancellationToken);

            var graphItem = await googleClient.GenerateContent(new GenerateContentRequest()
            {
                Contents =
                [
                    new Content(typed.Prompt)
                        {
                            Parts = [
                                new FileData() {
                                    FileUri = typed.YouTubeUrl
                                }
                            ]
                        }
                ]
            }, cancellationToken: cancellationToken);

            return graphItem?.Text?.ToTextCallToolResponse();
        });

    [Description("Please fill in the YouTube prompt details.")]
    public class GoogleVideoPromptYouTube
    {
        [JsonPropertyName("prompt")]
        [Required]
        [Description("The YouTube video question prompt.")]
        public string Prompt { get; set; } = default!;

        [JsonPropertyName("youTubeUrl")]
        [Required]
        [Description("YouTube url.")]
        public string YouTubeUrl { get; set; } = default!;

    }
}