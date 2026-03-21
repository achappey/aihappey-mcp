using Microsoft.Extensions.DependencyInjection;

namespace MCPhappey.Tools.Azure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAzureSkills(this IServiceCollection services, SkillsStorageSettings? settings)
    {
        if (settings?.IsConfigured != true)
            return services;

        services.AddSingleton(settings);
        services.AddSingleton<AzureSkillsStorageService>();
        return services;
    }
}
