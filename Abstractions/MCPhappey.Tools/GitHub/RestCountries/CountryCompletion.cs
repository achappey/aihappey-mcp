using MCPhappey.Common;
using MCPhappey.Common.Models;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using RESTCountries.NET.Services;

namespace MCPhappey.Tools.GitHub.RestCountries;

public class CountryCompletion : IAutoCompletion
{
    public bool SupportsHost(ServerConfig serverConfig)
        => serverConfig.Server.ServerInfo.Name.Equals("GitHub-RestCountries", StringComparison.OrdinalIgnoreCase);

    public async Task<Completion> GetCompletion(
     McpServer mcpServer,
     IServiceProvider serviceProvider,
     CompleteRequestParams? completeRequestParams,
     CancellationToken cancellationToken = default)
    {
        if (completeRequestParams?.Argument?.Name is not string argName || completeRequestParams.Argument.Value is not string argValue)
            return new();

        IEnumerable<string> result = [];

        switch (completeRequestParams.Argument.Name)
        {
            case "countryA":
                // Als countryB al is ingevuld, filter die weg
                var excludeB = completeRequestParams.Context?.Arguments?.TryGetValue("countryB", out var countryBValue) == true ? countryBValue : null;
                result = RestCountriesService.GetCountriesByNameContains(argValue ?? string.Empty)?
                                            .Select(a => a.Name.Official)
                                            .Where(name => !string.Equals(name, excludeB, StringComparison.OrdinalIgnoreCase))
                                            .OfType<string>()
                                            .Take(100)
                                            .Order()
                                            .ToList() ?? [];
                break;
            case "countryB":
                // Als countryA al is ingevuld, filter die weg
                var excludeA = completeRequestParams.Context?.Arguments?.TryGetValue("countryA", out var countryAValue) == true ? countryAValue : null;
                result = RestCountriesService.GetCountriesByNameContains(argValue ?? string.Empty)?
                                            .Select(a => a.Name.Official)
                                            .Where(name => !string.Equals(name, excludeA, StringComparison.OrdinalIgnoreCase))
                                            .OfType<string>()
                                            .Take(100)
                                            .Order()
                                            .ToList() ?? [];
                break;
            case "countryName":
                result = RestCountriesService.GetCountriesByNameContains(argValue ?? string.Empty)?
                                            .Select(a => a.Name.Official)
                                            .OfType<string>()
                                            .Take(100)
                                            .Order()
                                            .ToList() ?? [];
                break;
            case "region":
                result = RestCountriesService.GetAllCountries()?.Select(a => a.Region)
                                            .OfType<string>()
                                            .Distinct()
                                            .Take(100)
                                            .Order()
                                            .ToList() ?? [];
                break;

            default:
                break;
        }

        return await Task.FromResult(new Completion()
        {
            Values = [.. result]
        });

    }

    public IEnumerable<string> GetArguments(IServiceProvider serviceProvider)
        => ["countryA", "countryB", "countryName", "region"];
}
