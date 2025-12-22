using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.EuropeanUnion;

public static class EuropeanUnionVIESService
{
    private const string BaseUrl = "https://ec.europa.eu/taxation_customs/vies/rest-api";

    // -------------------------------------------------------
    // ✅ Validate real VAT numbers (POST)
    // -------------------------------------------------------
    [Description("Validate a real EU VAT number. Requires 2-letter country code and VAT number.")]
    [McpServerTool(
        Title = "Validate VAT number",
        Name = "european_union_vies_validate_vat",
        Idempotent = true,
        ReadOnly = true)]
    public static async Task<CallToolResult?> ValidateVat(
        [Description("Country code, e.g. 'NL'")] string countryCode,
        [Description("VAT number, e.g. '123456789B01'")] string vatNumber,
        IServiceProvider sp = null!,
        RequestContext<CallToolRequestParams> rc = null!,
        CancellationToken ct = default)
        => await rc.WithExceptionCheck(async () =>
        await rc.WithStructuredContent(async () =>
    {
        var eu = sp.GetRequiredService<EuropeanUnionClient>();
        var body = new JsonObject
        {
            ["countryCode"] = countryCode,
            ["vatNumber"] = vatNumber
        };

        await NotifyCall(rc, "POST", "/check-vat-number", body.ToJsonString());
        return await eu.PostAsync("check-vat-number", body, ct);
    }));

    // -------------------------------------------------------
    // ✅ VIES system status (GET)
    // -------------------------------------------------------
    [Description("Retrieve operational status of VIES VAT services per Member State.")]
    [McpServerTool(
        Title = "Get VIES status",
        Name = "european_union_vies_get_status",
        Idempotent = true,
        ReadOnly = true)]
    public static async Task<CallToolResult?> GetStatus(
        IServiceProvider sp = null!,
        RequestContext<CallToolRequestParams> rc = null!,
        CancellationToken ct = default)
        => await rc.WithExceptionCheck(async () =>
        await rc.WithStructuredContent(async () =>
    {
        var eu = sp.GetRequiredService<EuropeanUnionClient>();
        await NotifyCall(rc, "GET", "/check-status");
        return await eu.GetAsync("check-status", ct);
    }));

    // -------------------------------------------------------
    // ✅ Integration test validation (POST) – 100=VALID, 200=INVALID
    // -------------------------------------------------------
    [Description("Test VIES integration using EC test values. 100=valid, 200=invalid.")]
    [McpServerTool(
        Title = "Test VIES validation",
        Name = "european_union_vies_validate_test",
        Idempotent = true,
        ReadOnly = true)]
    public static async Task<CallToolResult?> ValidateTest(
        [Description("VAT number: 100 or 200")] string vatNumber,
        IServiceProvider sp = null!,
        RequestContext<CallToolRequestParams> rc = null!,
        CancellationToken ct = default)
        => await rc.WithExceptionCheck(async () =>
        await rc.WithStructuredContent(async () =>
    {
        var eu = sp.GetRequiredService<EuropeanUnionClient>();
        var body = new JsonObject
        {
            ["countryCode"] = "NL", // mandatory placeholder
            ["vatNumber"] = vatNumber
        };

        await NotifyCall(rc, "POST", "/check-vat-test-service", body.ToJsonString());
        return await eu.PostAsync("check-vat-test-service", body, ct);
    }));

    // =======================================================
    // 🧠 Shared trace helpers (keep your rich trace view)
    // =======================================================
    private static async Task NotifyCall(
        RequestContext<CallToolRequestParams> rc,
        string method,
        string endpoint,
        string? json = null)
    {
        var domain = new Uri(BaseUrl).Host;
        var msg = json is null
            ? $"**{method}** `{domain}{endpoint}`"
            : $"<details><summary>{method} {domain}{endpoint}</summary>\n\n```json\n{Pretty(json)}\n```\n</details>";
        await rc.Server.SendMessageNotificationAsync(msg);
    }

    private static readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true };
    
    private static string Pretty(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            return node?.ToJsonString(jsonOptions) ?? json;
        }
        catch
        {
            return json;
        }
    }
}
