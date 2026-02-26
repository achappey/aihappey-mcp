using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Parallel;

public static class ParallelService
{
    [Description("Extracts relevant content from web URLs using Parallel AI.")]
    [McpServerTool(
       Title = "Extract web content",
       Name = "parallel_extract_run",
       ReadOnly = true,
       Destructive = false)]
    public static async Task<CallToolResult?> Parallel_Extract_Run(
       [Description("List of URLs to extract content from.")]
        string[] urls,
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       [Description("Optional search or extraction objective. Focuses content on this goal.")]
        string? objective = null,
       [Description("Optional list of search queries to guide extraction.")]
        string[]? searchQueries = null,
       [Description("Include relevant excerpts from each page.")]
        bool excerpts = true,
       [Description("Include full content from each URL.")]
        bool fullContent = false,
       CancellationToken cancellationToken = default) =>
       await requestContext.WithExceptionCheck(async () =>
       await requestContext.WithStructuredContent(async () =>
   {
       if (urls == null || urls.Length == 0)
           throw new ArgumentException("At least one URL is required.", nameof(urls));

       // 1) Retrieve settings
       var settings = serviceProvider.GetService<ParallelSettings>()
           ?? throw new InvalidOperationException("No ParallelSettings found in service provider.");

       // 2) Prepare HTTP client
       using var client = new HttpClient { BaseAddress = new Uri("https://api.parallel.ai/") };
       client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
       client.DefaultRequestHeaders.Add("parallel-beta", "search-extract-2025-10-10");

       // 3) Build request payload
       var payload = new
       {
           urls,
           objective,
           search_queries = searchQueries,
           fetch_policy = new
           {
               excerpts,
               full_content = fullContent
           }
       };

       var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
       {
           DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
           PropertyNamingPolicy = JsonNamingPolicy.CamelCase
       });

       // 4) Send request
       using var req = new HttpRequestMessage(HttpMethod.Post, "v1beta/extract")
       {
           Content = new StringContent(json, Encoding.UTF8, MimeTypes.Json)
       };

       using var resp = await client.SendAsync(req, cancellationToken);
       var raw = await resp.Content.ReadAsStreamAsync(cancellationToken);

       if (!resp.IsSuccessStatusCode)
           throw new Exception(resp.ReasonPhrase);

       return await JsonNode.ParseAsync(raw, cancellationToken: cancellationToken);

   }));

    [Description("Performs a web search using Parallel AI.")]
    [McpServerTool(
       Title = "Search the web",
       Name = "parallel_search_run",
       ReadOnly = true,
       Destructive = false)]
    public static async Task<CallToolResult?> Parallel_Search_Run(
       [Description("Search query text.")]
        string query,
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       [Description("List of domains to include in the search results.")]
        string[]? includeDomains = null,
       [Description("List of domains to exclude from the search results.")]
        string[]? excludeDomains = null,
       CancellationToken cancellationToken = default) =>
       await requestContext.WithExceptionCheck(async () =>
       await requestContext.WithStructuredContent(async () =>
   {
       ArgumentException.ThrowIfNullOrWhiteSpace(query);

       // 1) Retrieve settings
       var settings = serviceProvider.GetService<ParallelSettings>()
           ?? throw new InvalidOperationException("No ParallelSettings found in service provider.");

       // 2) Prepare HTTP client
       using var client = new HttpClient { BaseAddress = new Uri("https://api.parallel.ai/") };
       client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
       client.DefaultRequestHeaders.Add("parallel-beta", "search-query-2025-10-10");

       // 3) Build request payload
       var payload = new
       {
           query,
           source_policy = new
           {
               include_domains = includeDomains,
               exclude_domains = excludeDomains
           }
       };

       var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
       {
           DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
           PropertyNamingPolicy = JsonNamingPolicy.CamelCase
       });

       // 4) Send request
       using var req = new HttpRequestMessage(HttpMethod.Post, "v1beta/search")
       {
           Content = new StringContent(json, Encoding.UTF8, MimeTypes.Json)
       };

       using var resp = await client.SendAsync(req, cancellationToken);
       var raw = await resp.Content.ReadAsStreamAsync(cancellationToken);

       if (!resp.IsSuccessStatusCode)
           throw new Exception(resp.ReasonPhrase);

       // 5) Return structured response
       return await JsonNode.ParseAsync(raw, cancellationToken: cancellationToken);
   }));

    [Description("Creates a new task run in Parallel AI with basic options.")]
    [McpServerTool(
        Title = "Create task run",
        Name = "parallel_task_create",
        ReadOnly = false,
        Destructive = false)]
    public static async Task<CallToolResult?> Parallel_Task_Create(
        [Description("JSON Schema object defining expected output.")]
        JsonNode outputSchema,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional text or JSON schema describing the expected input.")]
        string? inputSchema = null,
        [Description("Optional list of MCP server identifiers for this run.")]
        string[]? mcpServers = null,
        [Description("Enable task run event tracking.")]
        bool? enableEvents = null,
        CancellationToken cancellationToken = default)
        =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
    {
        // 1) Validate required field
        ArgumentNullException.ThrowIfNull(outputSchema);

        // 2) Prompt user if primitives missing
        if (string.IsNullOrWhiteSpace(inputSchema) || enableEvents == null)
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new CreateParallelTaskPrompt
                {
                    InputSchema = inputSchema,
                    EnableEvents = enableEvents ?? true,
                },
                cancellationToken);

            inputSchema = typed.InputSchema;
            enableEvents = typed.EnableEvents;
        }

        // 3) Retrieve settings
        var settings = serviceProvider.GetService<ParallelSettings>()
            ?? throw new InvalidOperationException("No ParallelSettings found in service provider.");

        // 4) Prepare HTTP client
        using var client = new HttpClient { BaseAddress = new Uri("https://api.parallel.ai/") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        client.DefaultRequestHeaders.Add("parallel-beta", "mcp-server-2025-07-17,events-sse-2025-07-24,webhook-2025-08-12");

        // 5) Build minimal payload
        var payload = new
        {
            task_spec = new
            {
                input_schema = inputSchema,
                output_schema = new
                {
                    type = "json",
                    json_schema = outputSchema
                }
            },
            mcp_servers = mcpServers,
            enable_events = enableEvents,
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        // 6) Send request
        using var req = new HttpRequestMessage(HttpMethod.Post, "v1beta/tasks/runs")
        {
            Content = new StringContent(json, Encoding.UTF8, MimeTypes.Json)
        };

        using var resp = await client.SendAsync(req, cancellationToken);
        var raw = await resp.Content.ReadAsStreamAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
            throw new Exception(resp.ReasonPhrase);

        // 7) Parse response
        var parsed = await JsonNode.ParseAsync(raw, cancellationToken: cancellationToken);
        return parsed;
    }));

    [Description("Please fill in the task creation details.")]
    public class CreateParallelTaskPrompt
    {
        [JsonPropertyName("input_schema")]
        [Description("Text or JSON schema describing the expected input.")]
        public string? InputSchema { get; set; }

        [JsonPropertyName("enable_events")]
        [DefaultValue(true)]
        [Description("Enable tracking of task run progress events.")]
        public bool EnableEvents { get; set; } = true;      
    }

    [Description("Creates a new task group in Parallel AI.")]
    [McpServerTool(
       Title = "Create task group",
       Name = "parallel_taskgroup_create",
       ReadOnly = false,
       Destructive = false)]
    public static async Task<CallToolResult?> Parallel_TaskGroup_Create(
       [Description("Optional user-defined metadata for the task group.")]
        Dictionary<string, string>? metadata,
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       CancellationToken cancellationToken = default) =>
       await requestContext.WithExceptionCheck(async () =>
       await requestContext.WithStructuredContent(async () =>
   {
       // 1) Retrieve settings
       var settings = serviceProvider.GetService<ParallelSettings>()
           ?? throw new InvalidOperationException("No ParallelSettings found in service provider.");

       // 2) Prepare HTTP client
       using var client = new HttpClient { BaseAddress = new Uri("https://api.parallel.ai/") };
       client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
       client.DefaultRequestHeaders.Add("parallel-beta", "taskgroup-2025-10-10");

       // 3) Build request body
       var payload = new
       {
           metadata
       };

       var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
       {
           DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
           PropertyNamingPolicy = JsonNamingPolicy.CamelCase
       });

       // 4) Send request
       using var req = new HttpRequestMessage(HttpMethod.Post, "v1beta/tasks/groups")
       {
           Content = new StringContent(json, Encoding.UTF8, MimeTypes.Json)
       };

       using var resp = await client.SendAsync(req, cancellationToken);
       var raw = await resp.Content.ReadAsStreamAsync(cancellationToken);

       if (!resp.IsSuccessStatusCode)
           throw new Exception(resp.ReasonPhrase);

       // 5) Return structured response
       return await JsonNode.ParseAsync(raw, cancellationToken: cancellationToken);
   }));

    [Description("Creates and runs a task in Parallel AI.")]
    [McpServerTool(
          Title = "Create task run",
          Name = "parallel_task_run_create",
          ReadOnly = false,
          Destructive = false)]
    public static async Task<CallToolResult?> Parallel_TaskRun_Create(
          [Description("Processor to use for the task (e.g. 'base').")]
        string processor,
          [Description("Task input, either plain text or JSON string.")]
        string input,
          IServiceProvider serviceProvider,
          RequestContext<CallToolRequestParams> requestContext,
          [Description("Optional user-defined metadata for the run.")]
        Dictionary<string, string>? metadata = null,
          [Description("Optional source policy for allowed or disallowed domains.")]
        JsonNode? sourcePolicy = null,
          [Description("Optional task specification overriding the default schema.")]
        JsonNode? taskSpec = null,
          [Description("Optional list of MCP servers to use for the run.")]
        string[]? mcpServers = null,
          [Description("Enable task run event tracking.")]
        bool? enableEvents = null,
          [Description("Optional default task specification (applies when task_spec is omitted).")]
        JsonNode? defaultTaskSpec = null,
          CancellationToken cancellationToken = default)
          =>
          await requestContext.WithExceptionCheck(async () =>
          await requestContext.WithStructuredContent(async () =>
      {
          // 1) If primitive fields are missing, elicit them
          if (string.IsNullOrWhiteSpace(processor) || string.IsNullOrWhiteSpace(input))
          {
              var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                  new CreateParallelTaskRunPrompt
                  {
                      Processor = processor,
                      Input = input,
                      EnableEvents = enableEvents ?? true
                  },
                  cancellationToken);

              processor = typed.Processor;
              input = typed.Input;
              enableEvents = typed.EnableEvents;
          }

          ArgumentException.ThrowIfNullOrWhiteSpace(processor);
          ArgumentException.ThrowIfNullOrWhiteSpace(input);

          // 2) Retrieve settings
          var settings = serviceProvider.GetService<ParallelSettings>()
              ?? throw new InvalidOperationException("No ParallelSettings found in service provider.");

          // 3) Prepare HTTP client
          using var client = new HttpClient { BaseAddress = new Uri("https://api.parallel.ai/") };
          client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
          client.DefaultRequestHeaders.Add("parallel-beta", "mcp-server-2025-07-17,events-sse-2025-07-24,webhook-2025-08-12");

          // 4) Build payload
          var payload = new
          {
              processor,
              input,
              metadata,
              source_policy = sourcePolicy,
              task_spec = taskSpec,
              mcp_servers = mcpServers,
              enable_events = enableEvents,
              default_task_spec = defaultTaskSpec
          };

          var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
          {
              DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
              PropertyNamingPolicy = JsonNamingPolicy.CamelCase
          });

          // 5) Send request
          using var req = new HttpRequestMessage(HttpMethod.Post, "v1beta/tasks/runs")
          {
              Content = new StringContent(json, Encoding.UTF8, MimeTypes.Json)
          };

          using var resp = await client.SendAsync(req, cancellationToken);
          var raw = await resp.Content.ReadAsStreamAsync(cancellationToken);

          if (!resp.IsSuccessStatusCode)
              throw new Exception(resp.ReasonPhrase);

          // 6) Return structured JSON response
          var parsed = await JsonNode.ParseAsync(raw, cancellationToken: cancellationToken);
          return parsed;
      }));


    [Description("Please fill in the task run details.")]
    public class CreateParallelTaskRunPrompt
    {
        [JsonPropertyName("processor")]
        [Required]
        [DefaultValue("base")]
        [Description("Processor to use for the task (e.g. 'base').")]
        public string Processor { get; set; } = "base";

        [JsonPropertyName("input")]
        [Required]
        [Description("Task input, either plain text or a JSON string.")]
        public string Input { get; set; } = default!;

        [JsonPropertyName("enable_events")]
        [DefaultValue(true)]
        [Description("Enable tracking of task run progress events.")]
        public bool EnableEvents { get; set; } = true;
    }
}

public class ParallelSettings
{
    public string ApiKey { get; set; } = default!;
}