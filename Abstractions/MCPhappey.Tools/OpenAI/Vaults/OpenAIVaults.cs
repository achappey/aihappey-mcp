using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.OpenAI.AgentsApi;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.OpenAI.Vaults;

public static partial class OpenAIVaults
{
    internal const string BaseUrl = $"{OpenAIAgentsHttp.ApiBaseUrl}/vaults";

    [Description("Create an OpenAI project vault. Optional metadata is loaded from a JSON object file so metadata remains structured without a JSON-string tool parameter.")]
    [McpServerTool(Title = "Create OpenAI Vault", Name = "openai_vaults_create", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenAIVaults_Create(
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        string? name = null, string? metadataFileUrl = null,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            var (input, rejected, _) = await requestContext.Server.TryElicit(new CreateVaultRequest
            { Name = name, MetadataFileUrl = metadataFileUrl }, cancellationToken);
            if (rejected is not null) return rejected;
            return await requestContext.WithStructuredContent(async () =>
            {
                if (input.Name is not null && string.IsNullOrWhiteSpace(input.Name))
                    throw new ValidationException("name cannot be blank when supplied.");
                var body = new JsonObject();
                if (input.Name is not null) body["name"] = input.Name.Trim();
                if (input.MetadataFileUrl is not null)
                {
                    var files = await serviceProvider.GetRequiredService<DownloadService>()
                        .DownloadContentAsync(serviceProvider, requestContext.Server, input.MetadataFileUrl, cancellationToken);
                    var file = files.FirstOrDefault() ?? throw new ValidationException("The metadata file could not be downloaded.");
                    body["metadata"] = OpenAIAgentsHttp.ParseObject(file.Contents.ToString(), "metadata file");
                }
                return await OpenAIAgentsHttp.SendAsync(serviceProvider, HttpMethod.Post, BaseUrl, body, cancellationToken);
            });
        });

    [Description("Permanently delete an OpenAI vault and all credentials after typed confirmation.")]
    [McpServerTool(Title = "Delete OpenAI Vault", Name = "openai_vaults_delete", ReadOnly = false, OpenWorld = false, Destructive = true)]
    public static async Task<CallToolResult?> OpenAIVaults_Delete(
        string vaultId, IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            Required(vaultId, "vaultId");
            return await requestContext.ConfirmAndDeleteAsync<DeleteItem>(vaultId,
                async ct => await OpenAIAgentsHttp.SendAsync(serviceProvider, HttpMethod.Delete, VaultUrl(vaultId), null, ct),
                $"OpenAI vault '{vaultId}' deleted successfully.", cancellationToken);
        });

    internal static string VaultUrl(string vaultId) => $"{BaseUrl}/{Uri.EscapeDataString(vaultId)}";
    internal static string CredentialsUrl(string vaultId) => $"{VaultUrl(vaultId)}/credentials";
    internal static string CredentialUrl(string vaultId, string credentialId) => $"{CredentialsUrl(vaultId)}/{Uri.EscapeDataString(credentialId)}";

    private static void Required(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ValidationException($"{name} is required.");
    }

    private static void ValidateTimestamp(string? value, string name)
    {
        if (value is not null && !DateTimeOffset.TryParse(value, out _))
            throw new ValidationException($"{name} must be an RFC 3339 timestamp.");
    }

    private static void ValidateTokenAuth(string? value)
    {
        if (value is not null && value is not ("none" or "client_secret_basic" or "client_secret_post"))
            throw new ValidationException("tokenEndpointAuth must be none, client_secret_basic, or client_secret_post.");
    }
}
