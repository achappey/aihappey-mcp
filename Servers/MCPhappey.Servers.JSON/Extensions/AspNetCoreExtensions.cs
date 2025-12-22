using MCPhappey.Common;
using MCPhappey.Servers.SQL.Providers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace MCPhappey.Servers.JSON.Extensions;

public static class AspNetCoreExtensions
{
    public static WebApplicationBuilder AddWidgetScraper(
        this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IContentScraper, WidgetScraper>();

        return builder;
    }
}