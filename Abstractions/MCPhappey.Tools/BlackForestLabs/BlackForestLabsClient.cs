using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.BlackForestLabs;

/// <summary>Native BFL HTTP only. Task IDs and credentials never leave the current call.</summary>
public sealed class BlackForestLabsClient(HttpClient httpClient, IServiceProvider services)
{
    public const string BaseUrl = "https://api.bfl.ai/";
    internal TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);
    internal TimeSpan PollTimeout { get; set; } = TimeSpan.FromMinutes(20);

    public async Task<JsonObject> GenerateAsync(McpServer server, string model, JsonObject payload,
        CancellationToken cancellationToken = default)
    {
        if (model is not ("flux-3-image" or "flux-3-image-klein" or "flux-3-video"))
            throw new ArgumentException("Unsupported native Black Forest Labs model.");
        var config = services.GetServerConfig(server) ?? throw new InvalidOperationException("Unknown MCP server.");
        if (config.Server.ServerInfo.Name is not ("BlackForestLabs-Images" or "BlackForestLabs-Videos"))
            throw new UnauthorizedAccessException("BFL credentials belong to a different MCP server.");
        var key = ProviderApiKeyResolver.Resolve(services, config, "bfl_api_key",
            () => services.GetService<BlackForestLabsSettings>()?.ApiKey);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(PollTimeout);
        try
        {
            var submitted = await SendAsync(HttpMethod.Post, new Uri(BaseUrl + "v1/" + model), key, payload, deadline.Token);
            var id = submitted["id"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("BFL returned no generation task ID.");
            var url = submitted["polling_url"]?.GetValue<string>()
                ?? BaseUrl + "v1/get_result?id=" + Uri.EscapeDataString(id);
            var pollingUri = ValidatePollingUri(url);
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                var result = await SendAsync(HttpMethod.Get, pollingUri, key, null, deadline.Token);
                var status = result["status"]?.GetValue<string>()?.Trim();
                if (string.IsNullOrWhiteSpace(status))
                    throw new InvalidOperationException("BFL polling returned no status.");
                if (status.Equals("Ready", StringComparison.OrdinalIgnoreCase))
                    return result["result"] as JsonObject ?? throw new InvalidOperationException("BFL completed without a result.");
                if (IsFailure(status)) throw new InvalidOperationException($"BFL generation failed: {status}.");
                // The FLUX harness reports phases such as Reasoning in addition to
                // queue/render phases. Only terminal states end polling; new progress
                // labels must not fail an already-submitted (and billable) generation.
                // The call-local deadline still bounds unrecognized progress states.
                await Task.Delay(PollInterval, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new TimeoutException("BFL generation exceeded the polling timeout. No task or session is retained by the MCP server.");
        }
    }

    internal static Uri ValidatePollingUri(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Port != 443
            || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/v1/get_result"
            || !Regex.IsMatch(uri.IdnHost, @"^api(?:\.[a-z0-9-]+)?\.bfl\.ai$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new UnauthorizedAccessException("BFL polling credentials are restricted to trusted HTTPS BFL result endpoints.");
        return uri;
    }

    private async Task<JsonObject> SendAsync(HttpMethod method, Uri uri, string key, JsonObject? body, CancellationToken token)
    {
        using var message = new HttpRequestMessage(method, uri);
        message.Headers.Add("x-key", key);
        if (body is not null) message.Content = JsonContent.Create(body);
        using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token);
        var text = await response.Content.ReadAsStringAsync(token);
        JsonObject? result = null;
        try { result = JsonNode.Parse(text) as JsonObject; }
        catch (System.Text.Json.JsonException) { }
        // A terminal BFL task failure can be returned with HTTP 503.
        if (result?["status"]?.GetValue<string>() is { } status && IsFailure(status))
            throw new InvalidOperationException($"BFL generation failed: {status}.");
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"BFL API request failed ({(int)response.StatusCode}). Check provider credentials, model inputs, quota, and approved safety levels.", null, response.StatusCode);
        return result ?? throw new InvalidOperationException("BFL returned an invalid JSON response.");
    }

    private static bool IsFailure(string status) => status.Trim().ToLowerInvariant() is
        "error" or "request moderated" or "content moderated" or "task not found"
        or "failed" or "cancelled" or "canceled" or "expired";

    public async Task<MediaOutput> DownloadOutputAsync(string value, string filename, string defaultMime,
        CancellationToken cancellationToken = default)
    {
        BinaryData data;
        var mime = defaultMime;
        if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            // Deliberately use a separate, credential-free, DNS-pinned downloader for signed CDN links.
            var file = await services.GetRequiredService<PublicFileDownloader>().DownloadAsync(value, cancellationToken, maxBytes: null);
            data = file.Contents;
            if (file.MimeType.StartsWith(defaultMime.Split('/')[0] + "/", StringComparison.OrdinalIgnoreCase)) mime = file.MimeType;
        }
        else
        {
            if (value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var comma = value.IndexOf(',');
                if (comma < 0 || !value[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("BFL returned an invalid media data URL.");
                mime = value[5..comma].Split(';')[0];
                value = value[(comma + 1)..];
            }
            data = BinaryData.FromBytes(Convert.FromBase64String(value));
        }
        if (defaultMime.StartsWith("image/"))
        {
            var bytes = data.ToArray();
            mime = bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71 }) ? "image/png" : "image/jpeg";
            PublicImageDownloader.ValidateImage(bytes, mime);
            filename = Path.GetFileNameWithoutExtension(filename) + (mime == "image/png" ? ".png" : ".jpg");
        }
        else if (defaultMime.StartsWith("video/") && !mime.StartsWith("video/"))
            throw new InvalidOperationException("BFL returned non-video media for a video result.");
        return new MediaOutput(filename, data, mime);
    }
}
