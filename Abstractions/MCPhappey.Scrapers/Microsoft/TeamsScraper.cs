using System.Net.Mime;
using MCPhappey.Auth.Models;
using MCPhappey.Common;
using MCPhappey.Common.Constants;
using MCPhappey.Common.Models;
using MCPhappey.Scrapers.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Server;

namespace MCPhappey.Scrapers.Microsoft;

public class TeamsScraper(IHttpClientFactory httpClientFactory, ServerConfig serverConfig,
    OAuthSettings oAuthSettings) : IContentScraper
{
    public bool SupportsHost(ServerConfig currentConfig, string url)
        => new Uri(url).Host.Equals("teams.microsoft.com", StringComparison.OrdinalIgnoreCase)
            && serverConfig.Server.OBO?.ContainsKey(Hosts.MicrosoftGraph) == true;

    public async Task<IEnumerable<FileItem>?> GetContentAsync(McpServer mcpServer, IServiceProvider serviceProvider,
         string url, CancellationToken cancellationToken = default)
    {
        var tokenService = serviceProvider.GetService<HeaderProvider>();
        var scrapers = serviceProvider.GetService<IEnumerable<IContentScraper>>();

        if (string.IsNullOrEmpty(tokenService?.Bearer))
        {
            return null;
        }

        using var graphClient = await httpClientFactory.GetOboGraphClient(tokenService.Bearer,
                serverConfig.Server, oAuthSettings);

        // ── 3. URL ontleden ────────────────────────────────────────────────────────
        var uri = new Uri(url);
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length < 3 || segments[0] != "l" || segments[1] != "message")
            return null;                                         // geen geldig deep-link-patroon

        var entityId = segments[2];                             // chatId of channelId
        var messageId = segments.Length > 3 ? segments[3] : null;
        if (string.IsNullOrEmpty(messageId))
            return null;

        // Bepaal chat vs kanaal
        var isChat = entityId.EndsWith("unq.gbl.spaces", StringComparison.OrdinalIgnoreCase);
        var fileItems = new List<FileItem>();

        ChatMessage? msg;
        string? contextText = null;

        if (isChat) // ─────────── CHATBERICHT ───────────
        {
            msg = await graphClient.Chats[entityId]
                             .Messages[messageId]
                             .GetAsync(a =>
                             {

                             }, cancellationToken);

            var chat = await graphClient.Chats[entityId]
                             .GetAsync(a =>
                             {
                             }, cancellationToken);

            contextText = string.Join(", ", chat?.Members?.Select(t => t.DisplayName) ?? []);

        }
        else       // ─────────── KANAALBERICHT ─────────
        {
            // groupId (= teamId) nodig uit query, anders kunnen we het bericht niet vinden
            var q = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var teamId = q["groupId"];
            if (string.IsNullOrEmpty(teamId))
                return null; // niet genoeg info

            msg = await graphClient.Teams[teamId]
                             .Channels[entityId]
                             .Messages[messageId]
                            .GetAsync(a =>
                             {
                                 //     a.QueryParameters.Expand = ["attachments"];
                             }, cancellationToken);

            var channel = await graphClient.Teams[teamId]
                                    .Channels[entityId]
                                    .GetAsync(a =>
                                    {
                                    }, cancellationToken);

            var team = await graphClient.Teams[teamId]
                                    .GetAsync(a =>
                                    {
                                    }, cancellationToken);

            contextText = $"{team?.DisplayName} - {channel?.DisplayName}";
        }

        fileItems.Add(new FileItem
        {
            Filename = $"TeamsMessage_{messageId}.json",
            Uri = url,
            MimeType = MediaTypeNames.Application.Json,
            Contents = BinaryData.FromObjectAsJson(new
            {
                Context = contextText,
                msg?.Subject,
                msg?.CreatedDateTime,
                msg?.From?.User?.DisplayName,
                Content = msg?.Body?.Content ?? string.Empty
            })
        });

        // ── 5. Bijlagen ophalen (indien aanwezig) ─────────────────────────────
        if (msg?.Attachments?.Count > 0)
        {
            foreach (var att in msg.Attachments)
            {
                // Alleen bestanden; andere types (links/mentions) overslaan
                if (att.ContentType is "reference" && att.ContentUrl is { Length: > 0 })
                {
                    var supportedScrapers = scrapers?
                       .Where(a => a.SupportsHost(serverConfig, att.ContentUrl));

                    foreach (var scraper in supportedScrapers ?? [])
                    {
                        var fileContent = await scraper.GetContentAsync(mcpServer, serviceProvider, att.ContentUrl, cancellationToken);

                        if (fileContent != null)
                        {
                            fileItems.AddRange(fileContent);
                        }
                    }
                }
                else
                {
                    if (!string.IsNullOrEmpty(att.Content)
                        && !string.IsNullOrEmpty(att.ContentType))
                        fileItems.AddRange(new FileItem()
                        {
                            MimeType = att.ContentType,
                            Uri = att.ContentUrl ?? string.Empty,
                            Filename = att.Name,
                            Contents = BinaryData.FromString(att.Content)
                        });
                }

            }
        }

        return fileItems;
    }

}
