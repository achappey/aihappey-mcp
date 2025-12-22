using Azure.Identity;
using MCPhappey.Auth.Models;
using MCPhappey.Common;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Simplicate.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace MCPhappey.Simplicate.Extensions;

//
// Summary:
//     ASP-NET Core helper to register Simplicate content-scraper plus the
//     required Azure Key Vault SecretClient.
//
public static class AspNetCoreExtensions
{
    //
    // Summary:
    //     Adds Simplicate scraper and Key Vault support.
    //
    // Parameters:
    //   services:
    //     Service-collection.
    //
    //   configurationSection:
    //     Configuration section that binds to SimplicateOptions (requires at least
    //     "KeyVaultUri").
    //
    // Returns:
    //     IServiceCollection for chaining.
    //
    public static WebApplicationBuilder WithSimplicateScraper(
        this WebApplicationBuilder builder,
        SimplicateOptions options,
        OAuthSettings? oAuthSettings = null)
    {
        builder.Services.AddSingleton(options);

        // Register SecretClient via Microsoft.Extensions.Azure so that it can be
        // consumed through DI (lifetime managed by Azure SDK).
        if (oAuthSettings != null)
        {
            builder.Services.AddAzureClients(builder =>
            {
                builder.AddSecretClient(new Uri(options.KeyVaultUri))
                       .WithCredential(new ClientSecretCredential(oAuthSettings.TenantId,
                            oAuthSettings.ClientId, oAuthSettings.ClientSecret));
            });
        }

        // Ensure IHttpClientFactory is available.
        builder.Services.AddHttpClient();

        // Register scraper.
        builder.Services.AddSingleton<IContentScraper, SimplicateScraper>();
        builder.Services.AddSingleton<IAutoCompletion, SimplicateCompletion>();

        return builder;
    }

    public static void ApplySimplicateOrganization(
     this IEnumerable<ServerConfig> servers,
     string organization)
    {
        foreach (var server in servers.Where(s =>
                 s.Server.ServerInfo.Name.StartsWith("Simplicate-",
                    StringComparison.OrdinalIgnoreCase)))
        {
            var originalTemplates = server.ResourceTemplateList?.ResourceTemplates;
            if (originalTemplates == null) continue;

            var newResources = new List<Resource>();

            var updatedTemplates = originalTemplates
                .Select(t =>
                {
                    if (!t.UriTemplate.Contains("{organization}"))
                        return t;

                    var concreteUri = t.UriTemplate.Replace("{organization}", organization);

                    if (concreteUri.CountPromptArguments() == 0)
                    {
                        newResources.Add(new Resource
                        {
                            Uri = concreteUri,
                            Name = t.Name,
                            Description = t.Description,
                            MimeType = t.MimeType,
                            Annotations = t.Annotations
                        });

                        return null; // we filter 'm straks weg
                    }
                    else
                    {
                        return new ResourceTemplate()
                        {
                            UriTemplate = concreteUri,
                            Name = t.Name,
                            Description = t.Description,
                            MimeType = t.MimeType,
                            Annotations = t.Annotations
                        };

                    }
                })
                .OfType<ResourceTemplate>()
                .ToList()!;

            server.ResourceTemplateList ??= new();
            server.ResourceTemplateList.ResourceTemplates = updatedTemplates;
            server.ResourceList ??= new();
            server.ResourceList.Resources = [.. server.ResourceList.Resources, .. newResources];
        }
    }
}
