using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Copilot;

public static class GraphCopilotRetrieval
{

    [Description("Chat with Microsoft 365 Copilot through the streamed Microsoft Graph Chat API, with optional timezone and web grounding.")]
    [McpServerTool(
       Title = "Microsoft 365 Copilot Chat",
       Name = "graph_copilot_chat",
       OpenWorld = false,
       ReadOnly = true)]
    public static async Task<CallToolResult?> Graph_CopilotChat(
       RequestContext<CallToolRequestParams> requestContext,
       IServiceProvider serviceProvider,
       [Description("The prompt to send to Microsoft 365 Copilot")] string prompt,
       [Description("IANA timezone identifier (e.g. Europe/Amsterdam)")] string timeZone = "Europe/Amsterdam",
       [Description("Include Web search grounding. When disabled, only Microsoft (OneDrive and SharePoint) company grounding will be used.")] bool? isWebEnabled = true,
       CancellationToken cancellationToken = default)
       => await ModelContextToolExtensions.WithExceptionCheck(async () =>
       await requestContext.WithOboGraphClient(async client =>
       await requestContext.WithStructuredContent(async () =>
       {
           var httpClient = await serviceProvider.GetGraphHttpClient(requestContext.Server);

           using var createContent = new StringContent("{}", Encoding.UTF8, "application/json");
           using var createResponse = await httpClient.PostAsync(
               "https://graph.microsoft.com/beta/copilot/conversations",
               createContent,
               cancellationToken);

           var createPayload = await createResponse.Content.ReadAsStringAsync(cancellationToken);
           if (!createResponse.IsSuccessStatusCode)
               throw new InvalidOperationException(
                   $"Microsoft Copilot create conversation failed ({(int)createResponse.StatusCode}): {createPayload}");

           using var createDocument = JsonDocument.Parse(createPayload);
           if (!createDocument.RootElement.TryGetProperty("id", out var conversationIdElement)
               || string.IsNullOrWhiteSpace(conversationIdElement.GetString()))
           {
               throw new InvalidOperationException(
                   "Microsoft Copilot create conversation response did not include an id.");
           }

           var conversationId = conversationIdElement.GetString()!;
           var body = new Dictionary<string, object?>
           {
               ["message"] = new Dictionary<string, object?>
               {
                   ["text"] = prompt
               },
               ["locationHint"] = new Dictionary<string, object?>
               {
                   ["timeZone"] = timeZone
               },
               ["contextualResources"] = new Dictionary<string, object?>
               {
                   ["webContext"] = new Dictionary<string, object?>
                   {
                       ["isWebEnabled"] = isWebEnabled
                   }
               }
           };

           using var chatRequest = new HttpRequestMessage(
               HttpMethod.Post,
               $"https://graph.microsoft.com/beta/copilot/conversations/{Uri.EscapeDataString(conversationId)}/chatOverStream")
           {
               Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
           };
           chatRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

           using var chatResponse = await httpClient.SendAsync(
               chatRequest,
               HttpCompletionOption.ResponseHeadersRead,
               cancellationToken);

           if (!chatResponse.IsSuccessStatusCode)
           {
               var error = await chatResponse.Content.ReadAsStringAsync(cancellationToken);
               throw new InvalidOperationException(
                   $"Microsoft Copilot chatOverStream failed ({(int)chatResponse.StatusCode}): {error}");
           }

           await using var stream = await chatResponse.Content.ReadAsStreamAsync(cancellationToken);
           using var reader = new StreamReader(stream);
           var dataBuffer = new StringBuilder();
           var accumulatedText = string.Empty;
           JsonElement? lastConversation = null;
           int? progressCounter = 0;

           async Task FlushEventAsync()
           {
               var data = dataBuffer.ToString().Trim();
               dataBuffer.Clear();

               if (string.IsNullOrWhiteSpace(data)
                   || string.Equals(data, "[DONE]", StringComparison.OrdinalIgnoreCase))
                   return;

               JsonElement chunk;
               try
               {
                   chunk = JsonSerializer.Deserialize<JsonElement>(data).Clone();
               }
               catch (JsonException)
               {
                   return;
               }

               if (chunk.ValueKind != JsonValueKind.Object)
                   return;

               lastConversation = chunk;
               var currentText = ExtractAssistantText(chunk, prompt);
               if (string.IsNullOrWhiteSpace(currentText)
                   || string.Equals(currentText, accumulatedText, StringComparison.Ordinal))
                   return;

               accumulatedText = currentText.StartsWith(accumulatedText, StringComparison.Ordinal)
                   ? accumulatedText + currentText[accumulatedText.Length..]
                   : currentText;

               progressCounter = await requestContext.Server.SendProgressNotificationAsync(
                   requestContext,
                   progressCounter,
                   accumulatedText,
                   cancellationToken: cancellationToken);
           }

           while (await reader.ReadLineAsync(cancellationToken) is { } line)
           {
               if (line.Length == 0)
               {
                   await FlushEventAsync();
                   continue;
               }

               if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
               {
                   if (dataBuffer.Length > 0)
                       dataBuffer.AppendLine();

                   dataBuffer.Append(line[5..].TrimStart());
               }
           }

           await FlushEventAsync();

           return lastConversation
               ?? throw new InvalidOperationException(
                   "Microsoft Copilot chatOverStream completed without a valid conversation response.");
       })));

    private static string? ExtractAssistantText(JsonElement conversation, string prompt)
    {
        if (!conversation.TryGetProperty("messages", out var messages)
            || messages.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var message in messages.EnumerateArray().Reverse())
        {
            if (!message.TryGetProperty("text", out var textElement)
                || textElement.ValueKind != JsonValueKind.String)
                continue;

            var text = textElement.GetString();
            if (!string.IsNullOrWhiteSpace(text)
                && !string.Equals(text, prompt, StringComparison.Ordinal))
                return text;
        }

        return null;
    }

    [Description("Retrieve Microsoft 365 Copilot semantic search results using the Microsoft Graph Retrieval API.")]
    [McpServerTool(
        Title = "Microsoft 365 Copilot Retrieval",
        Name = "graph_copilot_retrieval",
        OpenWorld = false,
        ReadOnly = true)]
    public static async Task<CallToolResult?> Graph_CopilotRetrieval(
        RequestContext<CallToolRequestParams> requestContext,
        IServiceProvider serviceProvider,
        [Description("The semantic search query")] string query,
        [Description("Indicates whether extracts should be retrieved from SharePoint, OneDrive, or Copilot connectors. Acceptable values are sharePoint, oneDriveBusiness, and externalItem.")] string dataSource = "sharePoint",
        [Description("The number of results that are returned in the response. Must be between 1 and 25.")] int? maximumNumberOfResults = null,
        [Description("Optional KQL filterExpression (e.g. path:\"https://contoso.sharepoint.com/sites/HR/\")")] string? filterExpression = null,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var httpClient = await serviceProvider.GetGraphHttpClient(requestContext.Server);

            // Be strict: API is picky
            var max = maximumNumberOfResults ?? 10;
            if (max < 1) max = 1;
            if (max > 25) max = 25;

            var body = new Dictionary<string, object?>
            {
                ["queryString"] = query,
                ["dataSource"] = dataSource, // must be exact casing per docs
                ["maximumNumberOfResults"] = max,
                ["resourceMetadata"] = new[] { "title", "author" }
            };

            if (!string.IsNullOrWhiteSpace(filterExpression))
                body["filterExpression"] = filterExpression;

            var json = JsonSerializer.Serialize(body);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var resp = await httpClient.PostAsync(
                "https://graph.microsoft.com/beta/copilot/retrieval",
                content,
                cancellationToken);

            var payload = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
            {
                throw new Exception(payload);
            }

            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement.Clone();
        })));

    [Description("Perform Microsoft 365 Copilot hybrid search (semantic + lexical) via Microsoft Graph.")]
    [McpServerTool(
           Title = "Microsoft 365 Copilot Search",
           Name = "graph_copilot_search",
           OpenWorld = false,
           ReadOnly = true)]
    public static async Task<CallToolResult?> Graph_CopilotSearch(
           RequestContext<CallToolRequestParams> requestContext,
           IServiceProvider serviceProvider,
           [Description("Natural language query")] string query,
           [Description("Number of results (1-100)")] int? pageSize = null,
           [Description("Optional KQL filter expression (path-based etc.)")] string? filterExpression = null,
           [Description("Include OneDrive results")] bool includeOneDrive = true,
           [Description("Include SharePoint results")] bool includeSharePoint = true,
           CancellationToken cancellationToken = default)
           => await ModelContextToolExtensions.WithExceptionCheck(async () =>
           await requestContext.WithOboGraphClient(async client =>
           await requestContext.WithStructuredContent(async () =>
           {
               var httpClient = await serviceProvider.GetGraphHttpClient(requestContext.Server);

               var size = pageSize ?? 25;
               if (size < 1) size = 1;
               if (size > 100) size = 100;

               // Build dataSources object dynamically
               var dataSources = new Dictionary<string, object>();

               if (includeOneDrive)
               {
                   dataSources["oneDrive"] = BuildSource(filterExpression);
               }

               if (includeSharePoint)
               {
                   dataSources["sharePoint"] = BuildSource(filterExpression);
               }

               var body = new Dictionary<string, object?>
               {
                   ["query"] = query,
                   ["pageSize"] = size,
                   ["dataSources"] = dataSources
               };

               var json = JsonSerializer.Serialize(body);
               using var content = new StringContent(json, Encoding.UTF8, "application/json");

               using var resp = await httpClient.PostAsync(
                   "https://graph.microsoft.com/beta/copilot/search",
                   content,
                   cancellationToken);

               var payload = await resp.Content.ReadAsStringAsync(cancellationToken);

               if (!resp.IsSuccessStatusCode)
               {
                   throw new Exception(payload);
               }

               using var doc = JsonDocument.Parse(payload);
               return doc.RootElement.Clone();
           })));

    private static object BuildSource(string? filterExpression)
    {
        var source = new Dictionary<string, object>
        {
            ["resourceMetadataNames"] = new[] { "title", "author" }
        };

        if (!string.IsNullOrWhiteSpace(filterExpression))
        {
            source["filterExpression"] = filterExpression;
        }

        return source;
    }
}
