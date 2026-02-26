using Microsoft.Extensions.DependencyInjection;

namespace MCPhappey.Tools.OCRSpace;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOCRSpace(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key == "api.ocr.space")
            .Value?
            .FirstOrDefault(h => h.Key == "apikey")
            .Value?
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new OCRSpaceSettings { ApiKey = key });
        services.AddHttpClient<OCRSpaceClient>();

        return services;
    }
}

