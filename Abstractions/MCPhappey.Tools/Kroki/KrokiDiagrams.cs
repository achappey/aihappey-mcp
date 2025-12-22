using System.ComponentModel;
using MCPhappey.Common.Extensions;
using MCPhappey.Tools.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MCPhappey.Core.Extensions;

namespace MCPhappey.Tools.Kroki;

public static class KrokiDiagrams
{
    [Description("Generate a Kroki diagram from code and diagram type")]
    [McpServerTool(Idempotent = true,
        OpenWorld = false,
        Destructive = false,
        Title = "Create a diagram with Kroki")]
    public static async Task<CallToolResult?> Kroki_CreateDiagram(
      [Description("Diagram type, e.g. graphviz, mermaid, plantuml, etc.")] string diagramType,
      [Description("The diagram source code (DOT, Mermaid, etc.)")] string diagramCode,
      [Description("Output file type/format, e.g. svg, png, pdf")] string fileType,
      IServiceProvider serviceProvider,
      RequestContext<CallToolRequestParams> requestContext,
      CancellationToken cancellationToken = default)
      => await requestContext.WithExceptionCheck(async () =>
    {
        if (!AllowedTypes.Contains(diagramType))
        {
            return $"Unsupported diagram type '{diagramType}'. Allowed types: {string.Join(", ", AllowedTypes)}"
                .ToErrorCallToolResponse();
        }

        var httpClientFactory = serviceProvider.GetService<IHttpClientFactory>()
            ?? throw new InvalidOperationException("No IHttpClientFactory found in service provider");
        var httpClient = httpClientFactory.CreateClient();

        var url = $"https://kroki.io/{diagramType}/{fileType}";

        // Prepare the HTTP POST request
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(diagramCode, System.Text.Encoding.UTF8, "text/plain")
        };

        var domain = new Uri(url).Host; // e.g., "example.com"
        var markdown =
            $"<details><summary>POST <a href=\"{url}\" target=\"blank\">{domain}</a></summary>\n\n```\n{diagramCode}\n```\n</details>";

        await requestContext.Server.SendMessageNotificationAsync(markdown);

        using var response = await httpClient.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        var error = await response.ToCallToolResponseOrErrorAsync(cancellationToken);
        if (error != null)
            return error;

        var fileBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        return fileBytes.ToBlobContent(url, "image/" + fileType + (fileType == "svg" ? "+xml" : string.Empty))
            .ToCallToolResult();
    });

    public static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "blockdiag",
        "bpmn",
        "bytefield",
        "seqdiag",
        "actdiag",
        "nwdiag",
        "packetdiag",
        "rackdiag",
        "c4plantuml",
        "d2",
        "dbml",
        "ditaa",
        "erd",
        "excalidraw",
        "graphviz",
        "mermaid",
        "nomnoml",
        "pikchr",
        "plantuml",
        "structurizr",
        "svgbob",
        "symbolator",
        "tikz",
        "vega",
        "vegalite",
        "wavedrom",
        "wireviz"
    };
}