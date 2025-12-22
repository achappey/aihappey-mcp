using MCPhappey.Common.Models;
using ModelContextProtocol.Server;

namespace MCPhappey.Common;

//
// Summary:
//     Interface for content scrapers
public interface IContentScraper
{
    //
    // Summary:
    //     Returns true if the scrapers supports the given host
    //
    // Parameters:
    //   host:
    //     Host name 
    //
    // Returns:
    //     Whether the host is supported
    bool SupportsHost(ServerConfig serverConfig, string host);

    //
    // Summary:
    //     Extract content from the given file.
    //
    // Parameters:
    //   mcpServer:
    //     Current MCP server
    //
    //   url:
    //     Url to download
    //
    //   cancellationToken:
    //     Async task cancellation token
    //
    // Returns:
    //     Content extracted from the url
    Task<IEnumerable<FileItem>?> GetContentAsync(McpServer mcpServer, IServiceProvider serviceProvider, string url, CancellationToken cancellationToken = default);
}