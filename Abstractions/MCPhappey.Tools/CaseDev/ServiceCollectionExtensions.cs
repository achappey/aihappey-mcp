using Microsoft.Extensions.DependencyInjection;

namespace MCPhappey.Tools.CaseDev;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCaseDev(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var hostHeaders = CaseDevHelpers.GetHostHeaders(headers, "api.case.dev", "case.dev");
        if (hostHeaders == null)
            return services;

        services.AddSingleton(new CaseDevSettings
        {
            BaseUrl = "https://api.case.dev",
            Headers = hostHeaders
        });

        services.AddHttpClient<CaseDevClient>();

        return services;
    }
}
