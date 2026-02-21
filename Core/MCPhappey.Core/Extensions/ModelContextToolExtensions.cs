using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using MCPhappey.Telemetry;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Core.Extensions;

public static partial class ModelContextToolExtensions
{
    public static JsonObject ToJsonObject(this Dictionary<string, object>? source)
    {
        var obj = new JsonObject();
        if (source == null)
            return obj;

        foreach (var (key, value) in source)
        {
            obj[key] = value switch
            {
                null => null,
                JsonNode n => n, // already a JsonNode
                JsonElement e => JsonNode.Parse(e.GetRawText()), // handle JSON elements
                Dictionary<string, object> d => d.ToJsonObject(), // recursive
                _ => JsonValue.Create(value) // primitives
            };
        }

        return obj;
    }

    /// <summary>
    /// Retrieves the merged tool metadata (as JsonObject) for a given tool.
    /// Returns an empty JsonObject if not found.
    /// </summary>
    public static async Task<JsonObject> GetToolMeta(
        this RequestContext<CallToolRequestParams> requestContext, Dictionary<string, object>? overrides = null)
    {
        var serverConfig = requestContext.Services?.GetServerConfig(requestContext.Server);
        var toolName = requestContext.Params?.Name;
        var toolMeta = serverConfig?.Server.Tools?.GetValueOrDefault(toolName ?? "")?.Meta;

        var locale = requestContext.Params?.Meta?
                .FirstOrDefault(a => a.Key.EndsWith("/locale"));

        // only add if not already present
        if (locale.HasValue
            && !locale.Value.Key.StartsWith("openai/")
            && locale.Value.Value is JsonNode node
            && node is not null
            && (toolMeta == null || !toolMeta.ContainsKey("openai/locale")))
        {
            toolMeta ??= [];

            // extract string if it's a value node, otherwise clone node
            if (node is JsonValue jv && jv.TryGetValue<string>(out var str))
                toolMeta["openai/locale"] = str;
        }

        // convert base meta to JsonObject
        var result = toolMeta?.ToJsonObject() ?? [];

        // apply overrides (override existing keys or add new)
        if (overrides is not null)
        {
            foreach (var kvp in overrides)
            {
                // serialize override value to JsonNode to stay consistent
                var nodeItem = JsonSerializer.SerializeToNode(kvp.Value, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                if (nodeItem is not null)
                    result[kvp.Key] = nodeItem;
            }
        }

        return await Task.FromResult(result);
    }

    public static async Task<CallToolResult?> WithExceptionCheck(this RequestContext<CallToolRequestParams> requestContext, Func<Task<CallToolResult?>> func)
    {
        try
        {
            return await func();
        }
        catch (Exception e)
        {
            return e.Message.ToErrorCallToolResponse();
        }
    }

    private static readonly JsonSerializerOptions IgnoreNullWebOptions = new(JsonSerializerOptions.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task<CallToolResult> WithStructuredContent<T>(this RequestContext<CallToolRequestParams> requestContext,
       Func<Task<T?>> func)
    {
        try
        {
            var result = await func();
            //var resultContent = JsonSerializer.SerializeToNode(result, IgnoreNullWebOptions);

            JsonNode resultContent = result switch
            {
                JsonNode node => node,
                null => new JsonObject(),
                _ => JsonSerializer.SerializeToNode(result, IgnoreNullWebOptions)!
            };

            return new CallToolResult()
            {
                Meta = await requestContext.GetToolMeta(),
                StructuredContent = resultContent
            };
        }
        catch (Exception e)
        {
            return e.Message.ToErrorCallToolResponse();
        }
    }

    public static async Task<ListToolsResult?> ToToolsList(this Server server, Kernel kernel, IList<Icon> icons,
            Dictionary<string, string>? headers = null)
    {
        if (server.Plugins?.Any() != true && server.McpExtension == null) return null;

        List<McpServerTool>? tools = [];

        foreach (var pluginTypeName in server.Plugins ?? [])
        {
            tools.AddRange(kernel.GetToolsFromType(pluginTypeName, icons) ?? []);
        }

        return await tools.GetListToolsResult(server, headers);
    }

    public static async Task<CallToolResult?> ToCallToolResult(this RequestContext<CallToolRequestParams> request,
        Server server, Kernel kernel, IList<Icon> icons,
        Dictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        var telemtry = request.Services!.GetService<IMcpTelemetryService>();
        var userId = request.User?.Claims.GetUserOid();
        var startTime = DateTime.UtcNow;

        if (server.Plugins?.Any() != true && server.McpExtension == null) return null;

        List<McpServerTool>? tools = [];

        foreach (var pluginTypeName in server.Plugins ?? [])
        {
            tools.AddRange(kernel.GetToolsFromType(pluginTypeName, icons) ?? []);
        }

        var result = await request.GetCallToolResult(tools, server, headers, cancellationToken: cancellationToken);

        var endTime = DateTime.UtcNow;

        if (telemtry != null)
        {
            await telemtry.TrackToolRequestAsync(request.Server.ServerOptions.ServerInfo?.Name!,
                request.Server.SessionId!,
                request.Server.ClientInfo?.Name!,
                request.Server.ClientInfo?.Version!,
                request.Params?.Name!,
                result.GetJsonSizeInBytes(), startTime, endTime, userId, request.User?.Identity?.Name, cancellationToken);
        }

        return result;
    }

    public static IEnumerable<McpServerTool>? GetToolsFromType(this Kernel kernel, string pluginTypeName, IList<Icon> icons)
    {
        var pluginType = Type.GetType(pluginTypeName);
        if (pluginType == null)
            return null;

        IEnumerable<McpServerTool>? pluginTools;

        if (pluginType.IsAbstract && pluginType.IsSealed)
        {
            // Static plugin type
            pluginTools = pluginType
                .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(a => McpServerTool.Create(a));
        }
        else
        {
            // Instance plugin type
            var instance = Activator.CreateInstance(pluginType)!;
            pluginTools = kernel.CreatePluginFromObject(instance)
                .AsAIFunctions()
                .Select(a => McpServerTool.Create(a));
        }

        // Only override icons when the tool has none
        foreach (var tool in pluginTools)
        {
            if (tool.ProtocolTool.Icons == null || !tool.ProtocolTool.Icons.Any())
            {
                tool.ProtocolTool.Icons = icons;
            }
        }

        return pluginTools;
    }


    public static IEnumerable<McpServerPrompt>? GetPromptsFromType(this Kernel kernel, string pluginTypeName, IEnumerable<Icon> icons)
    {
        var pluginType = Type.GetType(pluginTypeName);

        if (pluginType == null)
        {
            return null;
        }

        var createOptions = new Microsoft.Extensions.AI.AIJsonSchemaCreateOptions()
        {
            IncludeParameter = (info) => !info.ParameterType.Equals(typeof(RequestContext<CallToolRequestParams>))
        };

        IEnumerable<McpServerPrompt>? pluginTools = pluginType.IsAbstract && pluginType.IsSealed
            ? pluginType?
                .GetMethods(BindingFlags.Public |
                            BindingFlags.Static |
                            BindingFlags.Instance |
                            BindingFlags.DeclaredOnly)
                .Select(a => McpServerPrompt.Create(a, options: new()
                {
                    Title = McpServerTool.Create(a).ProtocolTool?.Title,
                    SchemaCreateOptions = createOptions,
                    Icons = [.. icons]
                }))
            : kernel.CreatePluginFromObject(Activator.CreateInstance(pluginType)!)
                    .AsAIFunctions()
                  .Select(a => McpServerPrompt.Create(a, options: new()
                  {
                      Title = a.Name,
                      SchemaCreateOptions = createOptions,
                      Icons = [.. icons]
                  }));

        return pluginTools;
    }

    private static async Task<ListToolsResult?> GetListToolsResult(this IEnumerable<McpServerTool>? tools, Server server,
        Dictionary<string, string>? headers = null)
    {
        if (server.McpExtension != null) return await server.ExtendListToolsCapabilities(headers);

        if (tools == null || !tools.Any())
            return null;

        var collection = new McpServerPrimitiveCollection<McpServerTool>();

        foreach (var tool in tools)
            collection.Add(tool);

        return await Task.FromResult(new ListToolsResult()
        {
            Tools = [.. tools.Select(a => a.ProtocolTool)],
        });

    }

    private static async Task<CallToolResult?> GetCallToolResult(
            this RequestContext<CallToolRequestParams> request,
            IEnumerable<McpServerTool>? tools, Server server,
            Dictionary<string, string>? headers = null,
            CancellationToken cancellationToken = default)
    {
        if (server.McpExtension != null) return await request.ExtendListToolsCapabilities(server, headers, cancellationToken: cancellationToken);

        if (tools == null || !tools.Any())
            return null;

        var tool = tools.FirstOrDefault(a => a.ProtocolTool.Name == request.Params?.Name);

        if (tool == null)
        {
            return JsonSerializer.Serialize($"Tool {tool?.ProtocolTool.Name} not found").ToErrorCallToolResponse();
        }

        request.Services!.WithHeaders(headers);

        try
        {
            return await tool.InvokeAsync(request, cancellationToken);
        }
        catch (Exception e)
        {
            return e.Message.ToErrorCallToolResponse();
        }

    }

    private static async Task<ListToolsResult?> ExtendListToolsCapabilities(this Server server,
        Dictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
    {
        var client = await McpClient.CreateAsync(
                  new HttpClientTransport(new HttpClientTransportOptions
                  {
                      Endpoint = new Uri(server.McpExtension?.Url!),
                      AdditionalHeaders = server.McpExtension?.Headers

                  }),
                  clientOptions: new McpClientOptions()
                  {
                      Capabilities = new ClientCapabilities()
                      {
                      }
                  },
                  cancellationToken: cancellationToken);

        if (client.ServerCapabilities.Tools == null) return null;

        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);

        return await Task.FromResult(new ListToolsResult()
        {
            Tools = [.. tools.Select(a => a.ProtocolTool)],
        });
    }

    private static async Task<CallToolResult?> ExtendListToolsCapabilities(this ModelContextProtocol.Server.RequestContext<CallToolRequestParams> request,
        Server server,
       Dictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
    {
        var client = await McpClient.CreateAsync(
                  new HttpClientTransport(new HttpClientTransportOptions
                  {
                      Endpoint = new Uri(server.McpExtension?.Url!),
                      AdditionalHeaders = server.McpExtension?.Headers

                  }),
                  clientOptions: new McpClientOptions()
                  {
                      Handlers = new McpClientHandlers()
                      {
                          ElicitationHandler = request.Server.ClientCapabilities?.Elicitation != null
                            ? async (req, cancellationToken) => await request.Server.ElicitAsync(req!, cancellationToken)
                          : null,
                          RootsHandler = request.Server.ClientCapabilities?.Roots != null
                            ? async (req, cancellationToken) => await request.Server.RequestRootsAsync(req!, cancellationToken)
                          : null,
                          SamplingHandler = request.Server.ClientCapabilities?.Sampling != null
                            ? async (req, progress, cancellationToken) => await request.Server.SampleAsync(req!, cancellationToken)
                          : null
                      },
                      Capabilities = new ClientCapabilities()
                      {
                          Sampling = request.Server.ClientCapabilities?.Sampling,
                          Elicitation = request.Server.ClientCapabilities?.Elicitation,
                          Roots = request.Server.ClientCapabilities?.Roots
                      }
                  },
                  cancellationToken: cancellationToken);


        if (client.ServerCapabilities.Tools == null) return null;

        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
        var tool = tools.FirstOrDefault(a => a.ProtocolTool.Name == request.Params?.Name);

        if (tool == null)
        {
            return JsonSerializer.Serialize($"Tool {tool?.ProtocolTool.Name} not found").ToErrorCallToolResponse();
        }

        request.Services!.WithHeaders(headers);

        var args = request.Params?.Arguments?
            .ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ValueKind == JsonValueKind.Undefined
                    ? null
                    : kvp.Value.Deserialize<object?>()
            );

        try
        {
            return await client.CallToolAsync(tool?.ProtocolTool.Name!, args, cancellationToken: cancellationToken);
        }
        catch (Exception e)
        {
            return e.Message.ToErrorCallToolResponse();
        }
    }

    public static string ResolveExtensionFromMime(this string mimeType)
    {
        var provider = new FileExtensionContentTypeProvider(); // extension -> mime

        foreach (var kvp in provider.Mappings)
        {
            if (string.Equals(kvp.Value, mimeType, StringComparison.OrdinalIgnoreCase))
                return kvp.Key; // e.g. ".xlsx"
        }

        return ".bin";
    }

    public static async Task<CallToolResult?> WithUploads(this RequestContext<CallToolRequestParams> request,
            CreateMessageResult createMessageResult,
            IServiceProvider serviceProvider,
            ContentBlock? customData = null,
            CancellationToken cancellationToken = default)
    {
        List<ContentBlock> uploadBlocks = [];

        foreach (var contentBlock in createMessageResult.Content)
        {
            if (contentBlock is EmbeddedResourceBlock embeddedResourceBlock
                && embeddedResourceBlock.Resource is BlobResourceContents blobResourceContents)
            {
                var upload = await request.Server.Upload(
                    serviceProvider,
                    request.ToOutputFileName(blobResourceContents.MimeType!.ResolveExtensionFromMime()),
                    BinaryData.FromBytes(blobResourceContents.Blob),
                    cancellationToken);

                uploadBlocks.Add(upload!);
            }

            if (contentBlock is ImageContentBlock imageContentBlock)
            {
                var upload = await request.Server.Upload(
                    serviceProvider,
                    request.ToOutputFileName(imageContentBlock.MimeType!.ResolveExtensionFromMime()),
                    BinaryData.FromBytes(imageContentBlock.Data),
                    cancellationToken);

                uploadBlocks.Add(upload!);
            }
        }

        if (uploadBlocks.Count != 0)
        {
            List<ContentBlock> uploadResult = customData != null ? [.. uploadBlocks, customData] : uploadBlocks;

            return uploadResult.ToCallToolResponse();
        }

        List<ContentBlock> resultList = customData != null ? [.. createMessageResult.Content, customData] : [.. createMessageResult.Content];

        return resultList.ToCallToolResponse();
    }

}