using System.ComponentModel;
using System.Text.Json.Nodes;
using MCPhappey.Common;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.OpenAI.Decisions;

public static class OpenAIDecisions
{
    [Description("Evaluate a condition against text, document, and/or image evidence using OpenAI Decisions. Returns the estimated probability that the condition is true, or a refusal; not a written answer. SharePoint folders trigger independent direct-child document evaluations with source-linked results/errors and MCP progress when a progress token is supplied.")]
    [McpServerTool(Title = "Check a condition", Name = "openai_decisions_predicate", ReadOnly = true, Destructive = false, OpenWorld = true)]
    public static Task<CallToolResult?> Predicate(
        [Description("Observable condition/question to evaluate against the input evidence.")] string instructions,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Inline text evidence, optional when document or image URLs supply the evidence.")] string? inputText = null,
        [Description("Document or SharePoint/OneDrive for Business folder URLs; delegated authorization is required for protected links. Folders include direct-child files only, evaluated independently with source-linked responses/errors. When any folder is present, explicit files are also independent; inline text/images are shared context. Without folders, documents remain shared evidence.")] string[]? documentUrls = null,
        [Description("Image file URLs to download and submit natively as inline base64 images. Protected SharePoint and OneDrive links are supported. Maximum 128 images.")] string[]? imageUrls = null,
        [Description("Optional unique name echoed in the answer.")] string? name = null,
        [Description("Image detail: auto, low, high, or original.")] string imageDetail = "auto",
        [Description("Optional opaque end-user safety identifier; not an authentication token or user identity.")] string? safetyIdentifier = null,
        CancellationToken cancellationToken = default)
        => ModelContextToolExtensions.WithExceptionCheck(async () => await ExecuteAsync(
            serviceProvider, requestContext, OpenAIDecisionsQuestions.Predicate(instructions, name),
            inputText, documentUrls, imageUrls, imageDetail, safetyIdentifier, cancellationToken));

    [Description("Choose one string value from a fixed set using OpenAI Decisions. Returns the selected value, per-option probabilities, and confidence, or a refusal. SharePoint folders evaluate direct-child documents independently with source-linked results/errors and token-aware MCP progress. Use the questions-URL tool for boolean/mixed-type choices or question batches.")]
    [McpServerTool(Title = "Select a choice", Name = "openai_decisions_choice", ReadOnly = true, Destructive = false, OpenWorld = true)]
    public static Task<CallToolResult?> Choice(
        [Description("Instructions defining how to select the appropriate option.")] string instructions,
        [Description("Between 2 and 255 distinct string choices. Include a fallback option if needed.")] string[] choiceValues,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional descriptions in the same order and count as choiceValues. Use an empty string for an option without a description.")] string[]? choiceDescriptions = null,
        [Description("Inline text evidence; optional when URLs supply evidence.")] string? inputText = null,
        [Description("Document or authenticated SharePoint/OneDrive for Business folder URLs for text extraction. Folders include direct-child files only; when any folder is present, every resolved document is evaluated independently with source-linked results/errors and shared inline text/images. Without folders, documents remain shared evidence.")] string[]? documentUrls = null,
        [Description("Image file URLs, including authenticated SharePoint and OneDrive links. Downloaded and sent inline; maximum 128 images.")] string[]? imageUrls = null,
        [Description("Optional unique name echoed in the answer.")] string? name = null,
        [Description("Image detail: auto, low, high, or original.")] string imageDetail = "auto",
        [Description("Optional opaque end-user safety identifier.")] string? safetyIdentifier = null,
        CancellationToken cancellationToken = default)
        => ModelContextToolExtensions.WithExceptionCheck(async () => await ExecuteAsync(
            serviceProvider, requestContext, OpenAIDecisionsQuestions.Choice(instructions, choiceValues, choiceDescriptions, name),
            inputText, documentUrls, imageUrls, imageDetail, safetyIdentifier, cancellationToken));

    [Description("Score evidence against ordered rubric levels using OpenAI Decisions. Returns the probability-weighted average of zero-based level indices, per-level probabilities, and confidence, or a refusal; not a written assessment. SharePoint folders evaluate direct-child documents independently with source-linked results/errors and token-aware MCP progress.")]
    [McpServerTool(Title = "Score against a rubric", Name = "openai_decisions_score", ReadOnly = true, Destructive = false, OpenWorld = true)]
    public static Task<CallToolResult?> Score(
        [Description("Instructions defining what to score against the rubric.")] string instructions,
        [Description("Ordered rubric labels, from lowest to highest. Numeric indices start at zero.")] string[] levelLabels,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional criteria for each level, in the same order and count as levelLabels. Use an empty string to omit a description.")] string[]? levelDescriptions = null,
        [Description("Inline text evidence; optional when URLs supply evidence.")] string? inputText = null,
        [Description("Document or authenticated SharePoint/OneDrive for Business folder URLs for text extraction. Folders include direct-child files only; when any folder is present, every resolved document is evaluated independently with source-linked results/errors and shared inline text/images. Without folders, documents remain shared evidence.")] string[]? documentUrls = null,
        [Description("Image file URLs, including authenticated SharePoint and OneDrive links. Downloaded and sent inline; maximum 128 images.")] string[]? imageUrls = null,
        [Description("Optional unique name echoed in the answer.")] string? name = null,
        [Description("Image detail: auto, low, high, or original.")] string imageDetail = "auto",
        [Description("Optional opaque end-user safety identifier.")] string? safetyIdentifier = null,
        CancellationToken cancellationToken = default)
        => ModelContextToolExtensions.WithExceptionCheck(async () => await ExecuteAsync(
            serviceProvider, requestContext, OpenAIDecisionsQuestions.Score(instructions, levelLabels, levelDescriptions, name),
            inputText, documentUrls, imageUrls, imageDetail, safetyIdentifier, cancellationToken));

    [Description("Download official OpenAI Decisions question definitions from a JSON URL and evaluate tool-supplied evidence. The file must be an object with a nonempty questions array of predicate, choice, and/or score definitions. Supports string/boolean choices. Only questions are imported; file input/model/settings are ignored. Returns the raw response, or independent source-linked responses/errors for direct-child documents in SharePoint folders, with token-aware MCP progress.")]
    [McpServerTool(Title = "Answer questions from a JSON URL", Name = "openai_decisions_questions_url", ReadOnly = true, Destructive = false, OpenWorld = true)]
    public static Task<CallToolResult?> QuestionsUrl(
        [Description("URL of the JSON questions file. Protected SharePoint and OneDrive file URLs are supported with delegated authorization.")] string questionsUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Inline text evidence; optional when URLs supply evidence. Input from the questions file is never used.")] string? inputText = null,
        [Description("Document or authenticated SharePoint/OneDrive for Business folder URLs for text extraction. Folders include direct-child files only; when any folder is present, every resolved document is evaluated independently with source-linked results/errors and shared inline text/images. Without folders, documents remain shared evidence.")] string[]? documentUrls = null,
        [Description("Image file URLs, including authenticated SharePoint and OneDrive links. Downloaded and sent inline; maximum 128 images.")] string[]? imageUrls = null,
        [Description("Image detail: auto, low, high, or original.")] string imageDetail = "auto",
        [Description("Optional opaque end-user safety identifier.")] string? safetyIdentifier = null,
        CancellationToken cancellationToken = default)
        => ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            var questions = await CreateInputBuilder(serviceProvider, requestContext)
                .LoadQuestionsAsync(questionsUrl, cancellationToken);
            return await ExecuteAsync(serviceProvider, requestContext, questions,
                inputText, documentUrls, imageUrls, imageDetail, safetyIdentifier, cancellationToken);
        });

    private static OpenAIDecisionsInputBuilder CreateInputBuilder(
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext)
    {
        var downloads = serviceProvider.GetRequiredService<DownloadService>();
        return new OpenAIDecisionsInputBuilder(
            (url, ct) => downloads.ScrapeContentAsync(serviceProvider, requestContext.Server, url, ct),
            (url, ct) => downloads.DownloadContentAsync(serviceProvider, requestContext.Server, url, ct));
    }

    private static async Task<CallToolResult> ExecuteAsync(
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        JsonArray questions, string? inputText, string[]? documentUrls, string[]? imageUrls,
        string imageDetail, string? safetyIdentifier, CancellationToken cancellationToken)
    {
        var client = serviceProvider.GetService<OpenAIDecisionsClient>()
            ?? throw new InvalidOperationException("OpenAI Decisions is not configured. Configure the api.openai.com Authorization domain header and register AddOpenAIDecisions.");
        async Task<OpenAIDecisionsDocuments> DiscoverAsync(string[] urls, CancellationToken ct)
        {
            if (!urls.Any(OpenAIDecisionsDocumentDiscovery.RequiresGraph))
                return new([], []);
            if (string.IsNullOrWhiteSpace(serviceProvider.GetService<HeaderProvider>()?.Bearer))
                throw new UnauthorizedAccessException("SharePoint document and folder discovery requires delegated authorization.");
            using var graph = await serviceProvider.GetOboGraphClient(requestContext.Server);
            return await new OpenAIDecisionsDocumentDiscovery(graph).DiscoverAsync(urls, ct);
        }

        var executor = new OpenAIDecisionsExecutor(
            CreateInputBuilder(serviceProvider, requestContext), DiscoverAsync, client.CreateAsync,
            async (progress, ct) =>
            {
                await requestContext.Server.SendProgressNotificationAsync(requestContext,
                    progress.Progress, progress.Message, progress.Total, ct);
            });
        var response = await executor.ExecuteAsync(questions, inputText, documentUrls, imageUrls,
            imageDetail, safetyIdentifier, cancellationToken);

        return new CallToolResult
        {
            StructuredContent = response,
            Content = [response.GetRawText().ToTextContentBlock()],
            Meta = await requestContext.GetToolMeta()
        };
    }
}
