using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MCPhappey.Tools;

public static class AgentProviderAccessExtensions
{
    public static IServiceCollection AddDualModeAgentAccess(this IServiceCollection services)
    {
        services.TryAddSingleton<PublicFileDownloader>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRequestContentDownloader, AgentContentDownloader>());
        services.AddHttpClient(AgentProviderHttp.ClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        return services;
    }
}
