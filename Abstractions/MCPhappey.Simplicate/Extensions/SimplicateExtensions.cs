using System.Text.Json;
using MCPhappey.Common;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Simplicate.Options;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Simplicate.Extensions;

public static class SimplicateExtensions
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    { PropertyNameCaseInsensitive = true };

    public static string GetApiUrl(
           this SimplicateOptions options,
           string endpoint)
           => $"https://{options.Organization}.simplicate.app/api/v2{endpoint}";

    public static async Task<SimplicateData<JsonElement>?> GetSimplicatePageAsync(
        this DownloadService downloadService,
        IServiceProvider serviceProvider,
        McpServer mcpServer,
        string url,
        CancellationToken cancellationToken = default)
    {
        var page = await downloadService.ScrapeContentAsync(serviceProvider, mcpServer, url, cancellationToken);
        var stringContent = page?.FirstOrDefault()?.Contents?.ToString();
        if (string.IsNullOrWhiteSpace(stringContent))
            return null;

        return JsonSerializer.Deserialize<SimplicateData<JsonElement>>(stringContent, JsonSerializerOptions);
    }

    public static async Task<List<JsonElement>> GetAllSimplicatePagesAsync(
        this DownloadService downloadService,
        IServiceProvider serviceProvider,
        McpServer mcpServer,
        string baseUrl,
        Func<int, string> progressTextSelector,
        RequestContext<CallToolRequestParams> requestContext,
        int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        var results = new List<JsonElement>();
        int offset = 0;
        int? totalCount = null;
        int? totalPages = null;

        while (true)
        {
            int pageNumber = (offset / pageSize) + 1;
            var builder = new UriBuilder(baseUrl);
            var queryDict = QueryHelpers.ParseQuery(builder.Query);
            queryDict["limit"] = pageSize.ToString();
            queryDict["offset"] = offset.ToString();
            queryDict["metadata"] = "count";
            builder.Query = string.Join("&", queryDict.SelectMany(kvp => kvp.Value.Select(v => $"{kvp.Key}={Uri.EscapeDataString(v!)}")));
            string url = builder.Uri.ToString();

            await requestContext.Server.SendProgressNotificationAsync(
                requestContext,
                pageNumber,
                progressTextSelector(pageNumber),
                totalPages > 0 ? totalPages : null,
                cancellationToken);

            var result = await downloadService.GetSimplicatePageAsync(
                serviceProvider, mcpServer, url, cancellationToken);

            if (result?.Data == null)
                break;

            var markdown =
                $"<details><summary><a href=\"{url}\" target=\"blank\">{new Uri(url).Host}</a></summary>\n\n```\n{JsonSerializer.Serialize(result)}\n```\n</details>";

            await requestContext.Server.SendMessageNotificationAsync(markdown, LoggingLevel.Debug, 
                cancellationToken: cancellationToken);

            results.AddRange(result.Data);

            if (totalCount == null && result.Metadata != null)
            {
                totalCount = result.Metadata.Count;
                totalPages = (int)Math.Ceiling((double)totalCount.Value / pageSize);
            }

            offset += pageSize;
            if (totalCount.HasValue && offset >= totalCount.Value)
                break;
        }

        return results;
    }


    public static DateTime? ParseDate(this string? dateString)
    {
        if (string.IsNullOrWhiteSpace(dateString))
            return null;
        if (DateTime.TryParse(dateString, out var dt))
            return dt;
        // eventueel: custom parse logic als je een ISO-formaat of andere verwacht
        return null;
    }


    public static int? ParseInt(this string? intString)
    {
        if (string.IsNullOrWhiteSpace(intString))
            return null;
        if (int.TryParse(intString, out var dt))
            return dt;
        // eventueel: custom parse logic als je een ISO-formaat of andere verwacht
        return null;
    }


    public static decimal ToAmount(this decimal item) =>
         Math.Round(item, 2, MidpointRounding.AwayFromZero);


    public static async Task<SimplicateData<T>?> GetSimplicatePageAsync<T>(
        this DownloadService downloadService,
        IServiceProvider serviceProvider,
        McpServer mcpServer,
        string url,
        CancellationToken cancellationToken = default)
    {
        var page = await downloadService.ScrapeContentAsync(serviceProvider, mcpServer, url, cancellationToken);
        var stringContent = page?.FirstOrDefault()?.Contents?.ToString();

        if (string.IsNullOrWhiteSpace(stringContent))
            return null;

        var opts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        return JsonSerializer.Deserialize<SimplicateData<T>>(stringContent, opts);
    }

    public static async Task<SimplicateItemData<T>?> GetSimplicateItemAsync<T>(
        this DownloadService downloadService,
        IServiceProvider serviceProvider,
        McpServer mcpServer,
        string url,
        CancellationToken cancellationToken = default)
    {
        var page = await downloadService.ScrapeContentAsync(serviceProvider, mcpServer, url, cancellationToken);
        var stringContent = page?.FirstOrDefault()?.Contents?.ToString();

        if (string.IsNullOrWhiteSpace(stringContent))
            return null;

        return JsonSerializer.Deserialize<SimplicateItemData<T>>(stringContent);
    }


    public static async Task<List<T>> GetAllSimplicatePagesAsync<T>(
        this DownloadService downloadService,
        IServiceProvider serviceProvider,
        McpServer mcpServer,
        string baseUrl,
        string filterString,
        Func<int, string> progressTextSelector,
        RequestContext<CallToolRequestParams> requestContext,
        int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        var results = new List<T>();
        int offset = 0;
        int? totalCount = null;
        int? totalPages = null;

        while (true)
        {
            int pageNumber = (offset / pageSize) + 1;
            string url = $"{baseUrl}?{filterString}&limit={pageSize}&offset={offset}&metadata=count";

            await requestContext.Server.SendProgressNotificationAsync(
                requestContext,
                pageNumber,
                progressTextSelector(pageNumber),
                totalPages > 0 ? totalPages : null,
                cancellationToken
            );

            var result = await downloadService.GetSimplicatePageAsync<T>(
                        serviceProvider, mcpServer, url, cancellationToken);

            if (result == null || result.Data == null)
                break;

            var uri = new Uri(url);
            var domain = uri.Host;
            var markdown =
                  $"<details><summary><a href=\"{url}\" target=\"blank\">{domain}</a></summary>\n\n```\n{JsonSerializer.Serialize(result)}\n```\n</details>";
            await requestContext.Server.SendMessageNotificationAsync(markdown, LoggingLevel.Debug);

            results.AddRange(result.Data);

            if (totalCount == null && result.Metadata != null)
            {
                totalCount = result.Metadata.Count;
                totalPages = (int)Math.Ceiling((double)totalCount.Value / pageSize);
            }

            offset += pageSize;
            if (totalCount.HasValue && offset >= totalCount.Value)
                break;
        }

        return results;
    }

    /// <summary>
    /// Common case: elicit and POST the same DTO type.
    /// Requires a public parameterless ctor to satisfy TryElicit's constraint.
    /// </summary>
    public static async Task<CallToolResult?> PostSimplicateResourceAsync<TDto>(
        this IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string relativePath,                   // e.g. "/projects/projectservice"
        TDto seedDto,
        CancellationToken cancellationToken = default)
        where TDto : class, new()              // <-- add new()
    {
        var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
        var url = simplicateOptions.GetApiUrl(relativePath);

        var (dto, notAccepted, _) = await requestContext.Server.TryElicit(seedDto, cancellationToken);
        if (notAccepted != null) return notAccepted;

        var scraper = serviceProvider.GetServices<IContentScraper>()
                                     .OfType<SimplicateScraper>()
                                     .First();

        var content = await scraper.PostSimplicateItemAsync(
            serviceProvider, url, dto!, requestContext: requestContext, cancellationToken: cancellationToken);

        return content?.ToCallToolResult();
    }

    /// <summary>
    /// Common case: elicit a DTO, then map it into the proper JSON structure and POST.
    /// Requires a public parameterless ctor to satisfy TryElicit's constraint.
    /// </summary>
    public static async Task<CallToolResult?> PostSimplicateResourceAsync<TDto>(
        this IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string relativePath,                       // e.g. "/projects/projectservice"
        TDto seedDto,
        Func<TDto, object> mapper,                 // <-- new: map DTO → object/dynamic/anon type
        CancellationToken cancellationToken = default)
        where TDto : class, new()
    {
        var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
        var url = simplicateOptions.GetApiUrl(relativePath);

        // Let Elicit fill the flat DTO
        var (dto, notAccepted, _) = await requestContext.Server.TryElicit(seedDto, cancellationToken);
        if (notAccepted != null) return notAccepted;

        // Map flat DTO into the correct Simplicate structure
        var mappedObject = mapper(dto!);

        var scraper = serviceProvider.GetServices<IContentScraper>()
                                     .OfType<SimplicateScraper>()
                                     .First();

        var content = await scraper.PostSimplicateItemAsync(
            serviceProvider,
            url,
            mappedObject,
            requestContext: requestContext,
            cancellationToken: cancellationToken);

        return content?.ToCallToolResult();
    }

    public static async Task<CallToolResult?> PutSimplicateResourceMergedAsync<TDto>(
        this IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string relativePath,       // e.g. "/crm/organization/{id}"
        TDto incomingDto,          // partial update data
        Func<TDto, object> mapper, // maps final dto → PUT body
        CancellationToken cancellationToken = default)
        where TDto : class, new()
    {
        var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var url = simplicateOptions.GetApiUrl(relativePath);

        // 1️⃣ Fetch existing item first
        var existing = await downloadService.GetSimplicateItemAsync<TDto>(
            serviceProvider, requestContext.Server, url, cancellationToken);

        var baseDto = existing?.Data ?? new TDto();

        // 2️⃣ Pre-fill incomingDto with defaults from existing
        foreach (var prop in typeof(TDto).GetProperties())
        {
            var incomingVal = prop.GetValue(incomingDto);
            if (incomingVal == null)
            {
                var existingVal = prop.GetValue(baseDto);
                if (existingVal != null)
                    prop.SetValue(incomingDto, existingVal);
            }
        }

        // 3️⃣ Let user/AI elicit interactively (with defaults prefilled)
        var (dto, notAccepted, _) = await requestContext.Server.TryElicit(incomingDto, cancellationToken);
        if (notAccepted != null) return notAccepted;

        // 4️⃣ Merge: prefer elicited non-nulls over existing
        foreach (var prop in typeof(TDto).GetProperties())
        {
            var newVal = prop.GetValue(dto);
            if (newVal != null)
                prop.SetValue(baseDto, newVal);
        }

        // 5️⃣ Map and PUT
        var mappedObject = mapper(baseDto);
        var scraper = serviceProvider.GetServices<IContentScraper>()
                                     .OfType<SimplicateScraper>()
                                     .First();

        var content = await scraper.PutSimplicateItemAsync(
            serviceProvider, url, mappedObject,
            requestContext: requestContext, cancellationToken: cancellationToken);

        return content?.ToCallToolResult();
    }

    public static async Task<ContentBlock?> PostSimplicateItemAsync<T>(
          this IServiceProvider serviceProvider,
          string baseUrl, // e.g. "https://{subdomain}.simplicate.nl/api/v2/project/project"
          T item,
          RequestContext<CallToolRequestParams> requestContext,
          CancellationToken cancellationToken = default)
    {
        var scraper = serviceProvider.GetServices<IContentScraper>()
         .OfType<SimplicateScraper>().First();

        return await scraper.PostSimplicateItemAsync(
         serviceProvider,
         baseUrl,
         item,
         requestContext: requestContext,
         cancellationToken: cancellationToken
     );
    }

    public static async Task<ContentBlock?> PostSimplicateItemAsync<T>(
        this SimplicateScraper downloadService,
        IServiceProvider serviceProvider,
        string baseUrl, // e.g. "https://{subdomain}.simplicate.nl/api/v2/project/project"
        T item,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(item, JsonSerializerOptions.Web);

        if (LoggingLevel.Debug.ShouldLog(requestContext.Server.LoggingLevel))
        {
            await requestContext.Server.SendMessageNotificationAsync(
                $"<details><summary>POST <code>{baseUrl}</code></summary>\n\n```\n{json}\n```\n</details>",
                LoggingLevel.Debug
            );
        }

        // Use your DownloadService to POST (assumes similar signature to ScrapeContentAsync)
        var response = await downloadService.PostContentAsync<T>(
            serviceProvider, baseUrl, json, cancellationToken);

        if (LoggingLevel.Debug.ShouldLog(requestContext.Server.LoggingLevel))
        {
            await requestContext.Server.SendMessageNotificationAsync(
                $"<details><summary>RESPONSE</summary>\n\n```\n{JsonSerializer.Serialize(response,
                    ResourceExtensions.JsonSerializerOptions)}\n```\n</details>",
                LoggingLevel.Debug
            );
        }

        return response.ToJsonContentBlock($"{baseUrl}/{response?.Data.Id}");
    }


    public static async Task<ContentBlock?> PutSimplicateItemAsync<T>(
        this SimplicateScraper downloadService,
        IServiceProvider serviceProvider,
        string baseUrl, // e.g. "https://{subdomain}.simplicate.nl/api/v2/project/project"
        T item,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(item, JsonSerializerOptions.Web);

        if (LoggingLevel.Debug.ShouldLog(requestContext.Server.LoggingLevel))
        {
            await requestContext.Server.SendMessageNotificationAsync(
                $"<details><summary>PUT <code>{baseUrl}</code></summary>\n\n```\n{json}\n```\n</details>",
                LoggingLevel.Debug
            );
        }

        // Use your DownloadService to POST (assumes similar signature to ScrapeContentAsync)
        var response = await downloadService.PutContentAsync<T>(
            serviceProvider, baseUrl, json, cancellationToken);

        if (LoggingLevel.Debug.ShouldLog(requestContext.Server.LoggingLevel))
        {
            await requestContext.Server.SendMessageNotificationAsync(
                $"<details><summary>RESPONSE</summary>\n\n```\n{JsonSerializer.Serialize(response,
                    ResourceExtensions.JsonSerializerOptions)}\n```\n</details>",
                LoggingLevel.Debug
            );
        }

        return response.ToJsonContentBlock($"{baseUrl}/{response?.Data.Id}");
    }
}
