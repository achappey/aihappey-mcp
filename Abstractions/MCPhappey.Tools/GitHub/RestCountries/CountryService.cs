using System.ComponentModel;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RESTCountries.NET.Services;

namespace MCPhappey.Tools.GitHub.RestCountries;

public static class CountryService
{
    const string ICON_SOURCE = "https://api.nuget.org/v3-flatcontainer/restcountries.net/3.5.0/icon";

    [Description("Search country codes and names")]
    [McpServerTool(Title = "Search country codes and names",
       Name = "github_rest_countries_search_codes",
       IconSource = ICON_SOURCE,
       UseStructuredContent = true,
       OutputSchemaType = typeof(CountryList),
       ReadOnly = true,
       OpenWorld = false)]
    public static async Task<CallToolResult> GitHubRestCountries_SearchCodes(
       [Description("Search query by name (contains)")] string name,
       RequestContext<CallToolRequestParams> requestContext)
        => await requestContext.WithStructuredContent(async () =>
    {
        var items = string.IsNullOrEmpty(name?.ToString())
                ? RestCountriesService.GetAllCountries()
                : RestCountriesService.GetCountriesByNameContains(name?.ToString() ?? string.Empty);

        return await Task.FromResult(new CountryList
        {
            Countries = items
        });
    });


    [Description("Get all country details by the alpha-2 code")]
    [McpServerTool(Title = "Get all country details by the alpha-2 code",
        Name = "github_rest_countries_get_detail",
        IconSource = ICON_SOURCE,
        UseStructuredContent = true,
        OutputSchemaType = typeof(RESTCountries.NET.Models.Country),
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GitHubRestCountries_GetDetail(
        [Description("The alpha-2 code of the country")] string cca,
        RequestContext<CallToolRequestParams> requestContext)
            => await requestContext.WithStructuredContent(async ()
            => await Task.FromResult(RestCountriesService.GetCountryByCode(cca)
                ?? throw new Exception($"{cca} not found")));

    [Description("Get countries by region")]
    [McpServerTool(Title = "Get countries by region",
        Name = "github_rest_countries_get_by_region",
        IconSource = ICON_SOURCE,
        UseStructuredContent = true,
        OutputSchemaType = typeof(CountryList),
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GitHubRestCountries_GetByRegion(
        [Description("The region to filter on (e.g. Europe, Asia, Africa).")] string region,
        RequestContext<CallToolRequestParams> requestContext)
        => await requestContext.WithStructuredContent(async ()
            => await Task.FromResult(
            new CountryList
            {
                Countries = RestCountriesService
                            .GetAllCountries()
                            .Where(a => a.Region.Equals(region, StringComparison.OrdinalIgnoreCase))
            }
    ));

    [Description("Render a countries UI widget")]
    [McpServerTool(Title = "Countries widget",
       Name = "github_rest_countries_render_countries_widget",
       IconSource = ICON_SOURCE,
       UseStructuredContent = true,
       OutputSchemaType = typeof(CountryList),
       ReadOnly = true,
       OpenWorld = false)]
    public static async Task<CallToolResult?> GitHubRestCountries_RenderCountriesWidget(
       [Description("Comma seperated list with country codes")] string countryCodes,
       RequestContext<CallToolRequestParams> requestContext)
       => await requestContext.WithStructuredContent(async ()
           => await Task.FromResult(
           new CountryList
           {
               Countries = countryCodes.Split(",")
                            .Select(a => RestCountriesService
                            .GetCountryByCode(a))
                            .OfType<RESTCountries.NET.Models.Country>()
           }
   ));

    [Description("Render a country UI widget")]
    [McpServerTool(Title = "Country widget",
        Name = "github_rest_countries_render_country_widget",
        IconSource = ICON_SOURCE,
        UseStructuredContent = true,
        OutputSchemaType = typeof(RESTCountries.NET.Models.Country),
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GitHubRestCountries_RenderCountryWidget(
        [Description("Country code")] string countryCode,
        RequestContext<CallToolRequestParams> requestContext)
        => await requestContext.WithStructuredContent(async () =>
        {
            var country = RestCountriesService
                                .GetCountryByCode(countryCode)
                                    ?? throw new Exception($"{countryCode} not found");

            return await Task.FromResult(country);
        });

    public class CountryList
    {
        [JsonPropertyName("countries")]
        public IEnumerable<RESTCountries.NET.Models.Country>? Countries { get; set; }
    }


}

