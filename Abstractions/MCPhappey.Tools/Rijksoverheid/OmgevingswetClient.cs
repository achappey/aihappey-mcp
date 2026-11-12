using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MCPhappey.Auth;
using MCPhappey.Common;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace MCPhappey.Tools.Rijksoverheid;

/// <summary>Request-local DSO credentials, constrained to the selected server's production API.</summary>
public sealed class OmgevingswetClient(IHttpClientFactory httpClientFactory)
{
    public const string ClientName = "Rijksoverheid-Omgevingswet";
    public const string InformationServer = "Rijksoverheid-Omgevingsinformatie";
    public const string PlansServer = "Rijksoverheid-RuimtelijkePlannen";
    public const string InformationBaseUrl = "https://service.omgevingswet.overheid.nl/publiek/omgevingsinformatie/api/ontsluiten/v2";
    public const string PlansBaseUrl = "https://ruimte.omgevingswet.overheid.nl/ruimtelijke-plannen/api/opvragen/v4";
    public const string RdNew = "http://www.opengis.net/def/crs/EPSG/0/28992";
    public const string CredentialVariable = "omgevingswet_api_key";

    public static bool Supports(ServerConfig config)
        => config.Server.ServerInfo.Name is InformationServer or PlansServer;

    public static string BaseUrl(ServerConfig config) => config.Server.ServerInfo.Name switch
    {
        InformationServer => InformationBaseUrl,
        PlansServer => PlansBaseUrl,
        _ => throw new ArgumentException("Unsupported Omgevingswet MCP server.", nameof(config))
    };

    public static Uri ValidateUri(ServerConfig config, string url)
    {
        var root = new Uri(BaseUrl(config) + "/");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443
            || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0
            || !root.IsBaseOf(uri))
            throw new ArgumentException("Resource URL must be within this server's production API base path.", nameof(url));

        // Reject encoded path separators and double-escaping, not just normalized ../ paths.
        foreach (var segment in uri.AbsolutePath.Split('/'))
        {
            var decoded = Uri.UnescapeDataString(segment);
            if (decoded is "." or ".." || decoded.IndexOfAny(['/', '\\', '%']) >= 0)
                throw new ArgumentException("Ambiguous API resource path.", nameof(url));
        }
        return uri;
    }

    public static string ValidateCrs(string crs)
        => crs is "epsg:4258" or "epsg:28992" ? crs
            : throw new ArgumentException("CRS must be epsg:4258 (ETRS89 longitude/latitude) or epsg:28992 (RD New metres).", nameof(crs));

    public async Task<FileItem> GetAsync(IServiceProvider services, ServerConfig config, string url,
        CancellationToken cancellationToken = default)
    {
        var uri = ValidateUri(config, url);
        var key = services.GetProviderCredential(config, CredentialVariable);
        var acceptCrs = config.Server.ServerInfo.Name == PlansServer ? ResourceCrs(services) : null;
        using var client = httpClientFactory.CreateClient(ClientName);
        for (var redirects = 0; ; redirects++)
        {
            using var request = CreateRequest(HttpMethod.Get, uri, key, acceptCrs);
            using var response = await client.SendAsync(request, cancellationToken);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                if (redirects >= 3 || response.Headers.Location is not { } location)
                    throw new HttpRequestException("Invalid or excessive API redirect.");
                uri = ValidateUri(config, new Uri(uri, location).AbsoluteUri);
                continue;
            }
            await EnsureSuccessAsync(response, key, cancellationToken);
            return new FileItem
            {
                Uri = url,
                Filename = Path.GetFileName(uri.AbsolutePath),
                MimeType = response.Content.Headers.ContentType?.MediaType ?? "application/json",
                Contents = BinaryData.FromBytes(await response.Content.ReadAsByteArrayAsync(cancellationToken))
            };
        }
    }

    public async Task<JsonNode> PostAsync(IServiceProvider services, ServerConfig config, string path,
        JsonObject body, OmgevingswetQuery? query = null, string? contentCrs = null, string? acceptCrs = null,
        CancellationToken cancellationToken = default)
    {
        var uri = ValidateUri(config, BaseUrl(config) + path + (query?.ToString() ?? ""));
        var key = services.GetProviderCredential(config, CredentialVariable);
        using var client = httpClientFactory.CreateClient(ClientName);
        using var request = CreateRequest(HttpMethod.Post, uri, key, acceptCrs);
        request.Content = JsonContent.Create(body);
        if (contentCrs is not null)
            request.Headers.Add("Content-Crs", contentCrs);
        using var response = await client.SendAsync(request, cancellationToken);
        // Never replay a POST or its API key at a redirect destination.
        await EnsureSuccessAsync(response, key, cancellationToken);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))
            ?? throw new InvalidOperationException("The API returned an empty JSON response.");
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, Uri uri, string key, string? acceptCrs)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Add("X-Api-Key", key);
        var mediaType = uri.AbsolutePath.EndsWith(".yaml", StringComparison.Ordinal) ? "application/yaml"
            : uri.AbsolutePath.EndsWith(".json", StringComparison.Ordinal)
                || uri.AbsolutePath.EndsWith("/crss", StringComparison.Ordinal)
                || uri.AbsolutePath.EndsWith("/info", StringComparison.Ordinal)
                || uri.AbsolutePath.EndsWith("/health", StringComparison.Ordinal)
                || uri.AbsolutePath.EndsWith("/app-info", StringComparison.Ordinal)
                || uri.AbsolutePath.EndsWith("/app-health", StringComparison.Ordinal)
                || uri.AbsolutePath.EndsWith("/onderwerpen/_zoek", StringComparison.Ordinal)
                ? "application/json" : "application/hal+json";
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(mediaType));
        if (acceptCrs is not null)
            request.Headers.Add("Accept-Crs", ValidateCrs(acceptCrs));
        return request;
    }

    private static string ResourceCrs(IServiceProvider services)
    {
        var access = services.GetService<IHttpContextAccessor>()?.HttpContext?.Items[typeof(ServerRequestAccess)] as ServerRequestAccess;
        // GetProviderCredential has already checked server/mode ownership of request headers.
        var headers = access?.Headers ?? services.GetService<HeaderProvider>()?.Headers;
        var crs = headers?.FirstOrDefault(pair => pair.Key.Equals("Accept-Crs", StringComparison.OrdinalIgnoreCase)).Value;
        return ValidateCrs(string.IsNullOrWhiteSpace(crs) ? "epsg:4258" : crs);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string key, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        detail = detail.Replace(key, "[redacted]", StringComparison.Ordinal);
        throw new HttpRequestException($"Omgevingswet API returned {(int)response.StatusCode} ({response.ReasonPhrase}): {detail}",
            null, response.StatusCode);
    }
}

/// <summary>OpenAPI form encoding: filter arrays are CSV, expand arrays use repeated parameters.</summary>
public sealed class OmgevingswetQuery
{
    private readonly List<KeyValuePair<string, string>> values = [];

    public OmgevingswetQuery Add(string name, string? value)
    {
        if (value is not null) values.Add(new(name, value));
        return this;
    }

    public OmgevingswetQuery Add(string name, int value) => Add(name, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public OmgevingswetQuery Add(string name, bool? value) => Add(name, value?.ToString().ToLowerInvariant());

    public OmgevingswetQuery Add(string name, IEnumerable<string>? items, bool explode = false)
    {
        if (items is null) return this;
        var array = items.ToArray();
        if (array.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException($"{name} cannot contain empty values.");
        if (explode)
            foreach (var item in array) Add(name, item);
        else if (array.Length > 0) Add(name, string.Join(',', array));
        return this;
    }

    public static OmgevingswetQuery Page(int page, int pageSize, bool information = false)
    {
        if (page < 0) throw new ArgumentOutOfRangeException(nameof(page), "Page numbering starts at zero.");
        if (information ? pageSize is < 1 or > 200 : pageSize is not (10 or 20 or 50 or 100))
            throw new ArgumentOutOfRangeException(nameof(pageSize), information ? "Size must be 1–200." : "Page size must be 10, 20, 50 or 100.");
        return new OmgevingswetQuery().Add("page", page).Add(information ? "size" : "pageSize", pageSize);
    }

    public override string ToString() => values.Count == 0 ? "" : "?" + string.Join('&', values.Select(pair =>
        $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
}
