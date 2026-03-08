using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.PDFData;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPDFData(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var domainHeaders = headers?
            .FirstOrDefault(h => h.Key.Equals("pdfdata.io", StringComparison.OrdinalIgnoreCase))
            .Value;

        var apiKey = domainHeaders?
            .FirstOrDefault(h => h.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(apiKey))
            return services;

        services.AddSingleton(new PDFDataSettings { ApiKey = apiKey });
        services.AddHttpClient<PDFDataClient>();

        return services;
    }
}
