using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.WebCrawlerAPI;

public static class WebCrawlerAPIFeeds
{
    [Description("Create a WebCrawlerAPI feed for monitoring website changes. Parameters are confirmed through elicitation before the feed is created.")]
    [McpServerTool(Title = "WebCrawlerAPI create feed", Name = "webcrawlerapi_feed_create", ReadOnly = false, Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> WebCrawlerAPI_Feed_Create(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Seed URL where the feed crawler starts.")] string url,
        [Description("Optional friendly feed name.")] string? name = null,
        [Description("Content format: markdown, cleaned, or html.")] string outputFormat = "markdown",
        [Description("Maximum number of pages to crawl per feed run.")][Range(1, 10000)] int itemsLimit = 10,
        [Description("Optional maximum crawl depth from 0 to 10.")][Range(0, 10)] int? maxDepth = null,
        [Description("Optional regular expression to whitelist URLs.")] string? whitelistRegexp = null,
        [Description("Optional regular expression to blacklist URLs.")] string? blacklistRegexp = null,
        [Description("Respect the website robots.txt file.")] bool respectRobotsTxt = false,
        [Description("Extract only the main article/blog content.")] bool mainContentOnly = false,
        [Description("Optional webhook URL for change notifications.")] string? webhookUrl = null,
        [Description("Include unavailable/error pages in feed output.")] bool includeErrors = false,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(new WebCrawlerAPIFeedCreateInput
            {
                Url = url,
                Name = name,
                OutputFormat = outputFormat,
                ItemsLimit = itemsLimit,
                MaxDepth = maxDepth,
                WhitelistRegexp = whitelistRegexp,
                BlacklistRegexp = blacklistRegexp,
                RespectRobotsTxt = respectRobotsTxt,
                MainContentOnly = mainContentOnly,
                WebhookUrl = webhookUrl,
                IncludeErrors = includeErrors
            }, cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            WebCrawlerAPIHelpers.ValidateRequired(typed.Url, nameof(url));

            var payload = new JsonObject
            {
                ["url"] = typed.Url!.Trim(),
                ["name"] = WebCrawlerAPIHelpers.NullIfWhiteSpace(typed.Name),
                ["output_format"] = WebCrawlerAPIHelpers.NullIfWhiteSpace(typed.OutputFormat) ?? "markdown",
                ["items_limit"] = typed.ItemsLimit,
                ["max_depth"] = typed.MaxDepth,
                ["whitelist_regexp"] = WebCrawlerAPIHelpers.NullIfWhiteSpace(typed.WhitelistRegexp),
                ["blacklist_regexp"] = WebCrawlerAPIHelpers.NullIfWhiteSpace(typed.BlacklistRegexp),
                ["respect_robots_txt"] = typed.RespectRobotsTxt,
                ["main_content_only"] = typed.MainContentOnly,
                ["webhook_url"] = WebCrawlerAPIHelpers.NullIfWhiteSpace(typed.WebhookUrl),
                ["include_errors"] = typed.IncludeErrors
            }.WithoutNulls();

            var client = serviceProvider.GetRequiredService<WebCrawlerAPIClient>();
            var response = await client.SendJsonAsync(HttpMethod.Post, "v2/feed", payload, cancellationToken);
            var feedId = response?["id"]?.GetValue<string>();

            var structured = WebCrawlerAPIHelpers.CreateStructuredResponse("/v2/feed", payload, response);
            structured["feedId"] = feedId;
            structured.WithoutNulls();

            return await WebCrawlerAPIHelpers.CreateToolResultAsync(
                requestContext,
                structured,
                $"WebCrawlerAPI feed created. FeedId={feedId ?? "unknown"}. Status={response?["status"]?.GetValue<string>() ?? "unknown"}.");
        });

    [Description("Pause a WebCrawlerAPI feed after confirmation through elicitation.")]
    [McpServerTool(Title = "WebCrawlerAPI pause feed", Name = "webcrawlerapi_feed_pause", ReadOnly = false, Destructive = false, OpenWorld = true)]
    public static Task<CallToolResult?> WebCrawlerAPI_Feed_Pause(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Feed ID to pause.")] string feedId,
        CancellationToken cancellationToken = default)
        => ManageFeedAsync(serviceProvider, requestContext, feedId, "pause", HttpMethod.Put, "paused", cancellationToken);

    [Description("Resume a paused WebCrawlerAPI feed after confirmation through elicitation.")]
    [McpServerTool(Title = "WebCrawlerAPI resume feed", Name = "webcrawlerapi_feed_resume", ReadOnly = false, Destructive = false, OpenWorld = true)]
    public static Task<CallToolResult?> WebCrawlerAPI_Feed_Resume(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Feed ID to resume.")] string feedId,
        CancellationToken cancellationToken = default)
        => ManageFeedAsync(serviceProvider, requestContext, feedId, "resume", HttpMethod.Put, "resumed", cancellationToken);

    [Description("Trigger an immediate WebCrawlerAPI feed run after confirmation through elicitation. If WebCrawlerAPI returns a job_id, the tool waits until that crawl job is done or error.")]
    [McpServerTool(Title = "WebCrawlerAPI run feed now", Name = "webcrawlerapi_feed_run", ReadOnly = false, Destructive = false, OpenWorld = true)]
    public static Task<CallToolResult?> WebCrawlerAPI_Feed_Run(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Feed ID to force-run.")] string feedId,
        CancellationToken cancellationToken = default)
        => ManageFeedAsync(serviceProvider, requestContext, feedId, "run", HttpMethod.Put, "run triggered", cancellationToken, waitForJob: true);

    [Description("Resend the webhook notification for the latest completed WebCrawlerAPI feed run after confirmation through elicitation.")]
    [McpServerTool(Title = "WebCrawlerAPI resend feed webhook", Name = "webcrawlerapi_feed_webhook_resend", ReadOnly = false, Destructive = false, OpenWorld = true)]
    public static Task<CallToolResult?> WebCrawlerAPI_Feed_Webhook_Resend(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Feed ID whose latest webhook should be resent.")] string feedId,
        CancellationToken cancellationToken = default)
        => ManageFeedAsync(serviceProvider, requestContext, feedId, "webhook/resend", HttpMethod.Post, "webhook resent", cancellationToken);

    [Description("Permanently cancel a WebCrawlerAPI feed after confirmation through elicitation.")]
    [McpServerTool(Title = "WebCrawlerAPI delete feed", Name = "webcrawlerapi_feed_delete", ReadOnly = false, Destructive = true, OpenWorld = true)]
    public static Task<CallToolResult?> WebCrawlerAPI_Feed_Delete(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Feed ID to permanently cancel.")] string feedId,
        CancellationToken cancellationToken = default)
        => ManageFeedAsync(serviceProvider, requestContext, feedId, null, HttpMethod.Delete, "deleted", cancellationToken);

    private static async Task<CallToolResult?> ManageFeedAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string feedId,
        string? actionPath,
        HttpMethod method,
        string actionDescription,
        CancellationToken cancellationToken,
        bool waitForJob = false)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(new WebCrawlerAPIFeedActionInput
            {
                FeedId = feedId,
                Action = actionDescription
            }, cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            WebCrawlerAPIHelpers.ValidateRequired(typed.FeedId, nameof(feedId));

            var relativeUrl = actionPath is null
                ? $"v2/feed/{Uri.EscapeDataString(typed.FeedId!)}"
                : $"v2/feed/{Uri.EscapeDataString(typed.FeedId!)}/{actionPath}";

            var endpoint = $"/{relativeUrl}";
            var request = new JsonObject
            {
                ["feed_id"] = typed.FeedId,
                ["action"] = actionDescription,
                ["method"] = method.Method
            };

            var client = serviceProvider.GetRequiredService<WebCrawlerAPIClient>();
            var response = await client.SendJsonAsync(method, relativeUrl, null, cancellationToken);
            JsonNode? finalJob = null;

            if (waitForJob)
            {
                var jobId = response?["job_id"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(jobId))
                    finalJob = await WebCrawlerAPIHelpers.PollJobUntilTerminalAsync(client, jobId, cancellationToken);
            }

            var structured = new JsonObject
            {
                ["provider"] = "webcrawlerapi",
                ["baseUrl"] = WebCrawlerAPIClient.BaseUrl,
                ["endpoint"] = endpoint,
                ["request"] = request,
                ["response"] = response?.DeepClone(),
                ["finalJob"] = finalJob?.DeepClone()
            }.WithoutNulls();

            var finalStatus = finalJob?["status"]?.GetValue<string>() ?? response?["status"]?.GetValue<string>() ?? "unknown";
            return await WebCrawlerAPIHelpers.CreateToolResultAsync(
                requestContext,
                structured,
                $"WebCrawlerAPI feed {actionDescription}. FeedId={typed.FeedId}. Status={finalStatus}.");
        });
}

public sealed class WebCrawlerAPIFeedCreateInput
{
    [Required]
    [JsonPropertyName("url")]
    [Description("Seed URL where the feed crawler starts.")]
    public string? Url { get; set; }

    [JsonPropertyName("name")]
    [Description("Optional friendly feed name.")]
    public string? Name { get; set; }

    [Required]
    [JsonPropertyName("outputFormat")]
    [Description("Content format: markdown, cleaned, or html.")]
    public string OutputFormat { get; set; } = "markdown";

    [Required]
    [JsonPropertyName("itemsLimit")]
    [Range(1, 10000)]
    [Description("Maximum number of pages to crawl per feed run.")]
    public int ItemsLimit { get; set; } = 10;

    [JsonPropertyName("maxDepth")]
    [Range(0, 10)]
    [Description("Optional maximum crawl depth from 0 to 10.")]
    public int? MaxDepth { get; set; }

    [JsonPropertyName("whitelistRegexp")]
    [Description("Optional regular expression to whitelist URLs.")]
    public string? WhitelistRegexp { get; set; }

    [JsonPropertyName("blacklistRegexp")]
    [Description("Optional regular expression to blacklist URLs.")]
    public string? BlacklistRegexp { get; set; }

    [JsonPropertyName("respectRobotsTxt")]
    [Description("Respect the website robots.txt file.")]
    public bool RespectRobotsTxt { get; set; }

    [JsonPropertyName("mainContentOnly")]
    [Description("Extract only the main article/blog content.")]
    public bool MainContentOnly { get; set; }

    [JsonPropertyName("webhookUrl")]
    [Description("Optional webhook URL for change notifications.")]
    public string? WebhookUrl { get; set; }

    [JsonPropertyName("includeErrors")]
    [Description("Include unavailable/error pages in feed output.")]
    public bool IncludeErrors { get; set; }
}

public sealed class WebCrawlerAPIFeedActionInput
{
    [Required]
    [JsonPropertyName("feedId")]
    [Description("Feed ID to manage.")]
    public string? FeedId { get; set; }

    [Required]
    [JsonPropertyName("action")]
    [Description("Requested feed action to confirm.")]
    public string Action { get; set; } = string.Empty;
}

