using MCPhappey.Auth.Models;
using MCPhappey.Common;
using MCPhappey.Common.Models;
using MCPhappey.Scrapers.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Server;

namespace MCPhappey.Scrapers.Microsoft;

/// <summary>
/// Scrapes a single Outlook-web message (outlook.office.com / outlook.office365.com).
/// Returns the message body and all <see cref="FileAttachment"/>s as <see cref="FileItem"/>s.
/// The url must contain the <c>ItemId</c> that Graph understands –
/// this is extracted via <see cref="OotlookExtensions.TryParse(string,out string?,out string?)"/>.
/// </summary>
public sealed class OutlookScraper(
    IHttpClientFactory httpClientFactory,
    ServerConfig serverConfig,
    OAuthSettings oAuthSettings) : IContentScraper
{
    #region IContentScraper

    public bool SupportsHost(ServerConfig currentConfig, string url) =>
        (new Uri(url).Host.EndsWith("outlook.office.com", StringComparison.OrdinalIgnoreCase) ||
         new Uri(url).Host.EndsWith("outlook.office365.com", StringComparison.OrdinalIgnoreCase)) &&
        serverConfig.Server.OBO?.ContainsKey(Common.Constants.Hosts.MicrosoftGraph) == true;



    public async Task<IEnumerable<FileItem>?> GetContentAsync(
        McpServer mcpServer,
        IServiceProvider serviceProvider,
        string url,
        CancellationToken cancellationToken = default)
    {
        // Acquire the current OBO token
        var tokenProvider = serviceProvider.GetService<HeaderProvider>();
        if (string.IsNullOrEmpty(tokenProvider?.Bearer))
            return null;

        // Resolve message identifiers from the url
        if (!OutlookExtensions.TryParse(url, out var mailbox, out var itemId) ||
            string.IsNullOrEmpty(itemId))
            return null;

        using var graph = await httpClientFactory.GetOboGraphClient(
            tokenProvider.Bearer,
            serverConfig.Server,
            oAuthSettings);

        var graphId = await graph.ToGraphRestIdAsync(itemId, mailbox, cancellationToken);
        // Query Parameters – expand attachments in a single call
        Message? message;
        if (string.IsNullOrEmpty(mailbox))
        {
            // fall-back to /me/ when no mailbox is embedded
            message = await graph.Me.Messages[graphId]
                .GetAsync(config =>
                {
                    config.QueryParameters.Expand = ["attachments"];
                }, cancellationToken);
        }
        else
        {
            message = await graph.Users[mailbox].Messages[graphId]
                .GetAsync(config =>
                {
                    config.QueryParameters.Expand = ["attachments"];
                }, cancellationToken);
        }

        if (message == null)
            return null;

        var result = new List<FileItem>();

        // 1. Message body
        var bodyItem = message.CreateBodyFileItem();
        if (bodyItem is not null)
            result.Add(bodyItem);

        // 2. Attachments
        if (message.Attachments != null)
        {
            foreach (var attachment in message.Attachments)
            {
                if (attachment is FileAttachment fileAttachment)
                {
                    var attachmentItem = await graph.CreateAttachmentFileItemAsync(
                        mailbox,
                        itemId,
                        fileAttachment,
                        cancellationToken);

                    if (attachmentItem is not null)
                        result.Add(attachmentItem);
                }
            }
        }

        return result;
    }

    #endregion

}
