using MCPhappey.Common;
using MCPhappey.Common.Models;
using ModelContextProtocol.Server;

namespace MCPhappey.Servers.SQL.Providers;

public class WidgetScraper() : IContentScraper
{
    public bool SupportsHost(ServerConfig serverConfig, string url)
        => url.StartsWith("ui://widget", StringComparison.OrdinalIgnoreCase);

    public async Task<IEnumerable<FileItem>?> GetContentAsync(McpServer mcpServer,
        IServiceProvider serviceProvider, string url, CancellationToken cancellationToken = default)
    {
        // Example: "ui://widget/country_detail.html"
        var filename = url.Replace("ui://widget/", ""); // country_detail.html

        // Locate your widget file in build output
        // Because your csproj copies Widgets/**/*.html to output,
        // we can safely look under Servers/*/Widgets/
        var baseDir = AppContext.BaseDirectory;
        var htmlFile = Directory.GetFiles(baseDir, filename, SearchOption.AllDirectories)
                                .FirstOrDefault();

        if (htmlFile == null)
            throw new FileNotFoundException($"Widget file not found: {filename}");

        var html = await File.ReadAllTextAsync(htmlFile, cancellationToken);

        return [ new FileItem() {
           Uri = url,
           Contents = BinaryData.FromString(html),
           MimeType = "text/html+skybridge"
        }];
    }

}
