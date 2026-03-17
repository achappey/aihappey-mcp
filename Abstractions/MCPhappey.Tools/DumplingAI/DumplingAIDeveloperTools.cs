using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.DumplingAI;

public static class DumplingAIDeveloperTools
{
    [Description("Run sandboxed JavaScript code with DumplingAI and return stdout, stderr, logs, and structured execution metadata.")]
    [McpServerTool(Title = "DumplingAI run JavaScript code", Name = "dumplingai_run_javascript", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> DumplingAI_RunJavaScript(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The JavaScript source code to execute.")] string code,
        [Description("Optional timeout in seconds.")][Range(1, 600)] int? timeout = null,
        [Description("Optional serialized stdin or input payload as plain text.")] string? input = null,
        CancellationToken cancellationToken = default)
        => await ExecuteAsync(
            serviceProvider,
            requestContext,
            "/run-js-code",
            new JsonObject
            {
                ["code"] = code,
                ["timeout"] = timeout,
                ["input"] = input
            }.WithoutNulls(),
            cancellationToken,
            "DumplingAI JavaScript execution completed.");

    [Description("Run sandboxed Python code with DumplingAI and return stdout, stderr, logs, and structured execution metadata.")]
    [McpServerTool(Title = "DumplingAI run Python code", Name = "dumplingai_run_python", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> DumplingAI_RunPython(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The Python source code to execute.")] string code,
        [Description("Optional timeout in seconds.")][Range(1, 600)] int? timeout = null,
        [Description("Optional serialized stdin or input payload as plain text.")] string? input = null,
        CancellationToken cancellationToken = default)
        => await ExecuteAsync(
            serviceProvider,
            requestContext,
            "/run-python-code",
            new JsonObject
            {
                ["code"] = code,
                ["timeout"] = timeout,
                ["input"] = input
            }.WithoutNulls(),
            cancellationToken,
            "DumplingAI Python execution completed.");

    private static async Task<CallToolResult?> ExecuteAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string endpoint,
        JsonObject payload,
        CancellationToken cancellationToken,
        string summary)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                if (string.IsNullOrWhiteSpace(payload["code"]?.GetValue<string>()))
                    throw new ValidationException("Code is required.");

                var client = serviceProvider.GetRequiredService<DumplingAIClient>();
                var response = await client.PostAsync(endpoint, payload, cancellationToken);
                var structured = DumplingAIHelpers.CreateStructuredResponse(endpoint, payload, response);

                return new CallToolResult
                {
                    Meta = await requestContext.GetToolMeta(),
                    StructuredContent = structured,
                    Content = [summary.ToTextContentBlock()]
                };
            }));
}
