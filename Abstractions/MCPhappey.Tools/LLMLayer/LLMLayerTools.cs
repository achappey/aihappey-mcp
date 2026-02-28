using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.LLMLayer;

public static class LLMLayerTools
{
    [Description("Search the web with LLMLayer across general, news, images, videos, shopping, and scholar search types.")]
    [McpServerTool(Title = "LLMLayer web search", Name = "llmlayer_web_search", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> LLMLayer_WebSearch(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Search query text.")] string query,
        [Description("Search type: general, news, images, videos, shopping, scholar.")] string search_type = "general",
        [Description("ISO location code, for example us, nl, uk, de.")] string location = "us",
        [Description("Optional recency filter for general/news: hour, day, week, month, year.")] string? recency = null,
        [Description("Optional domain filter list (general only). Prefix with '-' to exclude.")] string[]? domain_filter = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, _, _) = await requestContext.Server.TryElicit(
                new LLMLayerWebSearchRequest
                {
                    Query = query,
                    SearchType = search_type,
                    Location = location,
                    Recency = recency,
                    DomainFilter = domain_filter
                },
                cancellationToken);

            ArgumentException.ThrowIfNullOrWhiteSpace(typed.Query);

            var client = serviceProvider.GetRequiredService<LLMLayerClient>();
            var payload = new
            {
                query = typed.Query,
                search_type = typed.SearchType,
                location = typed.Location,
                recency = string.IsNullOrWhiteSpace(typed.Recency) ? null : typed.Recency,
                domain_filter = typed.DomainFilter is { Length: > 0 } ? typed.DomainFilter : null
            };

            var response = await client.PostJsonAsync("web_search", payload, cancellationToken) ?? new JsonObject();
            var resultCount = response["results"] is JsonArray arr ? arr.Count : 0;
            var summary = $"LLMLayer web search completed. Results={resultCount}.";

            var structured = new JsonObject
            {
                ["provider"] = "llmlayer",
                ["endpoint"] = "/api/v2/web_search",
                ["request"] = JsonSerializer.SerializeToNode(payload),
                ["response"] = response,
                ["resultCount"] = resultCount,
                ["cost"] = response["cost"]?.DeepClone()
            };

            return new CallToolResult
            {
                Meta = await requestContext.GetToolMeta(),
                StructuredContent = structured,
                Content = [summary.ToTextContentBlock()]
            };
        });

    [Description("Scrape a single URL with LLMLayer in markdown, html, and/or screenshot format.")]
    [McpServerTool(Title = "LLMLayer scrape", Name = "llmlayer_scrape", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> LLMLayer_Scrape(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("URL to scrape.")] string url,
        [Description("Output formats: markdown, html, screenshot.")] string[] formats,
        [Description("Extract only main content.")] bool main_content_only = false,
        [Description("Use advanced proxy for protected sites.")] bool advanced_proxy = false,
        [Description("Include images in markdown output.")] bool include_images = true,
        [Description("Include links in markdown output.")] bool include_links = true,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, _, _) = await requestContext.Server.TryElicit(
                new LLMLayerScrapeRequest
                {
                    Url = url,
                    Formats = formats,
                    MainContentOnly = main_content_only,
                    AdvancedProxy = advanced_proxy,
                    IncludeImages = include_images,
                    IncludeLinks = include_links
                },
                cancellationToken);

            ArgumentException.ThrowIfNullOrWhiteSpace(typed.Url);
            if (typed.Formats == null || typed.Formats.Length == 0)
                throw new ValidationException("At least one format is required.");

            var client = serviceProvider.GetRequiredService<LLMLayerClient>();
            var payload = new
            {
                url = typed.Url,
                formats = typed.Formats,
                main_content_only = typed.MainContentOnly,
                advanced_proxy = typed.AdvancedProxy,
                include_images = typed.IncludeImages,
                include_links = typed.IncludeLinks
            };

            var response = await client.PostJsonAsync("scrape", payload, cancellationToken) ?? new JsonObject();
            var summary = $"LLMLayer scrape completed. Url={typed.Url}. Formats={typed.Formats.Length}.";

            var structured = new JsonObject
            {
                ["provider"] = "llmlayer",
                ["endpoint"] = "/api/v2/scrape",
                ["request"] = JsonSerializer.SerializeToNode(payload),
                ["response"] = response,
                ["cost"] = response["cost"]?.DeepClone()
            };

            return new CallToolResult
            {
                Meta = await requestContext.GetToolMeta(),
                StructuredContent = structured,
                Content = [summary.ToTextContentBlock()]
            };
        });

    [Description("Map a website and return discovered links with titles.")]
    [McpServerTool(Title = "LLMLayer map", Name = "llmlayer_map", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> LLMLayer_Map(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Website URL to map.")] string url,
        [Description("Ignore sitemap and force crawling.")] bool ignoreSitemap = false,
        [Description("Include subdomains.")] bool includeSubdomains = false,
        [Description("Optional search keyword filter.")] string? search = null,
        [Description("Maximum number of URLs to return.")] int limit = 5000,
        [Description("Timeout in milliseconds.")] int timeout = 45000,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, _, _) = await requestContext.Server.TryElicit(
                new LLMLayerMapRequest
                {
                    Url = url,
                    IgnoreSitemap = ignoreSitemap,
                    IncludeSubdomains = includeSubdomains,
                    Search = search,
                    Limit = limit,
                    Timeout = timeout
                },
                cancellationToken);

            ArgumentException.ThrowIfNullOrWhiteSpace(typed.Url);

            var client = serviceProvider.GetRequiredService<LLMLayerClient>();
            var payload = new
            {
                url = typed.Url,
                ignoreSitemap = typed.IgnoreSitemap,
                includeSubdomains = typed.IncludeSubdomains,
                search = string.IsNullOrWhiteSpace(typed.Search) ? null : typed.Search,
                limit = typed.Limit,
                timeout = typed.Timeout
            };

            var response = await client.PostJsonAsync("map", payload, cancellationToken) ?? new JsonObject();
            var linkCount = response["links"] is JsonArray arr ? arr.Count : 0;
            var summary = $"LLMLayer map completed. Url={typed.Url}. Links={linkCount}.";

            var structured = new JsonObject
            {
                ["provider"] = "llmlayer",
                ["endpoint"] = "/api/v2/map",
                ["request"] = JsonSerializer.SerializeToNode(payload),
                ["response"] = response,
                ["linkCount"] = linkCount,
                ["cost"] = response["cost"]?.DeepClone()
            };

            return new CallToolResult
            {
                Meta = await requestContext.GetToolMeta(),
                StructuredContent = structured,
                Content = [summary.ToTextContentBlock()]
            };
        });

    [Description("Extract transcript from a public YouTube video.")]
    [McpServerTool(Title = "LLMLayer YouTube transcript", Name = "llmlayer_youtube_transcript", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> LLMLayer_YouTube_Transcript(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("YouTube URL.")] string url,
        [Description("Optional language code such as en, es, fr.")] string? language = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, _, _) = await requestContext.Server.TryElicit(
                new LLMLayerYouTubeTranscriptRequest
                {
                    Url = url,
                    Language = language
                },
                cancellationToken);

            ArgumentException.ThrowIfNullOrWhiteSpace(typed.Url);

            var client = serviceProvider.GetRequiredService<LLMLayerClient>();
            var payload = new
            {
                url = typed.Url,
                language = string.IsNullOrWhiteSpace(typed.Language) ? null : typed.Language
            };

            var response = await client.PostJsonAsync("youtube_transcript", payload, cancellationToken) ?? new JsonObject();
            var transcriptLength = response["transcript"]?.GetValue<string>()?.Length ?? 0;
            var summary = $"LLMLayer YouTube transcript completed. Characters={transcriptLength}.";

            var structured = new JsonObject
            {
                ["provider"] = "llmlayer",
                ["endpoint"] = "/api/v2/youtube_transcript",
                ["request"] = JsonSerializer.SerializeToNode(payload),
                ["response"] = response,
                ["transcriptLength"] = transcriptLength,
                ["cost"] = response["cost"]?.DeepClone()
            };

            return new CallToolResult
            {
                Meta = await requestContext.GetToolMeta(),
                StructuredContent = structured,
                Content = [summary.ToTextContentBlock()]
            };
        });

    [Description("Crawl multiple pages with LLMLayer crawl and forward progress updates via MCP notifications.")]
    [McpServerTool(Title = "LLMLayer crawl", Name = "llmlayer_crawl", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> LLMLayer_Crawl(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Seed URL to crawl.")] string url,
        [Description("Maximum pages to crawl (1-100).")][Range(1, 100)] int max_pages = 25,
        [Description("Maximum depth from seed URL.")] int max_depth = 2,
        [Description("Total crawl timeout in seconds.")] double timeout = 60,
        [Description("Extract only main content.")] bool main_content_only = false,
        [Description("Use advanced proxy.")] bool advanced_proxy = false,
        [Description("Include subdomains.")] bool include_subdomains = false,
        [Description("Include images in markdown.")] bool include_images = true,
        [Description("Include links in markdown.")] bool include_links = true,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, _, _) = await requestContext.Server.TryElicit(
                new LLMLayerCrawlStreamRequest
                {
                    Url = url,
                    MaxPages = max_pages,
                    MaxDepth = max_depth,
                    Timeout = timeout,
                    MainContentOnly = main_content_only,
                    AdvancedProxy = advanced_proxy,
                    IncludeSubdomains = include_subdomains,
                    IncludeImages = include_images,
                    IncludeLinks = include_links
                },
                cancellationToken);

            ArgumentException.ThrowIfNullOrWhiteSpace(typed.Url);

            var payload = new
            {
                url = typed.Url,
                max_pages = typed.MaxPages,
                max_depth = typed.MaxDepth,
                timeout = typed.Timeout,
                main_content_only = typed.MainContentOnly,
                advanced_proxy = typed.AdvancedProxy,
                include_subdomains = typed.IncludeSubdomains,
                include_images = typed.IncludeImages,
                include_links = typed.IncludeLinks,
                formats = new[] { "markdown" }
            };

            var client = serviceProvider.GetRequiredService<LLMLayerClient>();
            using var response = await client.PostSseAsync("crawl_stream", payload, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var rawErr = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new Exception($"LLMLayer crawl_stream failed ({(int)response.StatusCode}): {rawErr}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            var frames = new JsonArray();
            var pageFrames = 0;
            var successPages = 0;
            var failedPages = 0;
            JsonNode? usageFrame = null;
            JsonNode? doneFrame = null;

            int? progressCounter = 0;
            var dataBuffer = new StringBuilder();

            while (!reader.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync(cancellationToken) ?? string.Empty;

                if (string.IsNullOrWhiteSpace(line))
                {
                    progressCounter = await FlushCrawlSseData(
                        dataBuffer,
                        frames,
                        requestContext,
                        progressCounter,
                        pageRef: () => pageFrames++,
                        successRef: () => successPages++,
                        failRef: () => failedPages++,
                        usageRef: n => usageFrame = n,
                        doneRef: n => doneFrame = n,
                        cancellationToken);
                    continue;
                }

                if (line.StartsWith(':'))
                    continue;

                if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    var data = line.Length >= 5 ? line[5..].TrimStart() : string.Empty;
                    dataBuffer.AppendLine(data);
                }
            }

            _ = await FlushCrawlSseData(
                dataBuffer,
                frames,
                requestContext,
                progressCounter,
                pageRef: () => pageFrames++,
                successRef: () => successPages++,
                failRef: () => failedPages++,
                usageRef: n => usageFrame = n,
                doneRef: n => doneFrame = n,
                cancellationToken);

            var summary = $"LLMLayer crawl_stream completed. Pages={pageFrames}, Success={successPages}, Failed={failedPages}.";
            var structured = new JsonObject
            {
                ["provider"] = "llmlayer",
                ["endpoint"] = "/api/v2/crawl_stream",
                ["request"] = JsonSerializer.SerializeToNode(payload),
                ["frames"] = frames,
                ["pageFrames"] = pageFrames,
                ["successPages"] = successPages,
                ["failedPages"] = failedPages,
                ["usage"] = usageFrame,
                ["done"] = doneFrame,
                ["cost"] = usageFrame?["cost"]?.DeepClone()
            };

            return new CallToolResult
            {
                Meta = await requestContext.GetToolMeta(),
                StructuredContent = structured,
                Content = [summary.ToTextContentBlock()]
            };
        });

    private static async Task<int?> FlushCrawlSseData(
        StringBuilder dataBuffer,
        JsonArray frames,
        RequestContext<CallToolRequestParams> requestContext,
        int? progressCounter,
        Action pageRef,
        Action successRef,
        Action failRef,
        Action<JsonNode?> usageRef,
        Action<JsonNode?> doneRef,
        CancellationToken cancellationToken)
    {
        var data = dataBuffer.ToString().Trim();
        dataBuffer.Clear();

        if (string.IsNullOrWhiteSpace(data))
            return progressCounter;

        JsonNode parsedNode;
        try
        {
            parsedNode = JsonNode.Parse(data) ?? JsonValue.Create(data)!;
        }
        catch
        {
            parsedNode = JsonValue.Create(data)!;
        }

        frames.Add(parsedNode.DeepClone());

        if (parsedNode is not JsonObject parsedObject)
        {
            await requestContext.Server.SendMessageNotificationAsync($"LLMLayer crawl_stream raw frame: {data}", LoggingLevel.Info, cancellationToken);
            return await requestContext.Server.SendProgressNotificationAsync(
                requestContext,
                progressCounter,
                "LLMLayer crawl_stream frame received",
                cancellationToken: cancellationToken);
        }

        var type = parsedObject["type"]?.GetValue<string>()?.Trim().ToLowerInvariant() ?? "unknown";
        string message;
        var level = LoggingLevel.Info;

        switch (type)
        {
            case "page":
                pageRef();
                var page = parsedObject["page"] as JsonObject;
                var pageSuccess = page?["success"]?.GetValue<bool>() == true;
                if (pageSuccess) successRef(); else failRef();
                var title = page?["title"]?.GetValue<string>() ?? "(untitled)";
                var finalUrl = page?["final_url"]?.GetValue<string>() ?? page?["requested_url"]?.GetValue<string>() ?? "n/a";
                message = $"LLMLayer crawl page {(pageSuccess ? "success" : "failed")}: {title} ({finalUrl})";
                level = pageSuccess ? LoggingLevel.Info : LoggingLevel.Warning;
                break;

            case "usage":
                usageRef(parsedObject.DeepClone());
                message = $"LLMLayer crawl usage: billed={parsedObject["billed_count"]?.GetValue<int>()}, cost={parsedObject["cost"]?.GetValue<double>()}";
                break;

            case "done":
                doneRef(parsedObject.DeepClone());
                message = $"LLMLayer crawl done in {parsedObject["response_time"]?.GetValue<string>()}s.";
                break;

            case "error":
                message = $"LLMLayer crawl fatal error: {parsedObject["error"]?.GetValue<string>()}";
                level = LoggingLevel.Error;
                break;

            default:
                message = $"LLMLayer crawl frame type={type}.";
                break;
        }

        await requestContext.Server.SendMessageNotificationAsync(message, level, cancellationToken);
        return await requestContext.Server.SendProgressNotificationAsync(
            requestContext,
            progressCounter,
            message,
            cancellationToken: cancellationToken);
    }
}

public sealed class LLMLayerWebSearchRequest
{
    [JsonPropertyName("query")]
    [Required]
    [Description("Search query text.")]
    public string Query { get; set; } = string.Empty;

    [JsonPropertyName("search_type")]
    [Description("Search type: general, news, images, videos, shopping, scholar.")]
    public string SearchType { get; set; } = "general";

    [JsonPropertyName("location")]
    [Description("Location country code.")]
    public string Location { get; set; } = "us";

    [JsonPropertyName("recency")]
    [Description("Optional recency filter for general/news.")]
    public string? Recency { get; set; }

    [JsonPropertyName("domain_filter")]
    [Description("Optional list of domain filters.")]
    public string[]? DomainFilter { get; set; }
}

public sealed class LLMLayerScrapeRequest
{
    [JsonPropertyName("url")]
    [Required]
    [Description("URL to scrape.")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("formats")]
    [Required]
    [Description("Output formats list.")]
    public string[] Formats { get; set; } = ["markdown"];

    [JsonPropertyName("main_content_only")]
    [Description("Extract only main content.")]
    public bool MainContentOnly { get; set; }

    [JsonPropertyName("advanced_proxy")]
    [Description("Enable advanced proxy.")]
    public bool AdvancedProxy { get; set; }

    [JsonPropertyName("include_images")]
    [Description("Include images in markdown output.")]
    public bool IncludeImages { get; set; } = true;

    [JsonPropertyName("include_links")]
    [Description("Include links in markdown output.")]
    public bool IncludeLinks { get; set; } = true;
}

public sealed class LLMLayerMapRequest
{
    [JsonPropertyName("url")]
    [Required]
    [Description("Website URL to map.")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("ignoreSitemap")]
    [Description("Ignore sitemap and force crawling.")]
    public bool IgnoreSitemap { get; set; }

    [JsonPropertyName("includeSubdomains")]
    [Description("Include subdomains.")]
    public bool IncludeSubdomains { get; set; }

    [JsonPropertyName("search")]
    [Description("Optional search keyword filter.")]
    public string? Search { get; set; }

    [JsonPropertyName("limit")]
    [Range(1, 30000)]
    [Description("Maximum number of URLs.")]
    public int Limit { get; set; } = 5000;

    [JsonPropertyName("timeout")]
    [Range(1000, 300000)]
    [Description("Timeout in milliseconds.")]
    public int Timeout { get; set; } = 45000;
}

public sealed class LLMLayerYouTubeTranscriptRequest
{
    [JsonPropertyName("url")]
    [Required]
    [Description("YouTube URL.")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("language")]
    [Description("Optional transcript language code.")]
    public string? Language { get; set; }
}

public sealed class LLMLayerCrawlStreamRequest
{
    [JsonPropertyName("url")]
    [Required]
    [Description("Seed URL.")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("max_pages")]
    [Range(1, 100)]
    [Description("Maximum pages to crawl.")]
    public int MaxPages { get; set; } = 25;

    [JsonPropertyName("max_depth")]
    [Range(1, 10)]
    [Description("Maximum depth.")]
    public int MaxDepth { get; set; } = 2;

    [JsonPropertyName("timeout")]
    [Range(1, 3600)]
    [Description("Crawl timeout in seconds.")]
    public double Timeout { get; set; } = 60;

    [JsonPropertyName("main_content_only")]
    [Description("Extract only main content.")]
    public bool MainContentOnly { get; set; }

    [JsonPropertyName("advanced_proxy")]
    [Description("Enable advanced proxy.")]
    public bool AdvancedProxy { get; set; }

    [JsonPropertyName("include_subdomains")]
    [Description("Include subdomains.")]
    public bool IncludeSubdomains { get; set; }

    [JsonPropertyName("include_images")]
    [Description("Include images in markdown output.")]
    public bool IncludeImages { get; set; } = true;

    [JsonPropertyName("include_links")]
    [Description("Include links in markdown output.")]
    public bool IncludeLinks { get; set; } = true;
}

