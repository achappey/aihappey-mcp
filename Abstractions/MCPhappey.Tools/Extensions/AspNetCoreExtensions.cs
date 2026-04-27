using MCPhappey.Common;
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

namespace MCPhappey.Simplicate.Extensions;

public static class AspNetCoreExtensions
{
    public static WebApplicationBuilder WithCompletion(
        this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IAutoCompletion, GraphCompletion>();
        builder.Services.AddSingleton<IAutoCompletion, CountryCompletion>();
        builder.Services.AddSingleton<IAutoCompletion, OpenAICompletion>();
        builder.Services.AddSingleton<IContentScraper, VectorStoreScraper>();
        builder.Services.AddSingleton<IContentScraper, OpenAIFilesScraper>();
        builder.Services.AddSingleton<IContentScraper, ContainerScraper>();
        builder.Services.AddSingleton<IContentScraper, AnthropicMemoryStoresScraper>();
        builder.Services.AddSingleton<IContentScraper, AnthropicSessionsScraper>();
        builder.Services.AddSingleton<IContentScraper, AnthropicVaultsScraper>();

        return builder;
    }

    public static WebApplicationBuilder WithGoogleAI(
            this WebApplicationBuilder builder,
            string apiKey)
    {
        Mscc.GenerativeAI.GoogleAI googleAI = new(apiKey);
        builder.Services.AddSingleton(googleAI);

        return builder;
    }
}
