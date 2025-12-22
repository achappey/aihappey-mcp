using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json.Nodes;
using MCPhappey.Common.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Common.Extensions;

public static class ToolExtensions
{

    public static CallToolResult ToCallToolResponse(this IEnumerable<ContentBlock> content)
        => new()
        {
            Content = [.. content]
        };

    public static CallToolResult ToCallToolResponse(this JsonNode content)
        => new()
        {
            StructuredContent = content
        };


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

