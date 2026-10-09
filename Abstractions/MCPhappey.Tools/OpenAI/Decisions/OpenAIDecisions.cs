using System.ComponentModel;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.OpenAI.Decisions;

public static class OpenAIDecisions
{
    [Description("Evaluate a condition against shared text, document, and/or image evidence using OpenAI Decisions. Returns the estimated probability that the condition is true, or a refusal. This does not generate a written answer.")]
    [McpServerTool(Title = "Check a condition", Name = "openai_decisions_predicate", ReadOnly = true, Destructive = false, OpenWorld = true)]
    public static Task<CallToolResult?> Predicate(
        [Description("Observable condition/question to evaluate against the input evidence.")] string instructions,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Inline text evidence, optional when document or image URLs supply the evidence.")] string? inputText = null,
        [Description("Document URLs to extract as text through the RAG/resource pipeline. Protected SharePoint and OneDrive links are supported with delegated authorization.")] string[]? documentUrls = null,
        [Description("Image file URLs to download and submit natively as inline base64 images. Protected SharePoint and OneDrive links are supported. Maximum 128 images.")] string[]? imageUrls = null,
        [Description("Optional unique name echoed in the answer.")] string? name = null,
        [Description("Image detail: auto, low, high, or original.")] string imageDetail = "auto",
        [Description("Optional opaque end-user safety identifier; not an authentication token or user identity.")] string? safetyIdentifier = null,
        CancellationToken cancellationToken = default)
        => ModelContextToolExtensions.WithExceptionCheck(async () => await ExecuteAsync(
            serviceProvider, requestContext, OpenAIDecisionsQuestions.Predicate(instructions, name),
            inputText, documentUrls, imageUrls, imageDetail, safetyIdentifier, cancellationToken));

    [Description("Choose one string value from a fixed set using OpenAI Decisions and shared evidence. Returns the selected value, per-option probabilities, and confidence, or a refusal. Use the questions-URL tool for boolean/mixed-type choices or batches.")]
    [McpServerTool(Title = "Select a choice", Name = "openai_decisions_choice", ReadOnly = true, Destructive = false, OpenWorld = true)]
    public static Task<CallToolResult?> Choice(
        [Description("Instructions defining how to select the appropriate option.")] string instructions,
        [Description("Between 2 and 255 distinct string choices. Include a fallback option if needed.")] string[] choiceValues,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional descriptions in the same order and count as choiceValues. Use an empty string for an option without a description.")] string[]? choiceDescriptions = null,
        [Description("Inline text evidence; optional when URLs supply evidence.")] string? inputText = null,
        [Description("Document URLs for RAG/resource text extraction, including authenticated SharePoint and OneDrive links.")] string[]? documentUrls = null,
        [Description("Image file URLs, including authenticated SharePoint and OneDrive links. Downloaded and sent inline; maximum 128 images.")] string[]? imageUrls = null,
        [Description("Optional unique name echoed in the answer.")] string? name = null,
        [Description("Image detail: auto, low, high, or original.")] string imageDetail = "auto",
        [Description("Optional opaque end-user safety identifier.")] string? safetyIdentifier = null,
        CancellationToken cancellationToken = default)
        => ModelContextToolExtensions.WithExceptionCheck(async () => await ExecuteAsync(
            serviceProvider, requestContext, OpenAIDecisionsQuestions.Choice(instructions, choiceValues, choiceDescriptions, name),
            inputText, documentUrls, imageUrls, imageDetail, safetyIdentifier, cancellationToken));

    [Description("Score shared evidence against ordered rubric levels using OpenAI Decisions. Returns the probability-weighted average of zero-based level indices, per-level probabilities, and confidence, or a refusal. This is not a free-form written assessment.")]
    [McpServerTool(Title = "Score against a rubric", Name = "openai_decisions_score", ReadOnly = true, Destructive = false, OpenWorld = true)]
    public static Task<CallToolResult?> Score(
        [Description("Instructions defining what to score against the rubric.")] string instructions,
        [Description("Ordered rubric labels, from lowest to highest. Numeric indices start at zero.")] string[] levelLabels,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional criteria for each level, in the same order and count as levelLabels. Use an empty string to omit a description.")] string[]? levelDescriptions = null,
        [Description("Inline text evidence; optional when URLs supply evidence.")] string? inputText = null,
        [Description("Document URLs for RAG/resource text extraction, including authenticated SharePoint and OneDrive links.")] string[]? documentUrls = null,
        [Description("Image file URLs, including authenticated SharePoint and OneDrive links. Downloaded and sent inline; maximum 128 images.")] string[]? imageUrls = null,
        [Description("Optional unique name echoed in the answer.")] string? name = null,
        [Description("Image detail: auto, low, high, or original.")] string imageDetail = "auto",
        [Description("Optional opaque end-user safety identifier.")] string? safetyIdentifier = null,
        CancellationToken cancellationToken = default)
        => ModelContextToolExtensions.WithExceptionCheck(async () => await ExecuteAsync(
            serviceProvider, requestContext, OpenAIDecisionsQuestions.Score(instructions, levelLabels, levelDescriptions, name),
            inputText, documentUrls, imageUrls, imageDetail, safetyIdentifier, cancellationToken));

    [Description("Download official OpenAI Decisions question definitions from a JSON URL and evaluate them against tool-supplied evidence. The file must be a single object with a nonempty questions array of predicate, choice, and/or score definitions. Supports batches and string/boolean choices. Only questions are imported; file input/model/settings are ignored. Returns the complete raw decision response.")]
    [McpServerTool(Title = "Answer questions from a JSON URL", Name = "openai_decisions_questions_url", ReadOnly = true, Destructive = false, OpenWorld = true)]
    public static Task<CallToolResult?> QuestionsUrl(
        [Description("URL of the JSON questions file. Protected SharePoint and OneDrive file URLs are supported with delegated authorization.")] string questionsUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Inline text evidence; optional when URLs supply evidence. Input from the questions file is never used.")] string? inputText = null,
        [Description("Document URLs for RAG/resource text extraction, including authenticated SharePoint and OneDrive links.")] string[]? documentUrls = null,
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
        var input = await CreateInputBuilder(serviceProvider, requestContext)
            .BuildAsync(inputText, documentUrls, imageUrls, imageDetail, cancellationToken);
        var response = await client.CreateAsync(new OpenAIDecisionsRequest
        {
            Input = input,
            Questions = questions,
            SafetyIdentifier = safetyIdentifier
        }, cancellationToken);

        return new CallToolResult
        {
            StructuredContent = response,
            Content = [response.GetRawText().ToTextContentBlock()],
            Meta = await requestContext.GetToolMeta()
        };
    }
}
