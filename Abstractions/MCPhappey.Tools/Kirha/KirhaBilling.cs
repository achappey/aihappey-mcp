using System.ComponentModel;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Kirha;

public static class KirhaBilling
{
    [Description("Initiate a Kirha account top-up and return payment instructions as structured content.")]
    [McpServerTool(Title = "Kirha account top-up", Name = "kirha_billing_account_topup", ReadOnly = true, OpenWorld = true, UseStructuredContent = true)]
    public static async Task<CallToolResult?> AccountTopup(
        [Description("Top-up type. Currently only 'crypto' is supported.")] string type,
        [Description("Currency identifier, for example 'eth'.")] string currencyId,
        [Description("Number of credits to purchase.")] long credits,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(type);
                ArgumentException.ThrowIfNullOrWhiteSpace(currencyId);

                var client = serviceProvider.GetRequiredService<KirhaClient>();
                var body = new JsonObject
                {
                    ["type"] = type,
                    ["currency_id"] = currencyId,
                    ["credits"] = credits
                };

                return await client.PostBillingAsync("account/topup", body, cancellationToken)
                    ?? throw new Exception("Kirha returned no response.");
            }));

    [Description("Confirm a Kirha account top-up using the blockchain transaction hash.")]
    [McpServerTool(Title = "Kirha account top-up complete", Name = "kirha_billing_account_topup_complete", ReadOnly = true, OpenWorld = true, UseStructuredContent = true)]
    public static async Task<CallToolResult?> AccountTopupComplete(
        [Description("Identifier of the created top-up.")] string id,
        [Description("Blockchain transaction hash for the payment.")] string transactionHash,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(id);
                ArgumentException.ThrowIfNullOrWhiteSpace(transactionHash);

                var client = serviceProvider.GetRequiredService<KirhaClient>();
                await client.PostBillingNoContentAsync($"account/topup/{Uri.EscapeDataString(id)}/complete",
                    new JsonObject
                    {
                        ["transaction_hash"] = transactionHash
                    }, cancellationToken);

                return new JsonObject
                {
                    ["id"] = id,
                    ["transaction_hash"] = transactionHash,
                    ["status"] = "confirmed"
                };
            }));
}
