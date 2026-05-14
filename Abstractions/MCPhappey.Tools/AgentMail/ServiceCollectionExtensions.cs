using Microsoft.Extensions.DependencyInjection;

namespace MCPhappey.Tools.AgentMail;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAgentMail(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var hostHeaders = AgentMailHelpers.GetHostHeaders(headers, "api.agentmail.to", "api.agentmail.dev", "agentmail.to", "agentmail.dev");
        if (hostHeaders == null)
            return services;

        var baseUrl = headers?.Keys.Any(k => k.Equals("api.agentmail.dev", StringComparison.OrdinalIgnoreCase)
                                             || k.Equals("agentmail.dev", StringComparison.OrdinalIgnoreCase)) == true
            ? "https://api.agentmail.dev"
            : "https://api.agentmail.to";

        services.AddSingleton(new AgentMailSettings
        {
            BaseUrl = baseUrl,
            Headers = hostHeaders
        });

        services.AddHttpClient<AgentMailClient>();

        return services;
    }
}
