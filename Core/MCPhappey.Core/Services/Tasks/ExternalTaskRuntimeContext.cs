using MCPhappey.Common.Models;

namespace MCPhappey.Core.Services.Tasks;

public sealed class ExternalTaskRuntimeContext(
    ServerConfig serverConfig,
    ServerTaskRuntimeOptions options,
    Dictionary<string, string> headers,
    TimeSpan pollInterval)
{
    public ServerConfig ServerConfig { get; } = serverConfig;
    public ServerTaskRuntimeOptions Options { get; } = options;
    public Dictionary<string, string> Headers { get; } = headers;
    public TimeSpan PollInterval { get; } = pollInterval;
}

