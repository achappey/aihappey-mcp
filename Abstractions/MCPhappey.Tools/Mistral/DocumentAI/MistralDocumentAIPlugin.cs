using System.ComponentModel;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Microsoft.Extensions.DependencyInjection;
using MCPhappey.Core.Services;
using System.Text.Json;
using System.Text;
using System.Net.Http.Headers;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.Mistral.DocumentAI;

public static partial class MistralDocumentAIPlugin
{
    private const string OCR_MODEL = "mistral-ocr-latest";

    private const string BaseUrl = "https://api.mistral.ai/v1";

    [Description("Extract text content, images bboxes and metadata from a document using Mistral OCR AI")]
    [McpServerTool(Title = "Mistral document OCR", Name = "mistral_documentai_extract",
        IconSource = MistralConstants.ICON_SOURCE,
        Destructive = false, ReadOnly = true)]
    public static async Task<CallToolResult?> MistralDocumentAI_Extract(
        [Description("File url of the input document file. This tool can access secure SharePoint and OneDrive links.")] string fileUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        {
            var downloadService = serviceProvider.GetRequiredService<DownloadService>();
            var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
            var file = files.FirstOrDefault() ?? throw new Exception("File not found or empty.");
            var isImage = file.MimeType.StartsWith("image/");
            var httpClientFactory = serviceProvider.GetService<IHttpClientFactory>()
                ?? throw new InvalidOperationException("No IHttpClientFactory found in service provider");
            var mistralSettings = serviceProvider.GetService<MistralSettings>()
                ?? throw new InvalidOperationException("No MistralSettings found in service provider");

            var httpClient = httpClientFactory.CreateClient();

            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", mistralSettings.ApiKey);

            string dataUri = file.ToDataUri();

            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
            {
                w.WriteStartObject();

                w.WriteString("model", OCR_MODEL);

                w.WritePropertyName("document");
                w.WriteStartObject();
                if (isImage)
                {
                    w.WriteString("type", "image_url");
                    w.WriteString("image_url", dataUri);
                }
                else
                {
                    w.WriteString("type", "document_url");
                    w.WriteString("document_url", dataUri);
                }
                w.WriteEndObject();

                w.WriteBoolean("include_image_base64", true);
                w.WriteEndObject();
                w.Flush();
            }

            var json = Encoding.UTF8.GetString(ms.ToArray());
            var jsonContent = new StringContent(json, Encoding.UTF8, MimeTypes.Json);
            var response = await httpClient.PostAsync($"{BaseUrl}/ocr", jsonContent, cancellationToken);
            response.EnsureSuccessStatusCode();

            var ocrJson = await response.Content.ReadAsStringAsync(cancellationToken);

            // Return OCR JSON result
            return ocrJson.ToJsonCallToolResponse($"{BaseUrl}/ocr");
        });

    [Description("Run Mistral OCR with Document Annotations using a custom JSON schema.")]
    [McpServerTool(Title = "Mistral document annotation", Name = "mistral_documentai_annotate",
        IconSource = MistralConstants.ICON_SOURCE,
        Destructive = false, ReadOnly = true)]
    public static async Task<CallToolResult?> MistralDocumentAI_Annotate(
        [Description("File url of the input document (SharePoint/OneDrive secure links supported).")]
                string fileUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
          [Description(@"
            JSON for Mistral document_annotation_format.json_schema.

            Required: 
            - ""name"" (string)
            - ""schema"" (object)
            Optional:
            - ""strict"" (boolean)

            Rules:
            - Set ""additionalProperties"": false on the root object AND on every nested object.
            - Allowed keywords: type, properties, required, enum, const, additionalProperties
            - Disallowed keywords: format, nullable, $schema, $id, hints/instructions, patternProperties, anyOf, allOf, oneOf
            - Dates are plain ""string"" (no ""format"").
            - Optional fields = omit from ""required"".

            Example:
            { ""name"": ""InvoiceSummary"", ""strict"": true, ""schema"": { ""type"": ""object"", ""additionalProperties"": false, ""properties"": { ""company"": { ""type"": ""object"", ""additionalProperties"": false, ""properties"": { ""legal_name"": { ""type"": ""string"" }, ""vat_number"": { ""type"": ""string"" }, ""address"": { ""type"": ""string"" } }, ""required"": [ ""legal_name"" ] }, ""invoice"": { ""type"": ""object"", ""additionalProperties"": false, ""properties"": { ""number"": { ""type"": ""string"" }, ""date"": { ""type"": ""string"" } }, ""required"": [ ""number"" ] }, ""amounts"": { ""type"": ""object"", ""additionalProperties"": false, ""properties"": { ""currency"": { ""type"": ""string"" }, ""subtotal_excl_vat"": { ""type"": ""number"" }, ""vat_amount"": { ""type"": ""number"" }, ""total_incl_vat"": { ""type"": ""number"" } }, ""required"": [ ""subtotal_excl_vat"", ""total_incl_vat"" ] } }, ""required"": [ ""company"", ""invoice"", ""amounts"" ] } }
            ")]
            string documentJsonSchema,
        [Description("Comma-separated zero-based page indices (e.g. '0,1,2'). Optional.")]
                string? pagesCsv = null,
        [Description("Include base64 images in response (default: true).")]
                bool includeImageBase64 = true,
   CancellationToken cancellationToken = default) => await requestContext!.WithExceptionCheck(async () =>
    {
        if (string.IsNullOrWhiteSpace(fileUrl))
            throw new ArgumentException("fileUrl is required.");
        if (string.IsNullOrWhiteSpace(documentJsonSchema))
            throw new ArgumentException("documentJsonSchema is required and must contain a top-level 'schema'.");

        // Parse and validate provided json_schema (must include 'schema')
        JsonElement docSchemaElem;
        try
        {
            using var doc = JsonDocument.Parse(documentJsonSchema);
            var root = doc.RootElement;
            if (!root.TryGetProperty("schema", out _))
                throw new ArgumentException("documentJsonSchema must contain a top-level 'schema' object.");
            docSchemaElem = root.Clone();
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            throw new ArgumentException($"documentJsonSchema is invalid JSON: {ex.Message}");
        }

        // Optional pages
        int[]? pages = null;
        if (!string.IsNullOrWhiteSpace(pagesCsv))
        {
            try
            {
                pages = pagesCsv
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(int.Parse)
                    .Distinct()
                    .OrderBy(i => i)
                    .ToArray();
            }
            catch
            {
                throw new ArgumentException("pagesCsv must be a comma-separated list of integers (e.g., '0,1,2').");
            }
        }

        var downloadService = serviceProvider!.GetRequiredService<DownloadService>();
        var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext!.Server, fileUrl, cancellationToken);
        var file = files.FirstOrDefault() ?? throw new Exception("File not found or empty.");

        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var mistralSettings = serviceProvider.GetRequiredService<MistralSettings>();
        var httpClient = httpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mistralSettings.ApiKey);

        // data URI
        var dataUri = file.ToDataUri();
        var isImage = file.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

        // Build request JSON
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartObject();

            w.WriteString("model", OCR_MODEL);

            // document
            w.WritePropertyName("document");
            w.WriteStartObject();
            if (isImage)
            {
                w.WriteString("type", "image_url");
                w.WriteString("image_url", dataUri);
            }
            else
            {
                w.WriteString("type", "document_url");
                w.WriteString("document_url", dataUri);
            }
            w.WriteEndObject();

            // optional pages
            if (pages is { Length: > 0 })
            {
                w.WritePropertyName("pages");
                w.WriteStartArray();
                foreach (var p in pages) w.WriteNumberValue(p);
                w.WriteEndArray();
            }

            // document_annotation_format
            w.WritePropertyName("document_annotation_format");
            w.WriteStartObject();
            w.WriteString("type", "json_schema");
            w.WritePropertyName("json_schema");
            docSchemaElem.WriteTo(w); // expects object with { schema, [name], [strict] }
            w.WriteEndObject();

            w.WriteBoolean("include_image_base64", includeImageBase64);

            w.WriteEndObject();
            w.Flush();
        }

        var payload = Encoding.UTF8.GetString(ms.ToArray());
        var content = new StringContent(payload, Encoding.UTF8, MimeTypes.Json);

        using var response = await httpClient.PostAsync($"{BaseUrl}/ocr", content, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new Exception($"Mistral OCR API error {(int)response.StatusCode}: {response.ReasonPhrase}\n{responseText}");

        return responseText.ToJsonCallToolResponse($"{BaseUrl}/ocr");
    });
}
