using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.SweetCLI;

public static class SweetCLIService
{
    private const string BaseUrl = "https://billing.sweetcli.com/";

    [Description("Request a SweetCLI 6-digit login code via POST /billing/request-code.")]
    [McpServerTool(
        Name = "sweetcli_billing_request_code",
        Title = "SweetCLI request login code",
        ReadOnly = true,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> SweetCLI_Billing_Request_Code(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Email address to receive the 6-digit code.")] string email,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                    new SweetCLIBillingRequestCodeInput
                    {
                        Email = email
                    },
                    cancellationToken);

                if (notAccepted != null)
                    return notAccepted;

                if (typed == null)
                    return "Elicitation was not accepted.".ToErrorCallToolResponse();

                ArgumentException.ThrowIfNullOrWhiteSpace(typed.Email);

                var payload = new JsonObject
                {
                    ["email"] = typed.Email.Trim()
                };

                var response = await PostJsonAsync("billing/request-code", payload, cancellationToken)
                               ?? new JsonObject();

                var structured = new JsonObject
                {
                    ["provider"] = "sweetcli",
                    ["endpoint"] = "/billing/request-code",
                    ["request"] = payload,
                    ["response"] = response,
                    ["status"] = "ok"
                };

                return new CallToolResult
                {
                    Meta = await requestContext.GetToolMeta(),
                    StructuredContent = structured
                };
            }));

    [Description("Verify a SweetCLI 6-digit login code and receive authentication payload via POST /billing/verify-code.")]
    [McpServerTool(
        Name = "sweetcli_billing_verify_code",
        Title = "SweetCLI verify login code",
        ReadOnly = true,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> SweetCLI_Billing_Verify_Code(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Email used in request-code.")] string email,
        [Description("6-digit verification code.")] string code,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                    new SweetCLIBillingVerifyCodeInput
                    {
                        Email = email,
                        Code = code
                    },
                    cancellationToken);

                if (notAccepted != null)
                    return notAccepted;

                if (typed == null)
                    return "Elicitation was not accepted.".ToErrorCallToolResponse();

                ArgumentException.ThrowIfNullOrWhiteSpace(typed.Email);
                ArgumentException.ThrowIfNullOrWhiteSpace(typed.Code);

                var payload = new JsonObject
                {
                    ["email"] = typed.Email.Trim(),
                    ["code"] = typed.Code.Trim()
                };

                var response = await PostJsonAsync("billing/verify-code", payload, cancellationToken)
                               ?? new JsonObject();

                var structured = new JsonObject
                {
                    ["provider"] = "sweetcli",
                    ["endpoint"] = "/billing/verify-code",
                    ["request"] = payload,
                    ["response"] = response,
                    ["status"] = "ok"
                };

                return new CallToolResult
                {
                    Meta = await requestContext.GetToolMeta(),
                    StructuredContent = structured
                };
            }));

    private static async Task<JsonNode?> PostJsonAsync(string path, JsonObject body, CancellationToken cancellationToken)
    {
        using var client = new HttpClient
        {
            BaseAddress = new Uri(BaseUrl)
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, path.TrimStart('/'))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };

        using var response = await client.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"SweetCLI call failed ({(int)response.StatusCode}): {text}");

        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }
}

[Description("Please confirm the SweetCLI request-code input before sending.")]
public sealed class SweetCLIBillingRequestCodeInput
{
    [JsonPropertyName("email")]
    [Required]
    [Description("Email address to receive the 6-digit code.")]
    public string Email { get; set; } = string.Empty;
}

[Description("Please confirm the SweetCLI verify-code input before sending.")]
public sealed class SweetCLIBillingVerifyCodeInput
{
    [JsonPropertyName("email")]
    [Required]
    [Description("Email used in request-code.")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("code")]
    [Required]
    [Description("6-digit verification code.")]
    public string Code { get; set; } = string.Empty;
}
