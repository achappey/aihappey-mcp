using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Common.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Common.Extensions;

public static class ToolExtensions
{

    public static CallToolResult WithGatewayCost(
           this CallToolResult result,
           decimal? cost)
    {
        if (cost is null)
            return result;

        result.Meta ??= [];

        if (result.Meta["gateway"] is not JsonObject gateway)
        {
            gateway = [];
            result.Meta["gateway"] = gateway;
        }

        gateway["cost"] = cost;

        return result;
    }

    public static JsonObject ToJsonObject<T>(this T item)
    {
        if (item is JsonObject jsonObject)
            return (JsonObject)jsonObject.DeepClone();

        if (item is JsonNode jsonNode)
            return JsonNode.Parse(jsonNode.ToJsonString())?.AsObject() ?? new JsonObject();

        return JsonSerializer.SerializeToNode(item, JsonSerializerOptions.Web)?.AsObject() ?? new JsonObject();
    }


    public static JsonElement? ToJsonElement(this JsonNode? node)
    {
        if (node is null)
            return null;

        using var doc = JsonDocument.Parse(node.ToJsonString());
        return doc.RootElement.Clone();
    }

    public static JsonElement ToJsonElement<T>(this T item)
    {
        if (item is JsonElement element)
            return element.Clone();

        if (item is JsonDocument document)
            return document.RootElement.Clone();

        if (item is JsonNode jsonNode)
            return jsonNode.ToJsonElement() ?? ParseJsonElement("{}");

        return JsonSerializer.SerializeToElement(item, JsonSerializerOptions.Web);
    }

    public static JsonElement ParseJsonElement(this string? json)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        return doc.RootElement.Clone();
    }

    /* public static JsonNode? ToStructuredContent<T>(this T item)
             => JsonSerializer.SerializeToNode(item, JsonSerializerOptions.Web);*/

    public static JsonElement? ToStructuredContent<T>(this T item)
            => JsonSerializer.SerializeToElement(item, JsonSerializerOptions.Web);

    public static CallToolResult ToCallToolResponse(this IEnumerable<ContentBlock> content)
        => new()
        {
            Content = [.. content]
        };

    public static CallToolResult ToCallToolResponse(this JsonElement content)
          => new()
          {
              StructuredContent = content
          };

    public static CallToolResult ToCallToolResponse(this JsonNode content)
    {
        using var doc = JsonDocument.Parse(content.ToJsonString());

        return new CallToolResult
        {
            StructuredContent = doc.RootElement.Clone()
        };
    }


    public static CallToolResult ToErrorCallToolResponse(this string content)
         => new()
         {
             IsError = true,
             Content = [content.ToTextContentBlock()]
         };

    public static CallToolResult ToTextCallToolResponse(this string content)
            => new()
            {
                Content = [content.ToTextContentBlock()]
            };

    public static CallToolResult ToResourceLinkCallToolResponse(this ResourceLinkBlock resourceLinkBlock)
         => new()
         {
             Content = [resourceLinkBlock]
         };

    public static CallToolResult ToResourceLinkCallToolResponse(this IEnumerable<ResourceLinkBlock> resourceLinkBlocks)
            => new()
            {
                Content = [.. resourceLinkBlocks]
            };

    public static CallToolResult ToJsonCallToolResponse(this string content, string uri)
         => new()
         {
             Content = [content.ToJsonContent(uri)]
         };

    public static TextContentBlock ToTextContentBlock(this string contents) => new()
    {
        Text = contents
    };

    public static ResourceLinkBlock ToResourceLinkBlock(this string uri, string name, string? mimeType = null, string? description = null, long? size = null) => new()
    {
        Uri = uri,
        Name = name ?? "No name",
        Description = description,
        MimeType = mimeType,
        Size = size
    };

    public static CallToolResult ToCallToolResult(this ReadResourceResult result) => new()
    {
        Content = [.. result.Contents.Select(z => z.ToContent())],
    };

    public static CallToolResult ToCallToolResult(this ContentBlock result) => new()
    {
        Content = [result],
    };

    public static CallToolResult ToCallToolResult(this IEnumerable<ContentBlock> results) => new()
    {
        Content = [.. results],
    };

    public static string ToOutputFileName(this RequestContext<CallToolRequestParams> context, string extension)
        => $"{context.ToOutputFileName()}.{extension.ToLower()}";

    public static string ToOutputFileName(this RequestContext<CallToolRequestParams> context)
        => $"{DateTime.Now:yyMMdd_HHmmss}_{context.Params?.Name ?? context.Server.ServerOptions.ServerInfo?.Name}";

    public static string ToOutputFileName(this string filename)
        => $"{DateTime.Now:yyMMdd_HHmmss}_{filename}";

    public static HashSet<string> GetAllPlugins(this IReadOnlyList<ServerConfig> results) =>
            [.. results
             .Where(a => a.Server.Roles?.Any() != true)
             .SelectMany(r => r.Server.Plugins ?? [])
             .OfType<string>()
             .Distinct()];

    public static string GetEnumMemberValue<T>(this T enumValue) where T : Enum
    {
        var memberInfo = typeof(T).GetMember(enumValue.ToString()).FirstOrDefault();
        var attribute = memberInfo?.GetCustomAttribute<EnumMemberAttribute>();
        return attribute?.Value ?? enumValue.ToString();
    }

    public static async ValueTask<List<T>> MaterializeToListAsync<T>(
       this IAsyncEnumerable<T> source,
       CancellationToken cancellationToken = default)
    {
        var list = new List<T>();
        await foreach (var item in source.WithCancellation(cancellationToken))
        {
            list.Add(item);
        }
        return list;
    }

}

