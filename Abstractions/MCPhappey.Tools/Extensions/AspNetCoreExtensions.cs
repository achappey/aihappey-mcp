using MCPhappey.Common;
using MCPhappey.Tools;
using MCPhappey.Tools.GitHub.RestCountries;
using MCPhappey.Tools.Graph;
using MCPhappey.Tools.OpenAI;
using MCPhappey.Tools.OpenAI.Containers;
using MCPhappey.Tools.OpenAI.Files;
using MCPhappey.Tools.OpenAI.VectorStores;
using MCPhappey.Tools.Anthropic.MemoryStores;
using MCPhappey.Tools.Anthropic.Sessions;
using MCPhappey.Tools.Anthropic.Vaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using MCPhappey.Tools.Google;
using MCPhappey.Tools.BlackForestLabs;
using MCPhappey.Tools.SandBase;

namespace MCPhappey.Simplicate.Extensions;

public static class AspNetCoreExtensions
{
    public static WebApplicationBuilder WithCompletion(
        this WebApplicationBuilder builder)
    {
        builder.Services.AddDualModeAgentAccess();
        builder.Services.AddGoogleAI();
        builder.Services.AddBlackForestLabs();
        builder.Services.AddSingleton<IAutoCompletion, GraphCompletion>();
        builder.Services.AddSingleton<IAutoCompletion, CountryCompletion>();
        builder.Services.AddSingleton<IAutoCompletion, OpenAICompletion>();
        builder.Services.AddSingleton<IContentScraper, VectorStoreScraper>();
        builder.Services.AddSingleton<IContentScraper, OpenAIFilesScraper>();
        builder.Services.AddSingleton<IContentScraper, ContainerScraper>();
        builder.Services.AddSingleton<IContentScraper, AnthropicMemoryStoresScraper>();
        builder.Services.AddSingleton<IContentScraper, AnthropicSessionsScraper>();
        builder.Services.AddSingleton<IContentScraper, AnthropicVaultsScraper>();
        builder.Services.AddSingleton<IContentScraper, SandBaseScraper>();

        return builder;
    }

    public static WebApplicationBuilder WithGoogleAI(
            this WebApplicationBuilder builder,
            string? apiKey = null)
    {
        builder.Services.AddGoogleAI(apiKey);
        return builder;
    }
}
