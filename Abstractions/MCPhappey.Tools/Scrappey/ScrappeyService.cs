using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Scrappey;

public static class ScrappeyService
{
    [Description("Get the remaining Scrappey request balance.")]
    [McpServerTool(Title = "Scrappey remaining balance", Name = "scrappey_balance_get", ReadOnly = true, OpenWorld = true, UseStructuredContent = true)]
    public static async Task<CallToolResult?> Scrappey_Balance_Get(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var client = serviceProvider.GetRequiredService<ScrappeyClient>();
                var response = await client.GetBalanceAsync(cancellationToken) as JsonObject ?? [];

                var structured = new JsonObject
                {
                    ["provider"] = "scrappey",
                    ["baseUrl"] = "https://publisher.scrappey.com",
                    ["endpoint"] = "/api/v1/balance",
                    ["balance"] = response["balance"]?.DeepClone(),
                    ["response"] = response.DeepClone()
                };

                return new CallToolResult
                {
                    Meta = await requestContext.GetToolMeta(),
                    StructuredContent = structured,
                    Content = [$"Scrappey balance retrieved: {response["balance"]?.ToJsonString() ?? "unknown"}.".ToTextContentBlock()]
                };
            }));

    [Description("Submit a Scrappey POST browser request and return the structured response.")]
    [McpServerTool(Title = "Scrappey POST browser request", Name = "scrappey_request_post", ReadOnly = true, OpenWorld = true, UseStructuredContent = true)]
    public static async Task<CallToolResult?> Scrappey_Request_Post(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Target URL to open in Scrappey.")] string url,
        [Description("POST body content as plain text or JSON string.")] string? postData = null,
        [Description("Optional session id to reuse.")] string? session = null,
        [Description("Optional profile id for persistent browser identity.")] string? profileId = null,
        [Description("Optional proxy URL.")] string? proxy = null,
        [Description("Optional proxy country, e.g. UnitedStates.")] string? proxyCountry = null,
        [Description("Custom request headers as JSON object string.")] string? customHeadersJson = null,
        [Description("Cookies as JSON array or cookie object string.")] string? cookiesJson = null,
        [Description("Optional filter list as comma-separated values.")] string? filterCsv = null,
        [Description("Regex pattern or JSON array string of patterns.")] string? regexJson = null,
        [Description("When true, enable screenshot capture.")] bool screenshot = false,
        [Description("When true, upload screenshot and return public URL when supported.")] bool screenshotUpload = false,
        [Description("Viewport width for screenshots.")][Range(1, 10000)] int? screenshotWidth = null,
        [Description("Viewport height for screenshots.")][Range(1, 10000)] int? screenshotHeight = null,
        [Description("When true, request a full-page screenshot.")] bool fullPage = false,
        [Description("When true, use premium residential proxies.")] bool premiumProxy = false,
        [Description("When true, use mobile proxies.")] bool mobileProxy = false,
        [Description("When true, use datacenter proxies.")] bool datacenter = false,
        [Description("When true, assign a new proxy for the profile.")] bool forceNewProxy = false,
        [Description("When true, retry DataDome bypass.")] bool datadomeBypass = false,
        [Description("When true, enable autoparse when structureJson is supplied.")] bool autoparse = false,
        [Description("Autoparse structure as JSON object string.")] string? structureJson = null,
        [Description("Optional retry count.")][Range(0, 10)] int? retries = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(url);

                var payload = BuildBaseRequestPayload("request.post", url, session, profileId, proxy, proxyCountry,
                    customHeadersJson, cookiesJson, filterCsv, regexJson, screenshot, screenshotUpload,
                    screenshotWidth, screenshotHeight, fullPage, premiumProxy, mobileProxy, datacenter,
                    forceNewProxy, datadomeBypass, retries);

                if (!string.IsNullOrWhiteSpace(postData))
                    payload["postData"] = postData;

                if (autoparse)
                    payload["autoparse"] = true;

                if (!string.IsNullOrWhiteSpace(structureJson))
                    payload["structure"] = ParseJsonValue(structureJson, "structureJson");

                RemoveNulls(payload);

                var response = await serviceProvider.GetRequiredService<ScrappeyClient>().PostCommandAsync(payload, cancellationToken);
                return BuildToolResult(requestContext, payload, response, "Scrappey POST browser request completed.");
            }));

    [Description("Submit an advanced Scrappey POST browser automation request with browser actions and structured response.")]
    [McpServerTool(Title = "Scrappey advanced POST browser request", Name = "scrappey_request_post_advanced", ReadOnly = true, OpenWorld = true, UseStructuredContent = true)]
    public static async Task<CallToolResult?> Scrappey_Request_Post_Advanced(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Target URL to open in Scrappey.")] string url,
        [Description("POST body content as plain text or JSON string.")] string? postData = null,
        [Description("Browser actions as JSON array string.")] string? browserActionsJson = null,
        [Description("Optional session id to reuse.")] string? session = null,
        [Description("Optional profile id for persistent browser identity.")] string? profileId = null,
        [Description("Optional proxy URL.")] string? proxy = null,
        [Description("Optional proxy country, e.g. UnitedStates.")] string? proxyCountry = null,
        [Description("Custom request headers as JSON object string.")] string? customHeadersJson = null,
        [Description("Cookies as JSON array or cookie object string.")] string? cookiesJson = null,
        [Description("Abort-on-detection values as comma-separated list.")] string? abortOnDetectionCsv = null,
        [Description("Always-load values as comma-separated list.")] string? alwaysLoadCsv = null,
        [Description("Whitelisted domains as comma-separated list.")] string? whitelistedDomainsCsv = null,
        [Description("Optional filter list as comma-separated values.")] string? filterCsv = null,
        [Description("Regex pattern or JSON array string of patterns.")] string? regexJson = null,
        [Description("When true, abort only matching POST requests.")] bool abortOnPostRequest = false,
        [Description("When true, enable screenshot capture.")] bool screenshot = false,
        [Description("When true, upload screenshot and return public URL when supported.")] bool screenshotUpload = false,
        [Description("Viewport width for screenshots.")][Range(1, 10000)] int? screenshotWidth = null,
        [Description("Viewport height for screenshots.")][Range(1, 10000)] int? screenshotHeight = null,
        [Description("When true, request a full-page screenshot.")] bool fullPage = false,
        [Description("When true, use premium residential proxies.")] bool premiumProxy = false,
        [Description("When true, use mobile proxies.")] bool mobileProxy = false,
        [Description("When true, use datacenter proxies.")] bool datacenter = false,
        [Description("When true, assign a new proxy for the profile.")] bool forceNewProxy = false,
        [Description("When true, retry DataDome bypass.")] bool datadomeBypass = false,
        [Description("Optional captcha type such as turnstile, hcaptcha, or recaptchav2.")] string? captcha = null,
        [Description("When true, enable human-like mouse movements.")] bool mouseMovements = false,
        [Description("Optional retry count.")][Range(0, 10)] int? retries = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(url);

                var payload = BuildBaseRequestPayload("request.post", url, session, profileId, proxy, proxyCountry,
                    customHeadersJson, cookiesJson, filterCsv, regexJson, screenshot, screenshotUpload,
                    screenshotWidth, screenshotHeight, fullPage, premiumProxy, mobileProxy, datacenter,
                    forceNewProxy, datadomeBypass, retries);

                if (!string.IsNullOrWhiteSpace(postData))
                    payload["postData"] = postData;
                if (!string.IsNullOrWhiteSpace(browserActionsJson))
                    payload["browserActions"] = ParseJsonValue(browserActionsJson, "browserActionsJson");
                if (!string.IsNullOrWhiteSpace(abortOnDetectionCsv))
                    payload["abortOnDetection"] = ParseCsvArray(abortOnDetectionCsv);
                if (!string.IsNullOrWhiteSpace(alwaysLoadCsv))
                    payload["alwaysLoad"] = ParseCsvArray(alwaysLoadCsv);
                if (!string.IsNullOrWhiteSpace(whitelistedDomainsCsv))
                    payload["whitelistedDomains"] = ParseCsvArray(whitelistedDomainsCsv);
                if (abortOnPostRequest)
                    payload["abortOnPostRequest"] = true;
                if (!string.IsNullOrWhiteSpace(captcha))
                    payload["captcha"] = captcha;
                if (mouseMovements)
                    payload["mouseMovements"] = true;

                RemoveNulls(payload);

                var response = await serviceProvider.GetRequiredService<ScrappeyClient>().PostCommandAsync(payload, cancellationToken);
                return BuildToolResult(requestContext, payload, response, "Scrappey advanced POST browser request completed.");
            }));

    [Description("Create a persistent Scrappey browser session.")]
    [McpServerTool(Title = "Scrappey session create", Name = "scrappey_session_create", ReadOnly = true, OpenWorld = true, UseStructuredContent = true)]
    public static async Task<CallToolResult?> Scrappey_Session_Create(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Session identifier to create or reuse.")] string session,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(session);

                var payload = new JsonObject
                {
                    ["cmd"] = "sessions.create",
                    ["session"] = session
                };

                var response = await serviceProvider.GetRequiredService<ScrappeyClient>().PostCommandAsync(payload, cancellationToken);
                return BuildToolResult(requestContext, payload, response, $"Scrappey session created: {session}.");
            }));

    [Description("Destroy a Scrappey browser session.")]
    [McpServerTool(Title = "Scrappey session destroy", Name = "scrappey_session_destroy", ReadOnly = true, OpenWorld = true, UseStructuredContent = true)]
    public static async Task<CallToolResult?> Scrappey_Session_Destroy(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Session identifier to destroy.")] string session,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(session);

                var payload = new JsonObject
                {
                    ["cmd"] = "sessions.destroy",
                    ["session"] = session
                };

                var response = await serviceProvider.GetRequiredService<ScrappeyClient>().PostCommandAsync(payload, cancellationToken);
                return BuildToolResult(requestContext, payload, response, $"Scrappey session destroyed: {session}.");
            }));

    private static JsonObject BuildBaseRequestPayload(
        string cmd,
        string url,
        string? session,
        string? profileId,
        string? proxy,
        string? proxyCountry,
        string? customHeadersJson,
        string? cookiesJson,
        string? filterCsv,
        string? regexJson,
        bool screenshot,
        bool screenshotUpload,
        int? screenshotWidth,
        int? screenshotHeight,
        bool fullPage,
        bool premiumProxy,
        bool mobileProxy,
        bool datacenter,
        bool forceNewProxy,
        bool datadomeBypass,
        int? retries)
    {
        var payload = new JsonObject
        {
            ["cmd"] = cmd,
            ["url"] = url,
            ["session"] = session,
            ["profileId"] = profileId,
            ["proxy"] = proxy,
            ["proxyCountry"] = proxyCountry,
            ["customHeaders"] = string.IsNullOrWhiteSpace(customHeadersJson) ? null : ParseJsonValue(customHeadersJson, nameof(customHeadersJson)),
            ["cookies"] = string.IsNullOrWhiteSpace(cookiesJson) ? null : ParseJsonValue(cookiesJson, nameof(cookiesJson)),
            ["filter"] = string.IsNullOrWhiteSpace(filterCsv) ? null : ParseCsvArray(filterCsv),
            ["regex"] = string.IsNullOrWhiteSpace(regexJson) ? null : ParseJsonValue(regexJson, nameof(regexJson)),
            ["screenshot"] = screenshot,
            ["screenshotUpload"] = screenshotUpload,
            ["screenshotWidth"] = screenshotWidth.HasValue ? JsonValue.Create(screenshotWidth.Value) : null,
            ["screenshotHeight"] = screenshotHeight.HasValue ? JsonValue.Create(screenshotHeight.Value) : null,
            ["fullPage"] = fullPage,
            ["premiumProxy"] = premiumProxy,
            ["mobileProxy"] = mobileProxy,
            ["datacenter"] = datacenter,
            ["forceNewProxy"] = forceNewProxy,
            ["datadomeBypass"] = datadomeBypass,
            ["retries"] = retries.HasValue ? JsonValue.Create(retries.Value) : null
        };

        return payload;
    }

    private static CallToolResult BuildToolResult(
        RequestContext<CallToolRequestParams> requestContext,
        JsonObject requestPayload,
        ScrappeyResponse response,
        string summary)
    {
        var body = response.Body as JsonObject ?? [];
        var structured = new JsonObject
        {
            ["provider"] = "scrappey",
            ["baseUrl"] = "https://publisher.scrappey.com",
            ["endpoint"] = "/api/v1",
            ["request"] = requestPayload.DeepClone(),
            ["statusCode"] = response.StatusCode,
            ["response"] = body.DeepClone(),
            ["data"] = body["data"]?.DeepClone(),
            ["session"] = body["session"]?.DeepClone(),
            ["timeElapsed"] = body["timeElapsed"]?.DeepClone(),
            ["solution"] = body["solution"]?.DeepClone(),
            ["error"] = body["error"]?.DeepClone(),
            ["info"] = body["info"]?.DeepClone()
        };

        RemoveNulls(structured);

        return new CallToolResult
        {
            Meta = requestContext.GetToolMeta().GetAwaiter().GetResult(),
            StructuredContent = structured,
            Content = [summary.ToTextContentBlock()]
        };
    }

    private static JsonNode ParseJsonValue(string json, string parameterName)
    {
        try
        {
            return JsonNode.Parse(json)
                   ?? throw new ValidationException($"{parameterName} must contain valid JSON.");
        }
        catch (JsonException ex)
        {
            throw new ValidationException($"{parameterName} must contain valid JSON. {ex.Message}");
        }
    }

    private static JsonArray ParseCsvArray(string csv)
        => new(ParseCsv(csv).Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());

    private static List<string> ParseCsv(string csv)
        => csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static void RemoveNulls(JsonObject obj)
    {
        foreach (var property in obj.ToList())
        {
            if (property.Value is null)
            {
                obj.Remove(property.Key);
                continue;
            }

            if (property.Value is JsonObject child)
            {
                RemoveNulls(child);
                if (child.Count == 0)
                    obj.Remove(property.Key);
            }
        }
    }
}

