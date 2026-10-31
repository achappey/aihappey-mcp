using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using MCPhappey.Auth;
using MCPhappey.Common.Models;
using MCPhappey.Tools.Anthropic;
using MCPhappey.Tools.Anthropic.Skills;
using MCPhappey.Tools.OpenAI.AgentsApi;
using MCPhappey.Tools.OpenAI.Skills;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace MCPhappey.Tools;

/// <summary>One credential-isolated transport for agent mutations and MCP resource reads.</summary>
public static class AgentProviderHttp
{
    public const string ClientName = "MCPhappey.AgentProviders";

    internal static ServerConfig? GetRequestConfig(IServiceProvider services, string serverName)
    {
        var context = services.GetService<IHttpContextAccessor>()?.HttpContext;
        if (context?.Items[typeof(ServerRequestAccess)] is ServerRequestAccess access)
            return access.Config.Server.ServerInfo.Name.Equals(serverName, StringComparison.OrdinalIgnoreCase)
                ? access.Config : null;

        // Missing access state must not turn a target HTTP request into legacy operator-key access.
        // Unrelated consumers of the shared helpers retain their existing behavior.
        var requestedServer = context?.Request.Path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.Equals(requestedServer, serverName, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Validated MCP request access is required.");
        return null;
    }

    public static bool IsAgentServer(ServerConfig config)
        => config.Server.ServerInfo.Name.Equals("OpenAI-Agents", StringComparison.OrdinalIgnoreCase)
            || config.Server.ServerInfo.Name.Equals("Anthropic-Agents", StringComparison.OrdinalIgnoreCase);

    public static string GetProviderHost(ServerConfig config)
        => config.Server.ServerInfo.Name.Equals("OpenAI-Agents", StringComparison.OrdinalIgnoreCase)
            ? "api.openai.com"
            : config.Server.ServerInfo.Name.Equals("Anthropic-Agents", StringComparison.OrdinalIgnoreCase)
                ? AnthropicHeaders.ApiHost
                : throw new InvalidOperationException("This server does not expose agent provider resources.");

    public static void ValidateUrl(ServerConfig config, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || uri.Port != 443 || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment)
            || !uri.Host.Equals(GetProviderHost(config), StringComparison.OrdinalIgnoreCase)
            || !(uri.AbsolutePath.Equals("/v1/agents", StringComparison.Ordinal)
                || uri.AbsolutePath.StartsWith("/v1/agents/", StringComparison.Ordinal))
            || uri.AbsolutePath.Contains('%') || uri.AbsolutePath.Contains('\\'))
            throw new UnauthorizedAccessException("Agent credentials can only be sent to this server's HTTPS agent API endpoints.");
    }

    public static async Task<JsonNode> SendAsync(IServiceProvider services, ServerConfig config,
        HttpMethod method, string url, JsonNode? body, CancellationToken cancellationToken = default)
    {
        ValidateUrl(config, url);
        var openAi = GetProviderHost(config) == "api.openai.com";
        var apiKey = ProviderApiKeyResolver.Resolve(services, config,
            openAi ? "openai_api_key" : "anthropic_api_key",
            () => openAi ? services.GetService<OpenAISettings>()?.ApiKey : services.GetService<AnthropicSettings>()?.ApiKey);
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (openAi)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.Add(OpenAIAgentsHttp.BetaHeaderName, OpenAIAgentsHttp.BetaHeaderValue);
        }
        else
        {
            request.Headers.Add(AnthropicHeaders.ApiKeyHeader, apiKey);
            request.Headers.Add(AnthropicHeaders.AnthropicVersionHeader, AnthropicHeaders.AnthropicVersion);
            request.Headers.Add(AnthropicHeaders.AnthropicBetaHeader, AnthropicHeaders.BuildManagedAgentsBetaHeader(null));
        }
        if (body is not null)
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);
        using var response = await client.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Agent API returned {(int)response.StatusCode} {response.StatusCode}: {text}", null, response.StatusCode);
        return string.IsNullOrWhiteSpace(text) ? new JsonObject()
            : JsonNode.Parse(text) ?? throw new InvalidOperationException("The agent API response was not valid JSON.");
    }
}
