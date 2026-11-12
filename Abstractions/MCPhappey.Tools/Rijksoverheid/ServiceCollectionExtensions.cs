using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MCPhappey.Tools.Rijksoverheid;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOmgevingswet(this IServiceCollection services)
    {
        services.TryAddSingleton<OmgevingswetClient>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRequestContentDownloader, OmgevingswetContentDownloader>());
        services.AddHttpClient(OmgevingswetClient.ClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        return services;
    }
}
