using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Together.Images;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Together.CodeInterpreter;

public static class TogetherCodeInterpreter
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TogetherTciLanguage
    {
        python
    }

    // ========= Tools =========

    [Description("Execute code using Together Code Interpreter (tci/execute). Supports persistent sessions and file uploads.")]
    [McpServerTool(
        Title = "Execute with Together Code Interpreter",
        Name = "together_code_interpreter_execute",
        ReadOnly = false,
        OpenWorld = false)]
    public static async Task<CallToolResult?> TogetherCodeInterpreter_Execute(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        // Convenience fast-path parameters (optional); if omitted, we’ll elicit the full structured form.
        [Description("Inline code snippet to execute. If omitted, you’ll be asked for details.")]
        string code,
        [Description("Language (default: python).")]
        TogetherTciLanguage language = TogetherTciLanguage.python,
        [Description("Optional Together Code Interpreter session id to reuse.")]
        string? sessionId = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

            // 2) Construct Together API payload
            var payload = new JsonObject
            {
                ["code"] = code,
                ["language"] = language.ToString(),
            };

            if (!string.IsNullOrWhiteSpace(sessionId))
                payload["session_id"] = sessionId;

            var options = new JsonSerializerOptions
            {
                //   Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            // 3) Call Together API
            using var client = serviceProvider.CreateTogetherClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.together.xyz/v1/tci/execute");
            request.Content = new StringContent(payload.ToJsonString(options), Encoding.UTF8, MimeTypes.Json);

            using var resp = await client.SendAsync(request, cancellationToken);
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"{resp.StatusCode}: {json}");

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // 4) Normalize response → { session_id, status, outputs[] }
            // API model: { data: { outputs: [ {type:'stdout'|'stderr', data:'...'} ], session_id:'...', status:'success' }, errors: null }
            string? outSessionId = null;
            string? status = null;
            var outputsArray = new JsonArray();

            if (root.TryGetProperty("data", out var dataObj) && dataObj.ValueKind == JsonValueKind.Object)
            {
                if (dataObj.TryGetProperty("session_id", out var sid) && sid.ValueKind == JsonValueKind.String)
                    outSessionId = sid.GetString();

                if (dataObj.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String)
                    status = s.GetString();

                if (dataObj.TryGetProperty("outputs", out var outs) && outs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var o in outs.EnumerateArray())
                    {
                        var type = o.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                            ? t.GetString()
                            : null;
                        var data = o.TryGetProperty("data", out var d) && d.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False
                            ? d.ToString()
                            : o.TryGetProperty("data", out var d2) ? d2.GetRawText() : null;

                        outputsArray.Add(new JsonObject
                        {
                            ["type"] = type ?? "stdout",
                            ["data"] = data ?? string.Empty
                        });
                    }
                }
            }

            // 5) Also build a friendly concatenated text view
            var sb = new StringBuilder();
            foreach (var node in outputsArray)
            {
                if (node is not JsonObject obj) continue;
                var t = obj["type"]?.GetValue<string>() ?? "stdout";
                var d = obj["data"]?.GetValue<string>() ?? "";
                sb.AppendLine($"[{t}]");
                sb.AppendLine(d);
                sb.AppendLine();
            }

            var structured = new JsonObject
            {
                ["session_id"] = outSessionId ?? sessionId ?? "",
                ["status"] = status ?? "success",
                ["outputs"] = outputsArray
            };

            // 6) Return both JSON (machine-usable) and a readable text block (human-usable)
            return structured.ToJsonString().ToTextContentBlock().ToCallToolResult();
        });


}


