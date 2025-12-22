
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Graph.Beta.Search.Query;

namespace MCPhappey.Tools.SharePoint;

public static class SharePointExtensions
{
    public const string ICON_SOURCE = "https://cdn.worldvectorlogo.com/logos/microsoft-office-sharepoint-2018-present--1.svg";

    /// <summary>
    /// Executes a Microsoft Graph search request for the supplied query and entity types.
    /// </summary>
    public static async Task<IEnumerable<SearchHit>?> SearchContent(
        this GraphServiceClient graphServiceClient,
        string query,
        IReadOnlyList<EntityType?> entityTypes,
        int from = 0,
        int size = 20,
        CancellationToken cancellationToken = default)
    {
        var requestBody = new QueryPostRequestBody
        {
            Requests =
            [
                new()
                    {
                        EntityTypes = [.. entityTypes],
                        Query = new SearchQuery { QueryString = query },
                        From = from,
                        Size = size
                    }
            ],
        };

        var result = await graphServiceClient.Search.Query
            .PostAsQueryPostResponseAsync(requestBody, cancellationToken: cancellationToken);

        var searchItems = result?.Value?
            .SelectMany(y => y.HitsContainers ?? [])
            .SelectMany(y => y.Hits ?? [])
            .ToList();

        var hitContainer = result?.Value?.FirstOrDefault()?.HitsContainers?.FirstOrDefault();

        return searchItems;
    }

    /// <summary>
    /// Runs <see cref="SearchContent"/> across multiple entity combinations with throttling.
    /// </summary>
    public static async Task<IEnumerable<SearchHit?>> ExecuteSearchAcrossEntities(
        this GraphServiceClient client,
        string query,
        IEnumerable<EntityType?[]> entityCombinations,
        int maxConcurrency,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        using var semaphore = new SemaphoreSlim(maxConcurrency);

        var tasks = entityCombinations.Select(async entityTypes =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                return await client.SearchContent(query, entityTypes, size: pageSize, cancellationToken: cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var result = await Task.WhenAll(tasks);

        return result.OfType<IEnumerable<SearchHit>>()
            .SelectMany(a => a)
            .OrderBy(a => a.Rank);
    }

    public static Common.Models.SearchResult MapHit(this SearchHit hit)
    {
        static string? GetListItemTitle(ListItem? li)
        {
            if (li == null) return null;

            // Try to get from Fields.AdditionalData
            if (li.Fields?.AdditionalData != null &&
                li.Fields.AdditionalData.TryGetValue("Title", out var fieldTitle) &&
                fieldTitle is not null)
                return fieldTitle.ToString();

            // Try to get from AdditionalData (case-insensitive)
            if (li.AdditionalData != null)
            {
                if (li.AdditionalData.TryGetValue("title", out var lower) && lower is not null)
                    return lower.ToString();

                if (li.AdditionalData.TryGetValue("Title", out var upper) && upper is not null)
                    return upper.ToString();
            }

            return li.Id;
        }

        var (title, url, id) = hit.Resource switch
        {
            DriveItem d => (d.Name, d.WebUrl, d.Id),
            Contact c => (c.DisplayName, c.EmailAddresses?.FirstOrDefault()?.Address, c.Id),
            Message m => (m.Subject, m.WebLink, m.Id),
            Event ev => (ev.Subject, ev.WebLink, ev.Id),
            ListItem li => (GetListItemTitle(li), li?.WebUrl, li?.Id),
            _ => (null, null, (hit.Resource as Entity)?.Id)
        };

        return new()
        {
            Title = title ?? string.Empty,
            Source = url ?? string.Empty,
            Content =
            [
                new Common.Models.SearchResultContentBlock
            {
                Text = hit.Summary ?? string.Empty
            }
            ],
            Citations = new Common.Models.CitationConfiguration
            {
                Enabled = true
            }
        };
    }
}