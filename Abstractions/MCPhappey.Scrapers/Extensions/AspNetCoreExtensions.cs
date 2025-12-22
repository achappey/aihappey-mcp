using MCPhappey.Auth.Models;
using MCPhappey.Common;
using MCPhappey.Common.Constants;
using MCPhappey.Common.Models;
using MCPhappey.Scrapers.Generic;
using MCPhappey.Scrapers.Microsoft;
using Microsoft.Extensions.DependencyInjection;

namespace MCPhappey.Scrapers.Extensions;

public static class AspNetCoreExtensions
{
    public static IServiceCollection WithOboScrapers(
        this IServiceCollection services,
        IEnumerable<ServerConfig> servers,
        OAuthSettings oAuthSettings)
    {

        if (oAuthSettings != null)
        {
            foreach (var server in servers)
            {


                if (server.Server.OBO?.Values.Any(a => a.EndsWith(".sharepoint.com/.default")) == true)
                {
                    services.AddSingleton<IContentScraper>(sp =>
                   {
                       var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();

                       return new SharePointRESTScraper(httpClientFactory, server, oAuthSettings);
                   });
                }
                else
                {
                    if (server.Server.OBO?.Any() == true)
                    {
                        services.AddSingleton<IContentScraper>(sp =>
                        {
                            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();

                            return new OboClientScraper(httpClientFactory, server, oAuthSettings);
                        });
                    }
                }


                if (server.Server.OBO?.ContainsKey(Hosts.MicrosoftGraph) == true)
                {
                    var scopes = server.Server.OBO[Hosts.MicrosoftGraph];

                    // if (scopes.Contains("Sites."))
                    if (scopes.Contains("Sites.") || server.Server.OBO.Keys.Any(h =>
                        h.EndsWith(".sharepoint.com", StringComparison.OrdinalIgnoreCase)))
                    {
                        services.AddSingleton<IContentScraper>(sp =>
                        {
                            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();

                            return new SharePointScraper(httpClientFactory, server, oAuthSettings);
                        });
                    }

                    if (scopes.Contains("Mail."))
                    {
                        services.AddSingleton<IContentScraper>(sp =>
                        {
                            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();

                            return new OutlookScraper(httpClientFactory, server, oAuthSettings);
                        });
                    }

                    if (scopes.Contains("ChannelMessage.") || scopes.Contains("Chat."))
                    {
                        services.AddSingleton<IContentScraper>(sp =>
                        {
                            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();

                            return new TeamsScraper(httpClientFactory, server, oAuthSettings);
                        });
                    }

                    if (scopes.Contains("Tasks.") || scopes.Contains("Group."))
                    {
                        services.AddSingleton<IContentScraper>(sp =>
                        {
                            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
                            return new PlannerScraper(httpClientFactory, server, oAuthSettings);
                        });
                    }
                }

                if (server.Server.OBO?.ContainsKey("service.flow.microsoft.com") == true)
                {
                    services.AddSingleton<IContentScraper>(sp =>
                       {
                           var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();

                           return new PowerAutomateScraper(httpClientFactory, server, oAuthSettings);
                       });
                }
            }

        }

        return services;
    }

    public static IServiceCollection WithHostScrapers(
        this IServiceCollection services,
        Dictionary<string, Dictionary<string, string>>? domainHeaders = null,
        Dictionary<string, Dictionary<string, string>>? domainQueries = null)
    {
        foreach (var host in domainHeaders ?? [])
        {
            services.AddSingleton<IContentScraper>(sp =>
            {
                var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
                return new StaticHeaderScraper(httpClientFactory, host.Key, host.Value);
            });
        }

        foreach (var host in domainQueries ?? [])
        {
            services.AddSingleton<IContentScraper>(sp =>
            {
                var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
                return new StaticQueryScraper(httpClientFactory, host.Key, host.Value);
            });
        }

        return services;
    }

    public static IServiceCollection WithDefaultScrapers(
       this IServiceCollection services)
    {
        services.AddSingleton<IContentScraper, DynamicHeaderScraper>();
        services.AddSingleton<IContentScraper, McpStatisticsScraper>();
        services.AddSingleton<IContentScraper, NominatimScraper>();

        return services;
    }
}