using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Nodes;
using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using MCPhappey.Auth.Extensions;
using MCPhappey.Auth.Models;
using MCPhappey.Common;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Azure.Vault;

public static class AzureVaultSecrets
{
    [Description("Create a new Azure Key Vault secret or create a new version for an existing secret name using OBO authentication. Returns structured Key Vault metadata.")]
    [McpServerTool(Title = "Azure Vault secret set", Name = "azure_vault_secret_set", 
        ReadOnly = false, OpenWorld = true)]
    public static async Task<CallToolResult?> AzureVault_Secret_Set(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Azure Key Vault URI, for example https://my-vault.vault.azure.net/")] string vaultUri,
        [Description("Secret name.")] string secretName,
        [Description("Secret value. Using an existing secret name creates a new secret version.")] string secretValue,
        [Description("Optional content type.")] string? contentType = null,
        [Description("Optional enabled flag. When omitted, Azure default behavior is used.")] bool? enabled = null,
        [Description("Optional not-before timestamp in ISO 8601 UTC format.")] string? notBeforeUtc = null,
        [Description("Optional expires-on timestamp in ISO 8601 UTC format.")] string? expiresOnUtc = null,
        [Description("Optional tags as comma-separated key=value pairs.")] string? tagsCsv = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(vaultUri);
                ArgumentException.ThrowIfNullOrWhiteSpace(secretName);
                ArgumentException.ThrowIfNullOrWhiteSpace(secretValue);

                var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                    new AzureVaultSetSecretInput
                    {
                        VaultUri = vaultUri,
                        SecretName = secretName,
                        SecretValue = secretValue,
                        ContentType = contentType,
                        Enabled = enabled,
                        NotBeforeUtc = notBeforeUtc,
                        ExpiresOnUtc = expiresOnUtc,
                        TagsCsv = tagsCsv
                    },
                    cancellationToken);

                if (notAccepted != null) return notAccepted;
                if (typed == null) return "Elicitation was not accepted.".ToErrorCallToolResponse();

                vaultUri = typed.VaultUri;
                secretName = typed.SecretName;
                secretValue = typed.SecretValue;
                contentType = typed.ContentType;
                enabled = typed.Enabled;
                notBeforeUtc = typed.NotBeforeUtc;
                expiresOnUtc = typed.ExpiresOnUtc;
                tagsCsv = typed.TagsCsv;

                var client = await GetSecretClientAsync(serviceProvider, requestContext.Server, vaultUri, cancellationToken);

                var secret = new KeyVaultSecret(secretName, secretValue)
                {
                    Properties =
                    {
                        ContentType = NullIfWhiteSpace(contentType),
                        NotBefore = ParseDateTimeOffsetOrNull(notBeforeUtc, nameof(notBeforeUtc)),
                        ExpiresOn = ParseDateTimeOffsetOrNull(expiresOnUtc, nameof(expiresOnUtc))
                    }
                };

                if (enabled.HasValue)
                    secret.Properties.Enabled = enabled.Value;

                foreach (var tag in ParseTags(tagsCsv))
                    secret.Properties.Tags[tag.Key] = tag.Value;

                var response = await client.SetSecretAsync(secret, cancellationToken);
                var structured = BuildSetStructuredContent(vaultUri, response.Value);

                return new CallToolResult
                {
                    Meta = await requestContext.GetToolMeta(),
                    StructuredContent = structured,
                    Content = [$"Azure Key Vault secret '{response.Value.Name}' stored successfully as version '{response.Value.Properties.Version}'.".ToTextContentBlock()]
                };
            }));

    [Description("Update Azure Key Vault metadata on an existing secret version using OBO authentication. This updates secret properties only and does not change the secret value.")]
    [McpServerTool(Title = "Azure Vault secret version update", Name = "azure_vault_secret_version_update", ReadOnly = false, OpenWorld = true)]
    public static async Task<CallToolResult?> AzureVault_Secret_Version_Update(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Azure Key Vault URI, for example https://my-vault.vault.azure.net/")] string vaultUri,
        [Description("Secret name.")] string secretName,
        [Description("Secret version to update.")] string secretVersion,
        [Description("Optional content type.")] string? contentType = null,
        [Description("Optional enabled flag.")] bool? enabled = null,
        [Description("Optional not-before timestamp in ISO 8601 UTC format.")] string? notBeforeUtc = null,
        [Description("Optional expires-on timestamp in ISO 8601 UTC format.")] string? expiresOnUtc = null,
        [Description("Optional tags as comma-separated key=value pairs. When provided, replaces the version tags.")] string? tagsCsv = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(vaultUri);
                ArgumentException.ThrowIfNullOrWhiteSpace(secretName);
                ArgumentException.ThrowIfNullOrWhiteSpace(secretVersion);

                var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                    new AzureVaultUpdateSecretVersionInput
                    {
                        VaultUri = vaultUri,
                        SecretName = secretName,
                        SecretVersion = secretVersion,
                        ContentType = contentType,
                        Enabled = enabled,
                        NotBeforeUtc = notBeforeUtc,
                        ExpiresOnUtc = expiresOnUtc,
                        TagsCsv = tagsCsv
                    },
                    cancellationToken);

                if (notAccepted != null) return notAccepted;
                if (typed == null) return "Elicitation was not accepted.".ToErrorCallToolResponse();

                vaultUri = typed.VaultUri;
                secretName = typed.SecretName;
                secretVersion = typed.SecretVersion;
                contentType = typed.ContentType;
                enabled = typed.Enabled;
                notBeforeUtc = typed.NotBeforeUtc;
                expiresOnUtc = typed.ExpiresOnUtc;
                tagsCsv = typed.TagsCsv;

                var client = await GetSecretClientAsync(serviceProvider, requestContext.Server, vaultUri, cancellationToken);
                var properties = (await client.GetSecretAsync(secretName, secretVersion, cancellationToken)).Value.Properties;

                if (contentType != null)
                    properties.ContentType = NullIfWhiteSpace(contentType);

                if (enabled.HasValue)
                    properties.Enabled = enabled.Value;

                if (notBeforeUtc != null)
                    properties.NotBefore = ParseDateTimeOffsetOrNull(notBeforeUtc, nameof(notBeforeUtc));

                if (expiresOnUtc != null)
                    properties.ExpiresOn = ParseDateTimeOffsetOrNull(expiresOnUtc, nameof(expiresOnUtc));

                if (tagsCsv != null)
                {
                    properties.Tags.Clear();
                    foreach (var tag in ParseTags(tagsCsv))
                        properties.Tags[tag.Key] = tag.Value;
                }

                var response = await client.UpdateSecretPropertiesAsync(properties, cancellationToken);
                var structured = BuildPropertiesStructuredContent(vaultUri, response.Value);

                return new CallToolResult
                {
                    Meta = await requestContext.GetToolMeta(),
                    StructuredContent = structured,
                    Content = [$"Azure Key Vault secret '{response.Value.Name}' version '{response.Value.Version}' properties updated successfully.".ToTextContentBlock()]
                };
            }));

    private static async Task<SecretClient> GetSecretClientAsync(
        IServiceProvider serviceProvider,
        McpServer mcpServer,
        string vaultUri,
        CancellationToken cancellationToken)
    {
        var tokenProvider = serviceProvider.GetService<HeaderProvider>()
            ?? throw new UnauthorizedAccessException("No header provider is registered for OBO authentication.");
        var oAuthSettings = serviceProvider.GetRequiredService<OAuthSettings>();
        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var serverConfig = serviceProvider.GetServerConfig(mcpServer)
            ?? throw new InvalidOperationException("Unable to resolve the current MCP server configuration.");

        if (string.IsNullOrWhiteSpace(tokenProvider.Bearer))
            throw new UnauthorizedAccessException("An incoming bearer token is required for Azure Key Vault OBO authentication.");

        var credential = new OboTokenCredential(httpClientFactory, tokenProvider.Bearer, serverConfig.Server, oAuthSettings);
        _ = await credential.GetTokenAsync(new TokenRequestContext(["https://vault.azure.net/.default"]), cancellationToken);

        return new SecretClient(new Uri(vaultUri), credential);
    }

    private static JsonObject BuildSetStructuredContent(string vaultUri, KeyVaultSecret secret)
    {
        var structured = new JsonObject
        {
            ["provider"] = "azure-key-vault",
            ["operation"] = "set-secret",
            ["vaultUri"] = vaultUri,
            ["host"] = new Uri(vaultUri).Host,
            ["secretName"] = secret.Name,
            ["secretVersion"] = secret.Properties.Version,
            ["contentType"] = secret.Properties.ContentType,
            ["enabled"] = secret.Properties.Enabled,
            ["notBefore"] = secret.Properties.NotBefore?.ToString("O"),
            ["expiresOn"] = secret.Properties.ExpiresOn?.ToString("O"),
            ["createdOn"] = secret.Properties.CreatedOn?.ToString("O"),
            ["updatedOn"] = secret.Properties.UpdatedOn?.ToString("O"),
            ["recoverableDays"] = secret.Properties.RecoverableDays,
            ["recoveryLevel"] = secret.Properties.RecoveryLevel,
            ["managed"] = secret.Properties.Managed,
            ["tags"] = ToJsonObject(secret.Properties.Tags)
        };

        RemoveNulls(structured);
        return structured;
    }

    private static JsonObject BuildPropertiesStructuredContent(string vaultUri, SecretProperties properties)
    {
        var structured = new JsonObject
        {
            ["provider"] = "azure-key-vault",
            ["operation"] = "update-secret-properties",
            ["vaultUri"] = vaultUri,
            ["host"] = new Uri(vaultUri).Host,
            ["secretName"] = properties.Name,
            ["secretVersion"] = properties.Version,
            ["contentType"] = properties.ContentType,
            ["enabled"] = properties.Enabled,
            ["notBefore"] = properties.NotBefore?.ToString("O"),
            ["expiresOn"] = properties.ExpiresOn?.ToString("O"),
            ["createdOn"] = properties.CreatedOn?.ToString("O"),
            ["updatedOn"] = properties.UpdatedOn?.ToString("O"),
            ["recoverableDays"] = properties.RecoverableDays,
            ["recoveryLevel"] = properties.RecoveryLevel,
            ["managed"] = properties.Managed,
            ["tags"] = ToJsonObject(properties.Tags)
        };

        RemoveNulls(structured);
        return structured;
    }

    private static JsonObject ToJsonObject(IDictionary<string, string> tags)
    {
        var json = new JsonObject();
        foreach (var tag in tags)
            json[tag.Key] = tag.Value;

        return json;
    }

    private static Dictionary<string, string> ParseTags(string? tagsCsv)
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(tagsCsv))
            return tags;

        foreach (var part in tagsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = part.IndexOf('=');
            if (separatorIndex <= 0 || separatorIndex == part.Length - 1)
                throw new ArgumentException($"Invalid tag entry '{part}'. Expected key=value format.", nameof(tagsCsv));

            var key = part[..separatorIndex].Trim();
            var value = part[(separatorIndex + 1)..].Trim();

            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                throw new ArgumentException($"Invalid tag entry '{part}'. Both key and value are required.", nameof(tagsCsv));

            tags[key] = value;
        }

        return tags;
    }

    private static DateTimeOffset? ParseDateTimeOffsetOrNull(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            return parsed;

        throw new ArgumentException($"{parameterName} must be a valid ISO 8601 date/time value.", parameterName);
    }

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private static void RemoveNulls(JsonObject obj)
    {
        foreach (var property in obj.ToList())
        {
            if (property.Value is null)
            {
                obj.Remove(property.Key);
                continue;
            }

            if (property.Value is JsonObject child)
            {
                RemoveNulls(child);
                if (child.Count == 0)
                    obj.Remove(property.Key);
            }
        }
    }

    private sealed class OboTokenCredential(
        IHttpClientFactory httpClientFactory,
        string bearerToken,
        Common.Models.Server server,
        OAuthSettings oAuthSettings) : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => GetTokenAsync(requestContext, cancellationToken).GetAwaiter().GetResult();

        public override async ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            var scopeHost = ResolveScopeHost(requestContext);
            var delegated = await httpClientFactory.GetOboToken(bearerToken, scopeHost, server, oAuthSettings);
            return new AccessToken(delegated, DateTimeOffset.UtcNow.AddMinutes(55));
        }

        private static string ResolveScopeHost(TokenRequestContext requestContext)
        {
            var scope = requestContext.Scopes.FirstOrDefault()
                ?? throw new UnauthorizedAccessException("No scope was requested for Azure Key Vault authentication.");

            if (scope.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                && Uri.TryCreate(scope.Replace("/.default", string.Empty), UriKind.Absolute, out var uri))
            {
                return uri.Host;
            }

            return scope;
        }
    }

    [Description("Confirm Azure Key Vault secret write settings before storing the secret value.")]
    public sealed class AzureVaultSetSecretInput
    {
        [Description("Azure Key Vault URI, for example https://my-vault.vault.azure.net/")]
        public string VaultUri { get; set; } = string.Empty;

        [Description("Secret name.")]
        public string SecretName { get; set; } = string.Empty;

        [Description("Secret value. Using an existing secret name creates a new secret version.")]
        public string SecretValue { get; set; } = string.Empty;

        [Description("Optional content type.")]
        public string? ContentType { get; set; }

        [Description("Optional enabled flag. When omitted, Azure default behavior is used.")]
        public bool? Enabled { get; set; }

        [Description("Optional not-before timestamp in ISO 8601 UTC format.")]
        public string? NotBeforeUtc { get; set; }

        [Description("Optional expires-on timestamp in ISO 8601 UTC format.")]
        public string? ExpiresOnUtc { get; set; }

        [Description("Optional tags as comma-separated key=value pairs.")]
        public string? TagsCsv { get; set; }
    }

    [Description("Confirm Azure Key Vault secret version property updates before applying metadata changes.")]
    public sealed class AzureVaultUpdateSecretVersionInput
    {
        [Description("Azure Key Vault URI, for example https://my-vault.vault.azure.net/")]
        public string VaultUri { get; set; } = string.Empty;

        [Description("Secret name.")]
        public string SecretName { get; set; } = string.Empty;

        [Description("Secret version to update.")]
        public string SecretVersion { get; set; } = string.Empty;

        [Description("Optional content type.")]
        public string? ContentType { get; set; }

        [Description("Optional enabled flag.")]
        public bool? Enabled { get; set; }

        [Description("Optional not-before timestamp in ISO 8601 UTC format.")]
        public string? NotBeforeUtc { get; set; }

        [Description("Optional expires-on timestamp in ISO 8601 UTC format.")]
        public string? ExpiresOnUtc { get; set; }

        [Description("Optional tags as comma-separated key=value pairs. When provided, replaces the version tags.")]
        public string? TagsCsv { get; set; }
    }
}
