using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.QuiverAI;

public static class QuiverAIService
{
    private const string GenerationsPath = "v1/svgs/generations";
    private const string VectorizationsPath = "v1/svgs/vectorizations";

    [Description("Generate SVG from text and optional supportFileUrl references (SharePoint/OneDrive/HTTPS), stream SSE progress to MCP notifications, upload SVG outputs, and return only resource link blocks.")]
    [McpServerTool(Title = "Generate SVG with QuiverAI", Name = "quiverai_svgs_generate", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> QuiverAI_Svgs_Generate(
        [Description("Prompt describing the SVG to generate.")] string prompt,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Model identifier. Default: arrow-preview.")] string model = "arrow-preview",
        [Description("Optional extra styling guidance.")] string? instructions = null,
        [Description("Optional reference image URL(s), comma-separated. Supports SharePoint/OneDrive/HTTPS.")] string? supportFileUrl = null,
        [Description("Number of outputs to generate (1-16). Default: 1.")] int n = 1,
        [Description("Nucleus sampling probability (0-1). Default: 1.")] double top_p = 1,
        [Description("Maximum output tokens (1-131072). Default: 4096.")] int max_output_tokens = 4096,
        [Description("Sampling temperature (0-2). Default: 1.")] double temperature = 1,
        [Description("Presence penalty (-2 to 2). Default: 0.")] double? presence_penalty = 0,
        [Description("Use SSE streaming mode. Default: true.")] bool stream = true,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
    {
        ValidatePromptRequest(prompt, model, n, top_p, max_output_tokens, temperature, presence_penalty);

        var references = await ResolveReferencesAsync(serviceProvider, requestContext, supportFileUrl, cancellationToken);
        var request = BuildGenerateRequestJson(model, prompt, instructions, references, n, top_p, max_output_tokens, temperature, presence_penalty, stream);

        var client = serviceProvider.GetRequiredService<QuiverAIClient>();
        var outputs = stream
            ? await ExecuteStreamingAndCollectSvgsAsync(client, requestContext, GenerationsPath, request, cancellationToken)
            : await ExecuteNonStreamingAndCollectSvgsAsync(client, GenerationsPath, request, cancellationToken);

        var links = await UploadSvgOutputsAsync(outputs, filename, serviceProvider, requestContext, cancellationToken);
        return links.ToResourceLinkCallToolResponse();
    });

    [Description("Vectorize an image from fileUrl (SharePoint/OneDrive/HTTPS) into SVG, stream SSE progress to MCP notifications, upload SVG outputs, and return only resource link blocks.")]
    [McpServerTool(Title = "Vectorize image to SVG with QuiverAI", Name = "quiverai_svgs_vectorize", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> QuiverAI_Svgs_Vectorize(
        [Description("Image URL to vectorize. Supports SharePoint/OneDrive/HTTPS.")] string fileUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Model identifier. Default: arrow-preview.")] string model = "arrow-preview",
        [Description("Number of outputs to generate (1-16). Default: 1.")] int n = 1,
        [Description("Nucleus sampling probability (0-1). Default: 1.")] double top_p = 1,
        [Description("Maximum output tokens (1-131072). Default: 4096.")] int max_output_tokens = 4096,
        [Description("Sampling temperature (0-2). Default: 1.")] double temperature = 1,
        [Description("Presence penalty (-2 to 2). Default: 0.")] double? presence_penalty = 0,
        [Description("Auto-crop image before vectorization. Default: false.")] bool auto_crop = false,
        [Description("Optional target square size in pixels (128-4096).")]
        int? target_size = null,
        [Description("Use SSE streaming mode. Default: true.")] bool stream = true,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
    {
        if (string.IsNullOrWhiteSpace(fileUrl))
            throw new ValidationException("fileUrl is required.");

        ValidatePromptRequest("vectorize", model, n, top_p, max_output_tokens, temperature, presence_penalty);

        if (target_size is < 128 or > 4096)
            throw new ValidationException("target_size must be between 128 and 4096 when provided.");

        var imageRef = await ResolveSingleImageReferenceAsync(serviceProvider, requestContext, fileUrl, cancellationToken);
        var request = BuildVectorizeRequestJson(model, imageRef, n, top_p, max_output_tokens, temperature, presence_penalty, auto_crop, target_size, stream);

        var client = serviceProvider.GetRequiredService<QuiverAIClient>();
        var outputs = stream
            ? await ExecuteStreamingAndCollectSvgsAsync(client, requestContext, VectorizationsPath, request, cancellationToken)
            : await ExecuteNonStreamingAndCollectSvgsAsync(client, VectorizationsPath, request, cancellationToken);

        var links = await UploadSvgOutputsAsync(outputs, filename, serviceProvider, requestContext, cancellationToken);
        return links.ToResourceLinkCallToolResponse();
    });

    private static void ValidatePromptRequest(
        string prompt,
        string model,
        int n,
        double topP,
        int maxOutputTokens,
        double temperature,
        double? presencePenalty)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ValidationException("prompt is required.");
        if (string.IsNullOrWhiteSpace(model))
            throw new ValidationException("model is required.");
        if (n is < 1 or > 16)
            throw new ValidationException("n must be between 1 and 16.");
        if (topP is < 0 or > 1)
            throw new ValidationException("top_p must be between 0 and 1.");
        if (maxOutputTokens is < 1 or > 131072)
            throw new ValidationException("max_output_tokens must be between 1 and 131072.");
        if (temperature is < 0 or > 2)
            throw new ValidationException("temperature must be between 0 and 2.");
        if (presencePenalty is < -2 or > 2)
            throw new ValidationException("presence_penalty must be between -2 and 2 when provided.");
    }

    private static JsonObject BuildGenerateRequestJson(
        string model,
        string prompt,
        string? instructions,
        JsonArray references,
        int n,
        double topP,
        int maxOutputTokens,
        double temperature,
        double? presencePenalty,
        bool stream)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["prompt"] = prompt,
            ["stream"] = stream,
            ["n"] = n,
            ["top_p"] = topP,
            ["max_output_tokens"] = maxOutputTokens,
            ["temperature"] = temperature
        };

        if (!string.IsNullOrWhiteSpace(instructions))
            body["instructions"] = instructions.Trim();

        if (presencePenalty.HasValue)
            body["presence_penalty"] = presencePenalty.Value;

        if (references.Count > 0)
            body["references"] = references;

        return body;
    }

    private static JsonObject BuildVectorizeRequestJson(
        string model,
        JsonObject image,
        int n,
        double topP,
        int maxOutputTokens,
        double temperature,
        double? presencePenalty,
        bool autoCrop,
        int? targetSize,
        bool stream)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["image"] = image,
            ["stream"] = stream,
            ["n"] = n,
            ["top_p"] = topP,
            ["max_output_tokens"] = maxOutputTokens,
            ["temperature"] = temperature,
            ["auto_crop"] = autoCrop
        };

        if (presencePenalty.HasValue)
            body["presence_penalty"] = presencePenalty.Value;

        if (targetSize.HasValue)
            body["target_size"] = targetSize.Value;

        return body;
    }

    private static async Task<JsonArray> ResolveReferencesAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string? supportFileUrl,
        CancellationToken cancellationToken)
    {
        var refs = new JsonArray();
        var urls = ParseCsvUrls(supportFileUrl);
        if (urls.Count == 0)
            return refs;

        if (urls.Count > 4)
            throw new ValidationException("supportFileUrl supports up to 4 comma-separated URLs.");

        foreach (var url in urls)
            refs.Add(await ResolveSingleImageReferenceAsync(serviceProvider, requestContext, url, cancellationToken));

        return refs;
    }

    private static async Task<JsonObject> ResolveSingleImageReferenceAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string fileUrl,
        CancellationToken cancellationToken)
    {
        var download = serviceProvider.GetRequiredService<DownloadService>();
        var files = await download.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        var file = files.FirstOrDefault() ?? throw new InvalidOperationException("Unable to download content from provided fileUrl/supportFileUrl.");
        var b64 = Convert.ToBase64String(file.Contents.ToArray());

        return new JsonObject
        {
            ["base64"] = b64
        };
    }

    private static async Task<List<string>> ExecuteNonStreamingAndCollectSvgsAsync(
        QuiverAIClient client,
        string path,
        JsonObject request,
        CancellationToken cancellationToken)
    {
        var response = await client.PostJsonAsync(path, request, cancellationToken)
            ?? throw new InvalidOperationException("QuiverAI returned an empty response.");

        return ExtractSvgsFromJsonResponse(response);
    }

    private static async Task<List<string>> ExecuteStreamingAndCollectSvgsAsync(
        QuiverAIClient client,
        RequestContext<CallToolRequestParams> requestContext,
        string path,
        JsonObject request,
        CancellationToken cancellationToken)
    {
        using var httpRequest = client.CreateJsonPost(path, request);
        httpRequest.Headers.Accept.Clear();
        httpRequest.Headers.TryAddWithoutValidation("Accept", "text/event-stream");

        using var response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"QuiverAI streaming call failed ({(int)response.StatusCode}): {error}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        var byId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dataBuffer = new StringBuilder();
        string? currentEvent = null;
        int? progressCounter = 0;

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken) ?? string.Empty;

            if (string.IsNullOrWhiteSpace(line))
            {
                progressCounter = await FlushSseAsync(dataBuffer, currentEvent, byId, requestContext, progressCounter, cancellationToken);
                currentEvent = null;
                continue;
            }

            if (line.StartsWith(':'))
                continue;

            if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
            {
                currentEvent = line[6..].Trim();
                continue;
            }

            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var data = line.Length >= 5 ? line[5..].TrimStart() : string.Empty;
                dataBuffer.AppendLine(data);
            }
        }

        _ = await FlushSseAsync(dataBuffer, currentEvent, byId, requestContext, progressCounter, cancellationToken);

        var outputs = byId.Values.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        if (outputs.Count == 0)
            throw new InvalidOperationException("QuiverAI stream completed but no SVG content was received.");

        return outputs;
    }

    private static async Task<int?> FlushSseAsync(
        StringBuilder dataBuffer,
        string? eventName,
        IDictionary<string, string> byId,
        RequestContext<CallToolRequestParams> requestContext,
        int? progressCounter,
        CancellationToken cancellationToken)
    {
        var data = dataBuffer.ToString().Trim();
        dataBuffer.Clear();

        if (string.IsNullOrWhiteSpace(data))
            return progressCounter;

        if (string.Equals(data, "[DONE]", StringComparison.OrdinalIgnoreCase))
        {
            const string doneMsg = "QuiverAI stream completed ([DONE]).";
            await requestContext.Server.SendMessageNotificationAsync(doneMsg, LoggingLevel.Info, cancellationToken);
            return await requestContext.Server.SendProgressNotificationAsync(
                requestContext,
                progressCounter,
                doneMsg,
                cancellationToken: cancellationToken);
        }

        JsonObject? parsed = null;
        try
        {
            parsed = JsonNode.Parse(data) as JsonObject;
        }
        catch
        {
            // ignored on purpose; raw message still forwarded to notification stream.
        }

        if (parsed == null)
        {
            var rawMsg = $"QuiverAI SSE raw {(string.IsNullOrWhiteSpace(eventName) ? "event" : eventName)}: {data}";
            await requestContext.Server.SendMessageNotificationAsync(rawMsg, LoggingLevel.Info, cancellationToken);
            return await requestContext.Server.SendProgressNotificationAsync(
                requestContext,
                progressCounter,
                "QuiverAI SSE frame received",
                cancellationToken: cancellationToken);
        }

        var type = parsed["type"]?.GetValue<string>() ?? eventName ?? "event";
        var id = parsed["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N");
        var svg = parsed["svg"]?.GetValue<string>();
        var text = parsed["text"]?.GetValue<string>();

        if (!string.IsNullOrWhiteSpace(svg))
            byId[id] = svg;

        var msg = string.IsNullOrWhiteSpace(text)
            ? $"QuiverAI {type} (id={id}, svgLength={(svg?.Length ?? 0)})."
            : $"QuiverAI {type} (id={id}): {text}";

        await requestContext.Server.SendMessageNotificationAsync(msg, LoggingLevel.Info, cancellationToken);
        return await requestContext.Server.SendProgressNotificationAsync(
            requestContext,
            progressCounter,
            msg,
            cancellationToken: cancellationToken);
    }

    private static List<string> ExtractSvgsFromJsonResponse(JsonNode response)
    {
        var outputs = new List<string>();
        var data = response["data"]?.AsArray();
        if (data == null)
            return outputs;

        foreach (var item in data)
        {
            var svg = item?["svg"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(svg))
                outputs.Add(svg);
        }

        return outputs;
    }

    private static async Task<List<ResourceLinkBlock>> UploadSvgOutputsAsync(
        IReadOnlyCollection<string> svgs,
        string? filename,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
    {
        if (svgs.Count == 0)
            throw new InvalidOperationException("No SVG outputs to upload.");

        var links = new List<ResourceLinkBlock>();
        var baseName = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName();
        var i = 0;

        foreach (var svg in svgs.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            i++;
            var uploaded = await requestContext.Server.Upload(
                serviceProvider,
                $"{baseName}-{i}.svg",
                BinaryData.FromString(svg),
                cancellationToken);

            if (uploaded != null)
                links.Add(uploaded);
        }

        if (links.Count == 0)
            throw new InvalidOperationException("SVG generation succeeded but no output could be uploaded.");

        return links;
    }

    private static List<string> ParseCsvUrls(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : [.. value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Distinct(StringComparer.OrdinalIgnoreCase)];
}

