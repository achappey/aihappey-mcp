using System.ComponentModel;
using Bogus;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.OpenAI.Apps;

public static class OpenAIApps
{
    [Description("Get a random company list with outputTemplate.")]
    [McpServerTool(
         Title = "Get random company list",
         Name = "openai_apps_get_company_list",
         ReadOnly = true,
         OpenWorld = false)]
    public static async Task<CallToolResult?> OpenAIApps_GetCompanyList(
         RequestContext<CallToolRequestParams> requestContext,
         [Description("How many people")] int count = 10,
         [Description("Locale code")] string? locale = "en",
         [Description("Optional deterministic seed")] int? seed = null)
         => await requestContext.WithStructuredContent(async () =>
      {
          var f = CreateFaker(locale, seed);
          var data = Enumerable.Range(0, Math.Max(0, count))
              .Select(i =>
              {
                  // Minor per-item variation by nudging seed via IndexGlobal if provided
                  return new
                  {
                      Name = f.Company.CompanyName(),
                      Suffix = f.Company.CompanySuffix(),
                      Logo = f.Image.PicsumUrl(128, 128),
                      Address = f.Address.FullAddress(),
                      Url = f.Internet.Url(),
                      CatchPhrase = f.Company.CatchPhrase(),
                      Bs = f.Company.Bs()
                  };
              });

          return await Task.FromResult(new
          {
              companies = data
          });
      });

    private static Faker CreateFaker(string? locale, int? seed)
    {
        var f = new Faker(locale ?? "en");
        if (seed.HasValue)
        {
            f.Random = new Randomizer(seed.Value);
            //    f.DateTimeReference = new DateTime(2000, 1, 1);
        }
        return f;
    }
}

