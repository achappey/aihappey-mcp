using System.ComponentModel;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RESTCountries.NET.Services;

namespace MCPhappey.Tools.GitHub.RestCountries;

public static class CountryService
{
    const string SOURCE_URL = "https://github.com/egbakou/RESTCountries.NET";

    const string ICON_SOURCE = "https://api.nuget.org/v3-flatcontainer/restcountries.net/3.5.0/icon";

    [Description("Search country codes and names")]
    [McpServerTool(Title = "Search country codes and names",
        Name = "github_rest_countries_search_codes",
        IconSource = ICON_SOURCE,
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<EmbeddedResourceBlock> GitHubRestCountries_SearchCodes(
        [Description("Search query by name (contains)")] string name)
    {
        var items = string.IsNullOrEmpty(name?.ToString())
                ? RestCountriesService.GetAllCountries()
                : RestCountriesService.GetCountriesByNameContains(name?.ToString() ?? string.Empty);

        return await Task.FromResult(items
            .Select(t => new { t.Name.Common, t.Name.Official, t.Cca2 })
            .ToJsonContentBlock(SOURCE_URL));
    }

    [Description("Get all country details by the alpha-2 code")]
    [McpServerTool(Title = "Get all country details by the alpha-2 code",
        Name = "github_rest_countries_get_detail",
        IconSource = ICON_SOURCE,
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GitHubRestCountries_GetDetail(
        [Description("The alpha-2 code of the country")] string cca,
        RequestContext<CallToolRequestParams> requestContext) => await requestContext.WithStructuredContent(async () =>
            await Task.FromResult(RestCountriesService.GetCountryByCode(cca.ToString())));

    [Description("Get countries by region")]
    [McpServerTool(Title = "Get countries by region",
        Name = "github_rest_countries_get_by_region",
        IconSource = ICON_SOURCE,
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GitHubRestCountries_GetByRegion(
        [Description("The region to filter on (e.g. Europe, Asia, Africa).")] string region,
        RequestContext<CallToolRequestParams> requestContext) =>
            await requestContext.WithStructuredContent(async () => await Task.FromResult(
            new
            {
                countries = RestCountriesService
                            .GetAllCountries()
                            .Where(a => a.Region.Equals(region, StringComparison.OrdinalIgnoreCase))
            }
    ));
}

