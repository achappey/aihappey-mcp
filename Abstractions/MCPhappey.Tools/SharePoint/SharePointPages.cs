using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Auth.Extensions;
using MCPhappey.Auth.Models;
using MCPhappey.Common;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.SharePoint;

public static class SharePointPages
{

    [Description(
        "Set the promotion state of a SharePoint site page. " +
        "Use 0 for a normal page, 1 to promote on publish, or 2 for a news post.")]
    [McpServerTool(
        Title = "Set SharePoint page promotion state",
        Name = "sharepoint_set_page_promoted_state",
        Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> SharePointPages_SetPromotedState(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,

        [Description(
            "Exact SharePoint site URL containing the page, " +
            "e.g. https://contoso.sharepoint.com/sites/communications.")]
        string? siteUrl = null,

        [Description(
            "List item ID of the page in the Site Pages library.")]
        int? pageItemId = null,

        [Description(
            "Promotion state: 0 = normal page, 1 = promote on publish, 2 = news post.")]
        SharePointPagePromotedState? promotedState = null,

        [Description(
            "Title of the site pages list.")]
        string? sitePagesTitle = "Site Pages",

        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new SharePointSetPagePromotedStateInput
                {
                    SiteUrl = siteUrl,
                    SitePagesTitle = sitePagesTitle ?? "Site Pages",
                    PageItemId = pageItemId,
                    PromotedState = promotedState
                },
                cancellationToken);

            if (notAccepted != null)
                throw new Exception(JsonSerializer.Serialize(notAccepted));

            typed!.Validate();

            var tokenService = serviceProvider.GetRequiredService<HeaderProvider>();

            if (string.IsNullOrWhiteSpace(tokenService.Bearer))
                throw new UnauthorizedAccessException("Missing bearer token.");

            var httpClientFactory =
                serviceProvider.GetRequiredService<IHttpClientFactory>();

            var oauthSettings =
                serviceProvider.GetRequiredService<OAuthSettings>();

            var serverConfig =
                serviceProvider.GetServerConfig(requestContext.Server)
                ?? throw new InvalidOperationException(
                    "Could not resolve the MCP server configuration.");

            var siteUri = new Uri(typed.SiteUrl!);

            using var client = await httpClientFactory.GetOboHttpClient(
                tokenService.Bearer,
                siteUri.Host,
                serverConfig.Server,
                oauthSettings);

            var result = await SetPromotedStateAsync(
                client,
                typed.SiteUrl!,
                typed.PageItemId!.Value,
                typed.SitePagesTitle,
                typed.PromotedState!.Value,
                cancellationToken);

            return new
            {
                typed.SiteUrl,
                typed.PageItemId,
                ListTitle = sitePagesTitle,
                PromotedState = (int)typed.PromotedState.Value,
                PromotedStateName = typed.PromotedState.Value.ToString(),
                UpdateResult = result,
                Status = "SharePoint page promotion state updated successfully."
            };
        }));

    private static async Task<JsonElement?> SetPromotedStateAsync(
        HttpClient client,
        string siteUrl,
        int pageItemId,
        string sitePagesTitle,
        SharePointPagePromotedState promotedState,
        CancellationToken cancellationToken)
    {
        var url =
            $"{siteUrl.TrimEnd('/')}" +
            $"/_api/web/lists/getbytitle(@list)/items({pageItemId})" +
            "/ValidateUpdateListItem()" +
            $"?@list={ODataQuoted(sitePagesTitle)}";

        var payload = new
        {
            formValues = new[]
            {
                new
                {
                    FieldName = "PromotedState",
                    FieldValue = ((int)promotedState).ToString(
                        System.Globalization.CultureInfo.InvariantCulture)
                }
            },
            bNewDocumentUpdate = false
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url);

        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(
            request,
            cancellationToken);

        var raw = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"SharePoint failed to update PromotedState to " +
                $"{(int)promotedState} for Site Pages item {pageItemId} " +
                $"({(int)response.StatusCode} {response.ReasonPhrase}): {raw}",
                null,
                response.StatusCode);
        }

        if (string.IsNullOrWhiteSpace(raw))
            return null;

        using var document = JsonDocument.Parse(raw);
        return document.RootElement.Clone();
    }

    private static string ODataQuoted(string value)
    {
        var escaped = value.Replace(
            "'",
            "''",
            StringComparison.Ordinal);

        return Uri.EscapeDataString($"'{escaped}'");
    }

    [Description("Confirm the SharePoint page promotion-state update.")]
    public class SharePointSetPagePromotedStateInput
    {
        [JsonPropertyName("siteUrl")]
        [Required]
        [Description("Exact SharePoint site URL containing the page.")]
        public string? SiteUrl { get; set; }

        [JsonPropertyName("pageItemId")]
        [Required]
        [Description("List item ID of the page in the Site Pages library.")]
        public int? PageItemId { get; set; }

        [JsonPropertyName("promotedState")]
        [Required]
        [Description(
            "Promotion state: NormalPage, PromoteOnPublish, or NewsPost.")]
        public SharePointPagePromotedState? PromotedState { get; set; }

        [JsonPropertyName("sitePagesTitle")]
        [Required]
        [Description(
           "Title of the site pages list.")]
        public string SitePagesTitle { get; set; } = null!;

        public void Validate()
        {
            if (!Uri.TryCreate(
                    SiteUrl,
                    UriKind.Absolute,
                    out var siteUri))
            {
                throw new ValidationException(
                    "siteUrl must be an absolute URL.");
            }

            if (siteUri.Scheme != Uri.UriSchemeHttps)
            {
                throw new ValidationException(
                    "siteUrl must use HTTPS.");
            }

            if (PageItemId is null or <= 0)
            {
                throw new ValidationException(
                    "pageItemId must be a positive integer.");
            }

            if (PromotedState is null ||
                !Enum.IsDefined(PromotedState.Value))
            {
                throw new ValidationException(
                    "promotedState must be NormalPage, " +
                    "PromoteOnPublish, or NewsPost.");
            }
        }
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SharePointPagePromotedState
    {
        [Description("Normal SharePoint site page")]
        NormalPage = 0,

        [Description("Promote the page to news when it is published")]
        PromoteOnPublish = 1,

        [Description("Published SharePoint news post")]
        NewsPost = 2
    }

}