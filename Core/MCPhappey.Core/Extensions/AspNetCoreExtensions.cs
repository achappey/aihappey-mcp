using MCPhappey.Common;
using MCPhappey.Common.Models;
using MCPhappey.Core.Services;
using MCPhappey.Telemetry.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.DataFormats.WebPages;
using Microsoft.ML.Tokenizers;

namespace MCPhappey.Core.Extensions;

public static class AspNetCoreExtensions
{
    public static IServiceCollection AddMcpCoreServices(
        this IServiceCollection services,
        List<ServerConfig> servers,
        string? telemetryDatabase = null)
    {
        services.AddHttpContextAccessor();

        services.AddSingleton<TransformService>();
        services.AddSingleton<DownloadService>();
        services.AddScoped<PromptService>();
        services.AddScoped<SamplingService>();
        services.AddScoped<UploadService>();
        services.AddScoped<ResourceService>();
        services.AddSingleton<CompletionService>();

        services.AddSingleton<IReadOnlyList<ServerConfig>>(servers);
        services.AddSingleton<WebScraper>();
        services.AddScoped<HeaderProvider>();
        services.AddSingleton(new GptTokenizer(TiktokenTokenizer.CreateForModel("gpt-4o")));

        services.AddHttpClient();
        services.AddLogging();
        services.AddKernel();

        services.AddMcpServer()
            .WithConfigureSessionOptions(servers);

        if (telemetryDatabase != null)
        {
            services.AddTelemetryServices(telemetryDatabase);
        }

        return services;
    }
}