using System.ComponentModel;
using System.Text.Json;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Google.Interactions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

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
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var promptService = serviceProvider.GetRequiredService<PromptService>();
        var interactions = serviceProvider.GetRequiredService<GoogleInteractionsClient>();
        var contents = await downloadService.ScrapeContentAsync(serviceProvider,
            requestContext.Server, inputFileUrl, cancellationToken);
        var promptArgs = new Dictionary<string, JsonElement>
        {
            ["documentContent"] = JsonSerializer.SerializeToElement(string.Join("\n\n", contents.GetTextFiles()
                .Select(t => t.Contents.ToString()))),
            ["inputAroundPodcast"] = JsonSerializer.SerializeToElement(prompt)
        };

        var outlinePrompt = await promptService.GetServerPrompt(serviceProvider, requestContext.Server,
            "create-podcast-outline-from-document", promptArgs, cancellationToken: cancellationToken);
        var resultValue = await interactions.CreateTextInteractionAsync(new GoogleInteractionRequest
        {
            Model = GoogleInteractionsClient.DefaultModel,
            Input = string.Join("\n\n", outlinePrompt.Messages.Select(message => message.Content.ToString()))
                is var text ? GoogleInteractionInput.Text(text) : throw new InvalidOperationException()
        }, cancellationToken);

        var scriptPromptArgs = new Dictionary<string, JsonElement>
        {
            ["podcastOutline"] = JsonSerializer.SerializeToElement(resultValue),
            ["inputAroundPodcast"] = JsonSerializer.SerializeToElement(prompt),
            ["nameSpeakerOne"] = JsonSerializer.SerializeToElement(nameSpeakerOne),
            ["nameSpeakerTwo"] = JsonSerializer.SerializeToElement(nameSpeakerTwo),
        };

        var scriptPrompt = await promptService.GetServerPrompt(serviceProvider, requestContext.Server,
            "create-podcast-script-from-outline", scriptPromptArgs, cancellationToken: cancellationToken);
        var scriptResultValue = await interactions.CreateTextInteractionAsync(new GoogleInteractionRequest
        {
            Model = GoogleInteractionsClient.DefaultModel,
            Input = GoogleInteractionInput.Text(string.Join("\n\n", scriptPrompt.Messages.Select(message => message.Content.ToString())))
        }, cancellationToken);

        return scriptResultValue.ToTextCallToolResponse();
    }

}

