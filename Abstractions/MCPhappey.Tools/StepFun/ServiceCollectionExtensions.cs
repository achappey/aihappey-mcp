using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.StepFun;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddStepFun(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key == "api.stepfun.ai")
            .Value?
            .FirstOrDefault(h => h.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new StepFunSettings { ApiKey = key });
        return services;
    }
}

public sealed class StepFunSettings
{
    public string ApiKey { get; set; } = default!;
}

