using System.Web;
using MCPhappey.Core.Extensions;
using Microsoft.Graph.Beta;

namespace MCPhappey.Tools.OpenAI.Decisions;

public sealed record OpenAIDecisionsDocument(string Url, string? Name, string? DriveId = null, string? ItemId = null);

public sealed record OpenAIDecisionsDocuments(
    IReadOnlyList<OpenAIDecisionsDocument> Documents,
    IReadOnlyList<OpenAIDecisionsDocument> Folders)
{
    public bool HasFolders => Folders.Count > 0;
}

/// <summary>Resolves authenticated SharePoint/OneDrive for Business links without changing resource scraping.</summary>
public sealed class OpenAIDecisionsDocumentDiscovery(GraphServiceClient graph)
{
    public static bool RequiresGraph(string url)
    {
        var uri = new Uri(url);
        // Site pages/news and REST resources are handled by the existing scrapers, not drive items.
        return uri.Host.EndsWith(".sharepoint.com", StringComparison.OrdinalIgnoreCase)
            && !uri.AbsolutePath.Contains("/SitePages/", StringComparison.OrdinalIgnoreCase)
            && !uri.AbsolutePath.Contains("/_api/", StringComparison.OrdinalIgnoreCase)
            && !uri.AbsolutePath.EndsWith("/news.aspx", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<OpenAIDecisionsDocuments> DiscoverAsync(
        string[] urls, CancellationToken cancellationToken = default)
    {
        OpenAIDecisionsInputBuilder.ValidateUrls(urls);
        var documents = new List<OpenAIDecisionsDocument>();
        var folders = new List<OpenAIDecisionsDocument>();
        var identities = new HashSet<(string?, string)>();
        var visitedFolders = new HashSet<(string?, string)>();

        void AddDocument(OpenAIDecisionsDocument document)
        {
            var identity = document.DriveId != null && document.ItemId != null
                ? (document.DriveId, document.ItemId) : (null, document.Url);
            if (identities.Add(identity))
                documents.Add(document);
        }

        foreach (var url in urls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!RequiresGraph(url))
            {
                AddDocument(new(url, Path.GetFileName(new Uri(url).AbsolutePath)));
                continue;
            }

            var item = await graph.GetDriveItem(NormalizeFolderViewUrl(url), cancellationToken)
                ?? throw new InvalidOperationException($"Could not resolve SharePoint document or folder: {url}");
            if (item.Folder == null)
            {
                // Retain the supplied URL for the existing authenticated extraction pipeline.
                AddDocument(new(url, item.Name, item.ParentReference?.DriveId, item.Id));
                continue;
            }

            var driveId = item.ParentReference?.DriveId
                ?? throw new InvalidOperationException("SharePoint folder DriveId is missing.");
            var folderId = item.Id ?? throw new InvalidOperationException("SharePoint folder Id is missing.");
            if (!visitedFolders.Add((driveId, folderId)))
                continue;
            folders.Add(new(url, item.Name, driveId, folderId));

            var children = graph.Drives[driveId].Items[folderId].Children;
            var page = await children.GetAsync(cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException("SharePoint folder listing returned no response.");
            while (true)
            {
                foreach (var child in page.Value ?? [])
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // Never recurse or treat a folder listing as document evidence.
                    if (child.File == null || child.Folder != null)
                        continue;
                    if (string.IsNullOrWhiteSpace(child.WebUrl) || string.IsNullOrWhiteSpace(child.Id))
                        throw new InvalidOperationException("SharePoint folder contains a file without a source URL or Id.");
                    OpenAIDecisionsInputBuilder.ValidateUrls([child.WebUrl]);
                    AddDocument(new(child.WebUrl, child.Name, child.ParentReference?.DriveId ?? driveId, child.Id));
                }

                if (string.IsNullOrEmpty(page.OdataNextLink))
                    break;
                page = await children.WithUrl(page.OdataNextLink).GetAsync(cancellationToken: cancellationToken)
                    ?? throw new InvalidOperationException("SharePoint folder listing returned no response.");
            }
        }

        return new(documents, folders);
    }

    private static string NormalizeFolderViewUrl(string url)
    {
        var uri = new Uri(url);
        if (!uri.AbsolutePath.EndsWith("/Forms/AllItems.aspx", StringComparison.OrdinalIgnoreCase)
            && !uri.AbsolutePath.EndsWith("/_layouts/15/onedrive.aspx", StringComparison.OrdinalIgnoreCase))
            return url;

        var query = HttpUtility.ParseQueryString(uri.Query);
        var path = query["id"] ?? query["RootFolder"];
        // View URLs carry a server-relative path; never accept a different host here.
        return path is not null && path.StartsWith('/') && !path.StartsWith("//")
            ? uri.GetLeftPart(UriPartial.Authority) + path : url;
    }
}
