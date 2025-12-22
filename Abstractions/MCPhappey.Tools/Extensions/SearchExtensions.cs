using System.Collections.Concurrent;
using System.Text.Json;
using MCPhappey.Common.Extensions;
using MCPhappey.Core;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Extensions;

public static class SearchExtensions
{
    private const int TOKEN_SIZE = 30000;

    public static async Task<CallToolResult> ExtractWithFacts(this IServiceProvider serviceProvider,
       McpServer mcpServer,
       RequestContext<CallToolRequestParams> requestContext,
       string facts,
       string factsSourceUrl,
       string query,
       int? progressCounter,
       int limitSources = 5,
       CancellationToken cancellationToken = default)
    {
        var samplingService = serviceProvider.GetRequiredService<SamplingService>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var gptTokenizer = serviceProvider.GetRequiredService<GptTokenizer>();

        var config = serviceProvider.GetServerConfig(mcpServer);

        if (config == null)
        {
            return "Server error".ToErrorCallToolResponse();
        }

        ConcurrentDictionary<string, string> results = [];

        var args = new Dictionary<string, JsonElement>()
        {
            ["facts"] = JsonSerializer.SerializeToElement(facts),
            ["question"] = JsonSerializer.SerializeToElement(query)
        };

        var extractWithFactsSampleTask = samplingService.GetPromptSample(serviceProvider, mcpServer,
            "extract-with-facts", args,
            "gpt-5-mini", cancellationToken: cancellationToken);

        var urlArgs = new Dictionary<string, JsonElement>()
        {
            ["content"] = JsonSerializer.SerializeToElement(facts),
            ["question"] = JsonSerializer.SerializeToElement(query)
        };

        var extracUrlsFromFactsSample = await samplingService.GetPromptSample(serviceProvider,
            mcpServer, "extract-urls-with-facts", urlArgs,
                           "gpt-5.1", cancellationToken: cancellationToken);

        var urls = extracUrlsFromFactsSample.ToText()?
                         .Split(["\", \"", ","], StringSplitOptions.RemoveEmptyEntries)
                         .Select(a => a.Trim())
                         .Where(r => r.StartsWith("http"))
                         .Distinct()
                         .ToList()
                         .Take(limitSources) ?? [];

        var downloadSemaphore = new SemaphoreSlim(3);
        int counter = progressCounter++ ?? 1;
        int total = counter + (urls.Count() * 2);

        var readingTasks = urls.Select(url => ProcessUrlAsync(url, downloadSemaphore, cancellationToken)).ToList();
        await Task.WhenAll(readingTasks);

        // Helper method outside your loop:
        async Task ProcessUrlAsync(string url, SemaphoreSlim semaphore, CancellationToken cancellationToken)
        {
            await semaphore.WaitAsync();

            try
            {

                counter = (int)await mcpServer.SendProgressNotificationAsync(
                    requestContext,
                    counter,
                    $"Downloading: [{new Uri(url).Host}]({url})",
                    cancellationToken: cancellationToken
                );

                var scrapeTask = downloadService.ScrapeContentAsync(serviceProvider, mcpServer, url, cancellationToken: CancellationToken.None);
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                var completedTask = await Task.WhenAny(scrapeTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    throw new TimeoutException($"Timeout while downloading url {url}");
                }
                var urlResults = await scrapeTask;
                var urlResult = string.Join("\n\n", urlResults.Select(a => a.Contents.ToString()));

                if (string.IsNullOrEmpty(urlResult?.Trim()))
                    return;

                var tokenized = gptTokenizer.Tokenize(urlResult, TOKEN_SIZE);

                var urlFactArgs = new Dictionary<string, JsonElement>
                {
                    ["facts"] = JsonSerializer.SerializeToElement(tokenized),
                    ["question"] = JsonSerializer.SerializeToElement(query)
                };

                counter = (int)await mcpServer.SendProgressNotificationAsync(
                                  requestContext,
                                  counter,
                                  $"Reading: [{new Uri(url).Host}]({url})",
                                  cancellationToken: cancellationToken
                              );

                var extractFromUrlsWithFactsSampleTask = samplingService.GetPromptSample(
                    serviceProvider, mcpServer, "extract-with-facts",
                    urlFactArgs, "gpt-5-mini");

                timeoutTask = Task.Delay(TimeSpan.FromSeconds(10), cancellationToken: CancellationToken.None);
                completedTask = await Task.WhenAny(extractFromUrlsWithFactsSampleTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    throw new TimeoutException($"Timeout");
                }

                var extractFromUrlsWithFactsSample = await extractFromUrlsWithFactsSampleTask;
                var extractFromUrlsWithFactsSampleText = extractFromUrlsWithFactsSample.ToText();

                if (!string.IsNullOrEmpty(extractFromUrlsWithFactsSampleText) && extractFromUrlsWithFactsSampleText.HasResult())
                {
                    results.TryAdd(url, extractFromUrlsWithFactsSampleText ?? string.Empty);
                }
            }
            catch (Exception ex)
            {
                await requestContext.Server.SendMessageNotificationAsync($"Failed to process url {url}: {ex}", LoggingLevel.Warning);
            }
            finally
            {
                semaphore.Release();
            }
        }

        var extractWithFactsSample = await extractWithFactsSampleTask;
        var extractWithFactsSampletEXT = extractWithFactsSample.ToText();

        if (!string.IsNullOrEmpty(extractWithFactsSampletEXT) && extractWithFactsSampletEXT.HasResult())
        {
            results.TryAdd(factsSourceUrl,
                extractWithFactsSampletEXT ?? string.Empty);
        }

        return new()
        {
            Content = [.. results.Select(a => a.Value.ToTextResourceContent(a.Key))]
        };
    }

}
