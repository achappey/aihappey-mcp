using OpenAI;
using Microsoft.Net.Http.Headers;
using Microsoft.KernelMemory;
using MCPhappey.Core.Extensions;
using MCPhappey.WebApi;
using MCPhappey.Servers.JSON;
using MCPhappey.Servers.SQL.Extensions;
using MCPhappey.Decoders.Extensions;
using MCPhappey.Common.Constants;
using MCPhappey.Auth.Extensions;
using MCPhappey.Scrapers.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Simplicate.Extensions;
using MCPhappey.Tools.Deskbird;
using MCPhappey.Tools.Perplexity;
using MCPhappey.Tools.xAI;
using MCPhappey.Servers.JSON.Extensions;
using MCPhappey.Tools.AzureMaps;
using MCPhappey.Tools.StabilityAI.Models;
using MCPhappey.Tools.Azure.DocumentIntelligence;
using MCPhappey.Tools.Imagga;
using MCPhappey.Tools.AsyncAI;
using MCPhappey.Tools.Mem0;
using MCPhappey.Tools.Anthropic.Skills;
using MCPhappey.Tools.ElevenLabs;
using MCPhappey.Tools.Runway;
using MCPhappey.Common.Models;
using MCPhappey.Tools.Groq.Audio;
using MCPhappey.Tools.Replicate;
using MCPhappey.Tools.Parallel;
using MCPhappey.Tools.Together;
using MCPhappey.Tools.Mistral;
using MCPhappey.Tools.EuropeanUnion;
using MCPhappey.Tools.Cohere;
using MCPhappey.Tools.Rijkswaterstaat;
using MCPhappey.Tools.JinaAI;
using MCPhappey.Tools.Runware;
using MCPhappey.Tools.EdenAI;
using MCPhappey.Tools.VoyageAI;
using MCPhappey.Tools.AIML;
using MCPhappey.Tools.SPAMhunter;
using MCPhappey.Tools.Freepik;
using MCPhappey.Tools.ZAI;
using MCPhappey.Tools.GreenPT;
using MCPhappey.Tools.Audixa;
using MCPhappey.Tools.Deepgram;
using MCPhappey.Tools.Gladia;
using MCPhappey.Tools.ContextualAI;
using MCPhappey.Tools.Telnyx;
using MCPhappey.Tools.OpperAI;
using System.Text.Json;
using System.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);
var appConfig = builder.Configuration.Get<Config>();

var basePath = Path.Combine(AppContext.BaseDirectory, "Servers");
var servers = basePath.GetServers(appConfig?.Simplicate?.Organization ?? "").ToList();

if (!string.IsNullOrEmpty(appConfig?.McpDatabase))
{
    var icons = new List<ServerIcon>();

    if (!string.IsNullOrWhiteSpace(appConfig.DarkIcon))
    {
        icons.Add(new ServerIcon { Theme = "dark", Source = appConfig.DarkIcon });
    }

    if (!string.IsNullOrWhiteSpace(appConfig.LightIcon))
    {
        icons.Add(new ServerIcon { Theme = "light", Source = appConfig.LightIcon });
    }

    servers.AddRange(builder.AddSqlMcpServers(appConfig.McpDatabase, icons));

    if (icons.Any())
    {
        builder.Services.AddSingleton(icons);
    }
}

if (appConfig?.McpExtensions != null)
{
    foreach (var server in servers.Where(s => !string.IsNullOrEmpty(s.Server.BaseMcp)))
    {
        if (appConfig.McpExtensions.TryGetValue(server.Server.BaseMcp!, out var ext))
        {
            server.Server.McpExtension = appConfig.McpExtensions[server.Server.BaseMcp!];
        }
    }
}

static string? GetBearer(Config? cfg, string domain) =>
    cfg?.DomainHeaders?
       .FirstOrDefault(h => h.Key == domain)
       .Value?
       .FirstOrDefault(h => h.Key == HeaderNames.Authorization)
       .Value?
       .GetBearerToken();

static void AddApi<T>(IServiceCollection services, Config? cfg, string domain, Func<string, T> factory)
    where T : class
{
    var key = GetBearer(cfg, domain);
    if (!string.IsNullOrEmpty(key))
        services.AddSingleton(factory(key!));
}

AddApi(builder.Services, appConfig, "connect.deskbird.com", k => new DeskbirdSettings { ApiKey = k });
AddApi(builder.Services, appConfig, "api.stability.ai", k => new StabilityAISettings { ApiKey = k });
AddApi(builder.Services, appConfig, "api.x.ai", k => new XAISettings { ApiKey = k });
AddApi(builder.Services, appConfig, "api.together.xyz", k => new TogetherSettings { ApiKey = k });
AddApi(builder.Services, appConfig, "api.groq.com", k => new GroqSettings { ApiKey = k });
AddApi(builder.Services, appConfig, "api.aimlapi.com", k => new AIMLSettings { ApiKey = k });

builder.Services
.AddMistral(appConfig?.DomainHeaders)
.AddPerplexity(appConfig?.DomainHeaders)
.AddParallel(appConfig?.DomainHeaders)
.AddImagga(appConfig?.DomainHeaders)
.AddRunway(appConfig?.DomainHeaders)
.AddReplicate(appConfig?.DomainHeaders)
.AddCohere(appConfig?.DomainHeaders)
.AddJinaAI(appConfig?.DomainHeaders)
.AddAzureMaps(appConfig?.DomainHeaders)
.AddAsyncAI(appConfig?.DomainHeaders)
.AddRunware(appConfig?.DomainHeaders)
.AddEdenAI(appConfig?.DomainHeaders)
.AddVoyageAI(appConfig?.DomainHeaders)
.AddAIML(appConfig?.DomainHeaders)
.AddSPAMhunter(appConfig?.DomainHeaders)
.AddFreepik(appConfig?.DomainHeaders)
.AddZAI(appConfig?.DomainHeaders)
.AddGreenPT(appConfig?.DomainHeaders)
.AddAudixa(appConfig?.DomainHeaders)
.AddDeepgram(appConfig?.DomainHeaders)
.AddGladia(appConfig?.DomainHeaders)
.AddContextualAI(appConfig?.DomainHeaders)
.AddTelnyx(appConfig?.DomainHeaders)
.AddOpperAI(appConfig?.DomainHeaders)
.AddRijkswaterstaat()
.AddEuropeanUnionVies();

if (appConfig?.DomainHeaders is { } headers)
{
    var match = headers.FirstOrDefault(h =>
        h.Key.EndsWith(".cognitiveservices.azure.com", StringComparison.OrdinalIgnoreCase));

    if (match.Value?.TryGetValue("Ocp-Apim-Subscription-Key", out var diApiKey) == true &&
        !string.IsNullOrWhiteSpace(diApiKey))
    {
        builder.Services.AddSingleton(new AzureAISettings
        {
            Endpoint = match.Key,
            ApiKey = diApiKey
        });
    }
}

var elevenLabsKey = appConfig?.DomainHeaders?
    .FirstOrDefault(a => a.Key == "api.elevenlabs.io")
    .Value
    .FirstOrDefault(a => a.Key == "xi-api-key").Value;

if (elevenLabsKey != null)
{
    builder.Services.AddSingleton(new ElevenLabsSettings()
    {
        ApiKey = elevenLabsKey
    });
}

var mem0Key = appConfig?.DomainHeaders?
    .FirstOrDefault(a => a.Key == "api.mem0.ai")
    .Value
    .FirstOrDefault(a => a.Key == "Authorization").Value.Split(" ").LastOrDefault();

if (mem0Key != null)
{
    builder.Services.AddSingleton(new Mem0Settings()
    {
        ApiKey = mem0Key
    });
}

var antApiKey = appConfig?.DomainHeaders?
            .FirstOrDefault(a => a.Key == "api.anthropic.com")
            .Value
            .FirstOrDefault(a => a.Key == "x-api-key").Value;

if (antApiKey != null)
{
    builder.Services.AddSingleton(new AnthropicSettings()
    {
        ApiKey = antApiKey
    });
}

var apiKey = appConfig?.DomainHeaders?
            .FirstOrDefault(a => a.Key == Hosts.OpenAI)
            .Value
            .FirstOrDefault(a => a.Key == HeaderNames.Authorization).Value.GetBearerToken();

var openAiClient = !string.IsNullOrEmpty(apiKey) ?
    new OpenAIClient(apiKey) : null;

builder.Services.AddApplicationInsightsTelemetry();
builder.Logging.AddApplicationInsights();

if (!string.IsNullOrEmpty(appConfig?.KernelMemoryDatabase)
    && openAiClient != null
    && apiKey != null)
{
    builder.Services.AddKernelMemoryWithOptions(memoryBuilder =>
    {
        memoryBuilder
            .WithCustomWebScraper<DownloadService>()
            .WithSimpleQueuesPipeline()
            .WithOpenAI(new OpenAIConfig()
            {
                APIKey = apiKey,
                TextModel = "gpt-5.1",
                TextModelMaxTokenTotal = 65536,
                EmbeddingDimensions = 3072,
                EmbeddingModel = "text-embedding-3-large"
            })
            .WithDecoders(openAiClient)
            .WithSqlServerMemoryDb(new()
            {
                ConnectionString = appConfig?.KernelMemoryDatabase!
            })
            .WithSearchClientConfig(new()
            {
                MaxMatchesCount = int.MaxValue
            });
    }, new()
    {
        AllowMixingVolatileAndPersistentData = true
    });
}

static async Task<string> GetAppTokenAsync(
    string tenantId,
    string clientId,
    string clientSecret)
{
    using var http = new HttpClient();

    var url = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";

    var form = new Dictionary<string, string>
    {
        ["client_id"] = clientId,
        ["client_secret"] = clientSecret,
        ["scope"] = "https://graph.microsoft.com/.default",
        ["grant_type"] = "client_credentials"
    };

    var res = await http.PostAsync(url, new FormUrlEncodedContent(form));
    res.EnsureSuccessStatusCode();

    var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
    return json.RootElement.GetProperty("access_token").GetString()!;
}

// -------------------------------------------
// 2. Read top 999 users from Graph
// -------------------------------------------
static async Task<IReadOnlyList<Microsoft.Graph.Beta.Models.User>> LoadUsersAsync(string token)
{
    using var http = new HttpClient();
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    // Only ID + displayName
    var url = "https://graph.microsoft.com/beta/users?$select=id,displayName,mail&$top=999";

    var res = await http.GetAsync(url);
    res.EnsureSuccessStatusCode();

    var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
    var users = doc.RootElement.GetProperty("value");

    var dict = new List<Microsoft.Graph.Beta.Models.User>();

    foreach (var u in users.EnumerateArray())
    {
        var id = u.GetProperty("id").GetString()!;
        var name = u.TryGetProperty("displayName", out var dn)
            ? dn.GetString() ?? ""
            : "";

        var mail = u.TryGetProperty("mail", out var m)
            ? m.GetString() ?? ""
            : "";

        dict.Add(new Microsoft.Graph.Beta.Models.User()
        {
            Id = id,
            DisplayName = name,
            Mail = mail
        });
    }

    return dict.AsReadOnly();
}

builder.Services.AddSingleton(sp =>
{
    var token = GetAppTokenAsync(appConfig?.OAuth?.TenantId!, appConfig?.OAuth?.ClientId!,
        appConfig?.OAuth?.ClientSecret!).Result;
    return LoadUsersAsync(token).Result;
});


if (appConfig?.OAuth != null)
{
    builder.Services.AddSingleton(appConfig.OAuth);
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigin", policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders("Mcp-Session-Id");
    });
});

builder
.WithCompletion()
.AddWidgetScraper();

if (!string.IsNullOrEmpty(appConfig?.PrivateKey))
{
    builder.AddAuthServices(appConfig.PrivateKey);
}

if (appConfig?.ApplicationInsights != null)
{
    builder.Services.AddSingleton(appConfig.ApplicationInsights);
}

builder.Services.WithHostScrapers(appConfig?.DomainHeaders, appConfig?.DomainQueryStrings);

if (appConfig?.OAuth != null)
{
    builder.Services.WithOboScrapers(servers, appConfig.OAuth);
}

if (openAiClient != null)
{
    builder.Services.AddSingleton(openAiClient);
}

var googleApiKey = appConfig?.DomainQueryStrings?
            .FirstOrDefault(a => a.Key == "generativelanguage.googleapis.com")
            .Value
            .FirstOrDefault(a => a.Key == "key").Value.GetBearerToken();

if (googleApiKey != null)
{
    builder.WithGoogleAI(googleApiKey);
}

builder.Services.WithDefaultScrapers();

if (appConfig?.Simplicate != null)
{
    builder.WithSimplicateScraper(appConfig.Simplicate, appConfig.OAuth);
    servers.ApplySimplicateOrganization(appConfig.Simplicate.Organization);
}

builder.Services.AddMcpCoreServices(servers, appConfig?.TelemetryDatabase);

var app = builder.Build();
app.UseCors("AllowSpecificOrigin");
app.UseRouting();

if (appConfig?.OAuth != null)
{
    app.MapOAuth([.. servers.Where(a => a.Server.HasAuth())], appConfig.OAuth);
}
app.UseWidgets(Path.Combine(AppContext.BaseDirectory, "Widgets"));
app.UseMcpWebApplication(servers);


app.Run();

