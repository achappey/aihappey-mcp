using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;

namespace MCPhappey.Tests;

public sealed class McpStatelessTransportTests
{
    [Fact]
    public void Core_retains_the_SDK_stateless_transport_default()
    {
        var services = new ServiceCollection();
        services.AddMcpCoreServices([ServerHeaderAuthenticationTests.ImageServer()]);
        using var provider = services.BuildServiceProvider();
        Assert.Equal(HttpServerSessionMode.Stateless, provider.GetRequiredService<IOptions<HttpServerTransportOptions>>().Value.SessionMode);
    }
}
