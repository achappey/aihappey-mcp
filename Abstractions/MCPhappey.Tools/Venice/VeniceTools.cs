using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Venice;

public static class VeniceTools
{
    private const string SearchPath = "augment/search";
    private const string ScrapePath = "augment/scrape";
    private const string TextParserPath = "augment/text-parser";
    private const string CryptoRpcPathPrefix = "crypto/rpc";

    [Description("Search the web with Venice Augment and return structured search results. Uses Brave by default, with optional Google provider.")]
    [McpServerTool(Title = "Venice web search", Name = "venice_web_search", ReadOnly = true, Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> Venice_Web_Search(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Search query, between 1 and 400 characters.")] string query,
        [Description("Maximum number of results to return. Allowed range: 1-20. Default: 10.")] int limit = 10,
        [Description("Search provider to use. Allowed values: brave, google. Defaults to brave.")] string search_provider = "brave",
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new VeniceWebSearchRequest
                {
                    Query = NormalizeRequired(query, "query"),
                    Limit = NormalizeLimit(limit),
                    SearchProvider = NormalizeSearchProvider(search_provider)
                },
                cancellationToken);

            if (notAccepted != null)
                return notAccepted;

            if (typed == null)
                return "No input data provided".ToErrorCallToolResponse();

            var body = new JsonObject
            {
                ["query"] = NormalizeRequired(typed.Query, "query"),
                ["limit"] = NormalizeLimit(typed.Limit),
                ["search_provider"] = NormalizeSearchProvider(typed.SearchProvider)
            };

            var parsed = await SendJsonAsync(serviceProvider, SearchPath, body, cancellationToken);
            var summary = BuildSearchSummary(parsed);

            return ToStructuredResult(parsed, summary);
        });

    [Description("Scrape a public web page with Venice Augment and return markdown content plus structured metadata.")]
    [McpServerTool(Title = "Venice web scrape", Name = "venice_web_scrape", ReadOnly = true, Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> Venice_Web_Scrape(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Public HTTP or HTTPS URL to scrape.")] string url,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new VeniceWebScrapeRequest
                {
                    Url = NormalizeUrl(url, "url")
                },
                cancellationToken);

            if (notAccepted != null)
                return notAccepted;

            if (typed == null)
                return "No input data provided".ToErrorCallToolResponse();

            var body = new JsonObject
            {
                ["url"] = NormalizeUrl(typed.Url, "url")
            };

            var parsed = await SendJsonAsync(serviceProvider, ScrapePath, body, cancellationToken);
            var markdown = parsed["content"]?.GetValue<string>();

            return ToStructuredResult(parsed, string.IsNullOrWhiteSpace(markdown)
                ? parsed.ToJsonString()
                : markdown);
        });

    [Description("Extract text from a document using Venice Augment text parser. Supports SharePoint, OneDrive, and HTTP fileUrl inputs for PDF, DOCX, PPTX, XLSX, and plain text files.")]
    [McpServerTool(Title = "Venice text parser", Name = "venice_text_parser", ReadOnly = true, Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> Venice_Text_Parser(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("File URL of the document to parse. Secure SharePoint and OneDrive links are supported.")] string fileUrl,
        [Description("Response format from Venice. Allowed values: json, text. Default: json.")] string response_format = "json",
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new VeniceTextParserRequest
                {
                    FileUrl = NormalizeRequired(fileUrl, "fileUrl"),
                    ResponseFormat = NormalizeTextParserResponseFormat(response_format)
                },
                cancellationToken);

            if (notAccepted != null)
                return notAccepted;

            if (typed == null)
                return "No input data provided".ToErrorCallToolResponse();

            var sourceFile = await DownloadSingleFileAsync(serviceProvider, requestContext, typed.FileUrl, cancellationToken);
            var responseFormat = NormalizeTextParserResponseFormat(typed.ResponseFormat);
            using var form = BuildTextParserForm(sourceFile, responseFormat);

            using var client = serviceProvider.CreateVeniceClient(responseFormat == "text" ? MimeTypes.PlainText : MimeTypes.Json);
            using var req = new HttpRequestMessage(HttpMethod.Post, TextParserPath)
            {
                Content = form
            };

            using var resp = await client.SendAsync(req, cancellationToken);
            var raw = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"Venice text parser failed ({(int)resp.StatusCode}): {raw}");

            if (responseFormat == "text")
            {
                return new CallToolResult
                {
                    StructuredContent = new JsonObject
                    {
                        ["provider"] = "venice",
                        ["type"] = "text_parser",
                        ["source"] = typed.FileUrl,
                        ["response_format"] = "text",
                        ["text"] = raw
                    }.ToJsonElement(),
                    Content = [raw.ToTextContentBlock()]
                };
            }

            var parsed = ParseJsonObject(raw, "Venice text parser");
            var text = parsed["text"]?.GetValue<string>();

            return ToStructuredResult(parsed, string.IsNullOrWhiteSpace(text)
                ? parsed.ToJsonString()
                : text);
        });

    [Description("Proxy a JSON-RPC 2.0 request to a supported Venice crypto RPC network. Pass one JSON-RPC object or an array of up to 100 objects as a JSON string.")]
    [McpServerTool(Title = "Venice crypto RPC", Name = "venice_crypto_rpc", ReadOnly = false, Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> Venice_Crypto_Rpc(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Venice crypto RPC network slug, for example ethereum-mainnet, base-mainnet, polygon-mainnet, or starknet-mainnet. Read the Venice crypto networks resource for the live list.")] string network,
        [Description("JSON-RPC request payload as JSON string. Use a single object or an array of up to 100 JSON-RPC objects.")] string requestJson,
        [Description("Optional idempotency key for safe retries. Allowed characters: letters, numbers, underscore, hyphen. Max length: 255.")] string? idempotency_key = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new VeniceCryptoRpcRequest
                {
                    Network = NormalizeRequired(network, "network"),
                    RequestJson = NormalizeRequired(requestJson, "requestJson"),
                    IdempotencyKey = NormalizeOptional(idempotency_key)
                },
                cancellationToken);

            if (notAccepted != null)
                return notAccepted;

            if (typed == null)
                return "No input data provided".ToErrorCallToolResponse();

            var payload = ParseJsonRpcPayload(typed.RequestJson);
            var networkSlug = NormalizeNetwork(typed.Network);
            var idempotencyKey = NormalizeIdempotencyKey(typed.IdempotencyKey);

            using var client = serviceProvider.CreateVeniceClient(MimeTypes.Json);
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{CryptoRpcPathPrefix}/{networkSlug}")
            {
                Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, MimeTypes.Json)
            };

            if (idempotencyKey is not null)
                req.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);

            using var resp = await client.SendAsync(req, cancellationToken);
            var raw = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"Venice crypto RPC failed ({(int)resp.StatusCode}): {raw}");

            var parsed = TryParseJson(raw);
            var structured = new JsonObject
            {
                ["provider"] = "venice",
                ["type"] = "crypto_rpc",
                ["network"] = networkSlug,
                ["idempotency_key"] = idempotencyKey,
                ["headers"] = ExtractCryptoHeaders(resp),
                ["response"] = parsed
            };

            return ToStructuredResult(structured, parsed.ToJsonString());
        });

    private static async Task<JsonObject> SendJsonAsync(
        IServiceProvider serviceProvider,
        string path,
        JsonObject body,
        CancellationToken cancellationToken)
    {
        using var client = serviceProvider.CreateVeniceClient(MimeTypes.Json);
        using var req = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, MimeTypes.Json)
        };

        using var resp = await client.SendAsync(req, cancellationToken);
        var raw = await resp.Content.ReadAsStringAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Venice {path} failed ({(int)resp.StatusCode}): {raw}");

        return ParseJsonObject(raw, $"Venice {path}");
    }

    private static async Task<FileItem> DownloadSingleFileAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string fileUrl,
        CancellationToken cancellationToken)
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        return files.FirstOrDefault()
               ?? throw new InvalidOperationException("Failed to download document content from fileUrl.");
    }

    private static MultipartFormDataContent BuildTextParserForm(FileItem sourceFile, string responseFormat)
    {
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(sourceFile.Contents.ToArray());

        if (!string.IsNullOrWhiteSpace(sourceFile.MimeType))
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(sourceFile.MimeType);

        form.Add(fileContent, "file", string.IsNullOrWhiteSpace(sourceFile.Filename) ? "document.bin" : sourceFile.Filename);
        form.Add(new StringContent(responseFormat), "response_format");

        return form;
    }

    private static JsonObject ParseJsonObject(string raw, string operation)
        => JsonNode.Parse(raw)?.AsObject()
           ?? throw new InvalidOperationException($"{operation} returned invalid JSON.");

    private static JsonNode TryParseJson(string raw)
    {
        try
        {
            return JsonNode.Parse(raw) ?? JsonValue.Create(raw)!;
        }
        catch
        {
            return JsonValue.Create(raw)!;
        }
    }

    private static JsonNode ParseJsonRpcPayload(string requestJson)
    {
        var payload = JsonNode.Parse(requestJson)
                      ?? throw new ValidationException("requestJson must be a valid JSON-RPC object or array.");

        if (payload is JsonArray array)
        {
            if (array.Count is < 1 or > 100)
                throw new ValidationException("requestJson batch must contain between 1 and 100 JSON-RPC objects.");

            foreach (var item in array)
                ValidateJsonRpcObject(item);

            return payload;
        }

        ValidateJsonRpcObject(payload);
        return payload;
    }

    private static void ValidateJsonRpcObject(JsonNode? node)
    {
        if (node is not JsonObject obj)
            throw new ValidationException("Each JSON-RPC request must be a JSON object.");

        var method = obj["method"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(method))
            throw new ValidationException("Each JSON-RPC request must include a non-empty method property.");
    }

    private static JsonObject ExtractCryptoHeaders(HttpResponseMessage response)
    {
        var headers = new JsonObject();
        AddHeaderIfPresent(headers, response, "X-Balance-Remaining");
        AddHeaderIfPresent(headers, response, "X-Venice-RPC-Credits");
        AddHeaderIfPresent(headers, response, "X-Venice-RPC-Cost-USD");
        AddHeaderIfPresent(headers, response, "X-Request-ID");
        AddHeaderIfPresent(headers, response, "Idempotent-Replayed");
        return headers;
    }

    private static void AddHeaderIfPresent(JsonObject headers, HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out var values))
            headers[name] = string.Join(",", values);
    }

    private static CallToolResult ToStructuredResult(JsonNode structured, string? text = null)
    {
        var result = new CallToolResult
        {
            StructuredContent = structured.ToJsonElement()
        };

        if (!string.IsNullOrWhiteSpace(text))
            result.Content = [text.ToTextContentBlock()];

        return result;
    }

    private static string BuildSearchSummary(JsonObject parsed)
    {
        var query = parsed["query"]?.GetValue<string>() ?? "";
        var results = parsed["results"] as JsonArray;

        if (results is null || results.Count == 0)
            return parsed.ToJsonString();

        var builder = new StringBuilder();
        builder.AppendLine($"Query: {query}");
        builder.AppendLine();

        foreach (var item in results.OfType<JsonObject>())
        {
            var title = item["title"]?.GetValue<string>();
            var url = item["url"]?.GetValue<string>();
            var content = item["content"]?.GetValue<string>();
            var date = item["date"]?.GetValue<string>();

            builder.Append("- ");
            builder.Append(string.IsNullOrWhiteSpace(title) ? url : title);
            if (!string.IsNullOrWhiteSpace(url))
                builder.Append($" ({url})");
            if (!string.IsNullOrWhiteSpace(date))
                builder.Append($" — {date}");
            builder.AppendLine();

            if (!string.IsNullOrWhiteSpace(content))
                builder.AppendLine($"  {content}");
        }

        return builder.ToString();
    }

    private static string NormalizeRequired(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ValidationException($"{field} is required.");

        return value.Trim();
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int NormalizeLimit(int value)
        => value is >= 1 and <= 20
            ? value
            : throw new ValidationException("limit must be between 1 and 20.");

    private static string NormalizeSearchProvider(string? value)
    {
        var normalized = NormalizeRequired(value ?? "brave", "search_provider").ToLowerInvariant();
        return normalized is "brave" or "google"
            ? normalized
            : throw new ValidationException("search_provider must be one of: brave, google.");
    }

    private static string NormalizeTextParserResponseFormat(string? value)
    {
        var normalized = NormalizeRequired(value ?? "json", "response_format").ToLowerInvariant();
        return normalized is "json" or "text"
            ? normalized
            : throw new ValidationException("response_format must be one of: json, text.");
    }

    private static string NormalizeUrl(string value, string field)
    {
        var normalized = NormalizeRequired(value, field);
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ValidationException($"{field} must be an absolute HTTP or HTTPS URL.");

        return normalized;
    }

    private static string NormalizeNetwork(string value)
    {
        var normalized = NormalizeRequired(value, "network").ToLowerInvariant();
        return normalized.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
            ? normalized
            : throw new ValidationException("network may only contain letters, numbers, and hyphens.");
    }

    private static string? NormalizeIdempotencyKey(string? value)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null)
            return null;

        if (normalized.Length > 255 || normalized.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '-')))
            throw new ValidationException("idempotency_key must match [A-Za-z0-9_-]{1,255}.");

        return normalized;
    }
}

[Description("Please confirm the Venice web search request.")]
public sealed class VeniceWebSearchRequest
{
    [JsonPropertyName("query")]
    [Required]
    [StringLength(400, MinimumLength = 1)]
    [Description("Search query, between 1 and 400 characters.")]
    public string Query { get; set; } = default!;

    [JsonPropertyName("limit")]
    [Range(1, 20)]
    [Description("Maximum number of results to return. Allowed range: 1-20.")]
    public int Limit { get; set; } = 10;

    [JsonPropertyName("search_provider")]
    [Description("Search provider to use. Allowed values: brave, google.")]
    public string SearchProvider { get; set; } = "brave";
}

[Description("Please confirm the Venice web scrape request.")]
public sealed class VeniceWebScrapeRequest
{
    [JsonPropertyName("url")]
    [Required]
    [Url]
    [Description("Public HTTP or HTTPS URL to scrape.")]
    public string Url { get; set; } = default!;
}

[Description("Please confirm the Venice text parser request.")]
public sealed class VeniceTextParserRequest
{
    [JsonPropertyName("fileUrl")]
    [Required]
    [Description("File URL of the document to parse. Secure SharePoint and OneDrive links are supported.")]
    public string FileUrl { get; set; } = default!;

    [JsonPropertyName("response_format")]
    [Description("Response format from Venice. Allowed values: json, text.")]
    public string ResponseFormat { get; set; } = "json";
}

[Description("Please confirm the Venice crypto RPC request.")]
public sealed class VeniceCryptoRpcRequest
{
    [JsonPropertyName("network")]
    [Required]
    [Description("Venice crypto RPC network slug.")]
    public string Network { get; set; } = default!;

    [JsonPropertyName("requestJson")]
    [Required]
    [Description("JSON-RPC request payload as JSON string. Use a single object or an array of up to 100 JSON-RPC objects.")]
    public string RequestJson { get; set; } = default!;

    [JsonPropertyName("idempotency_key")]
    [Description("Optional idempotency key for safe retries.")]
    public string? IdempotencyKey { get; set; }
}

