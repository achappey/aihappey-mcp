using System.ComponentModel;
using System.Text.Json;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Mscc.GenerativeAI;

namespace MCPhappey.Tools.Google.Audio;

public static partial class GoogleAudio
{
    [Description("Creates a full podcast audio script from a document, using AI to generate a storyline and script.")]
    [McpServerTool(
        Title = "Create podcast script from document",
        ReadOnly = true
    )]
    public static async Task<CallToolResult> GoogleAudio_CreatePodcastScript(
        [Description("The url of the input file")]
        string inputFileUrl,
        [Description("The input prompt to guide the creation of the podcast")]
        string prompt,
        [Description("Name of speaker one")]
        string nameSpeakerOne,
        [Description("Name of speaker two")]
        string nameSpeakerTwo,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        var googleAI = serviceProvider.GetRequiredService<GoogleAI>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var samplingService = serviceProvider.GetRequiredService<SamplingService>();
        var contents = await downloadService.ScrapeContentAsync(serviceProvider,
            requestContext.Server, inputFileUrl, cancellationToken);
        var promptArgs = new Dictionary<string, JsonElement>
        {
            ["documentContent"] = JsonSerializer.SerializeToElement(string.Join("\n\n", contents.GetTextFiles()
                .Select(t => t.Contents.ToString()))),
            ["inputAroundPodcast"] = JsonSerializer.SerializeToElement(prompt)
        };

        var result = await samplingService.GetPromptSample(
                     serviceProvider,
                     requestContext.Server,
                     "create-podcast-outline-from-document",
                     promptArgs,
                     "gpt-5.1",
                     cancellationToken: cancellationToken
                 );

        var resultValue = result.ToText();

        var scriptPromptArgs = new Dictionary<string, JsonElement>
        {
            ["podcastOutline"] = JsonSerializer.SerializeToElement(resultValue),
            ["inputAroundPodcast"] = JsonSerializer.SerializeToElement(prompt),
            ["nameSpeakerOne"] = JsonSerializer.SerializeToElement(nameSpeakerOne),
            ["nameSpeakerTwo"] = JsonSerializer.SerializeToElement(nameSpeakerTwo),
        };

        var scriptResult = await samplingService.GetPromptSample(
                     serviceProvider,
                     requestContext.Server,
                     "create-podcast-script-from-outline",
                     scriptPromptArgs,
                     "gpt-5.1",
                     cancellationToken: cancellationToken
                 );

        var scriptResultValue = scriptResult.ToText() ?? string.Empty;

        return scriptResultValue.ToTextCallToolResponse();
    }

}

