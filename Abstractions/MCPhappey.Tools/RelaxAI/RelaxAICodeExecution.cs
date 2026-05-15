using System.ComponentModel;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.RelaxAI;

public static class RelaxAICodeExecution
{
    [Description("Execute Python code with RelaxAI in an isolated sandbox and return stdout, stderr, exit code, timing, security violations, and generated plots as structured content.")]
    [McpServerTool(Title = "Relax AI execute Python code", Name = "relaxai_code_execute", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> RelaxAI_Code_Execute(
        [Description("Python code to execute. Must be non-empty valid Python source up to 50,000 characters.")] string code,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Programming language. Only python is currently supported. Default: python.")] string lang = "python",
        [Description("Execution timeout in seconds. Must be between 1 and 30. Default: 30.")] int? timeout = 30,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(code);
                ArgumentException.ThrowIfNullOrWhiteSpace(lang);

                if (code.Length > 50_000)
                    throw new ArgumentException("Code must be 50,000 characters or fewer.", nameof(code));

                if (!lang.Equals("python", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("RelaxAI Code Execution currently supports only lang='python'.", nameof(lang));

                if (timeout is < 1 or > 30)
                    throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be between 1 and 30 seconds.");

                var client = serviceProvider.GetRequiredService<RelaxAIClient>();
                return await client.PostJsonAsync(
                    "tools/code",
                    new RelaxAICodeExecutionRequest
                    {
                        Code = code,
                        Lang = lang.ToLowerInvariant(),
                        Timeout = timeout
                    },
                    cancellationToken);
            }));
}

internal sealed class RelaxAICodeExecutionRequest
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("lang")]
    public string Lang { get; set; } = "python";

    [JsonPropertyName("timeout")]
    public int? Timeout { get; set; }
}
