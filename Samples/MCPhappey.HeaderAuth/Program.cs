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
using MCPhappey.Servers.JSON.Extensions;
using MCPhappey.Tools.AzureMaps;
using MCPhappey.Tools.Azure;
using MCPhappey.Tools.Azure.DocumentIntelligence;
using MCPhappey.Tools.Imagga;
using MCPhappey.Tools.AsyncAI;
using MCPhappey.Tools.DumplingAI;
using MCPhappey.Tools.Mem0;
using MCPhappey.Tools.Anthropic;
using MCPhappey.Tools.Anthropic.Skills;
using MCPhappey.Tools.Anthropic.Messages;
using MCPhappey.Tools.SandBase;
using MCPhappey.Tools.OpenAI.Responses;
using MCPhappey.Tools.OpenAI.Skills;
using MCPhappey.Tools.ElevenLabs;
using MCPhappey.Tools.Runway;
using MCPhappey.Common.Models;
using MCPhappey.Tools.Groq.Audio;
using MCPhappey.Tools.Replicate;
using MCPhappey.Tools.Parallel;
using MCPhappey.Tools.Mistral;
using MCPhappey.Tools.EuropeanUnion;
using MCPhappey.Tools.Cohere;
using MCPhappey.Tools.Rijkswaterstaat;
using MCPhappey.Tools.JinaAI;
using MCPhappey.Tools.Runware;
using MCPhappey.Tools.Magisterium;
using MCPhappey.Tools.CaseDev;
using MCPhappey.Tools.QuiverAI;
using MCPhappey.Tools.Azuce;
using MCPhappey.Tools.VoyageAI;
using MCPhappey.Tools.AIML;
using MCPhappey.Tools.RelaxAI;
using MCPhappey.Tools.GreenPT;
using MCPhappey.Tools.APIpie;
using MCPhappey.Tools.Audixa;
using MCPhappey.Tools.Deepgram;
using MCPhappey.Tools.Gladia;
using MCPhappey.Tools.ExtendAI;
using MCPhappey.Tools.Telnyx;
using MCPhappey.Tools.OpperAI;
using MCPhappey.Tools.BergetAI;
using MCPhappey.Tools.Tinfoil;
using MCPhappey.Tools.DeepL;
using MCPhappey.Tools.RekaAI;
using MCPhappey.Tools.Recraft;
using MCPhappey.Tools.Scrappey;
using MCPhappey.Tools.Scaleway;
using MCPhappey.Tools.SiliconFlow;
using MCPhappey.Tools.Upstage;
using MCPhappey.Tools.MiniMax;
using MCPhappey.Tools.Speechify;
using MCPhappey.Tools.Speechactors;
using MCPhappey.Tools.Verbatik;
using MCPhappey.Tools.deAPI;
using MCPhappey.Tools.ImageRouter;
using MCPhappey.Tools.CometAPI;
using MCPhappey.Tools.Mixedbread;
using MCPhappey.Tools.StepFun;
using MCPhappey.Tools.Kugu;
using MCPhappey.Tools.Infomaniak;
using MCPhappey.Tools.Daglo;
using MCPhappey.Tools.Parasail;
using MCPhappey.Tools.Monica;
using MCPhappey.Tools.Ideogram;
using MCPhappey.Tools.Picsart;
using MCPhappey.Tools.Morpheus;
using MCPhappey.Tools.Pinecone;
using MCPhappey.Tools.SambaNova;
using MCPhappey.Tools.Fireworks;
using MCPhappey.Tools.Nebius;
using MCPhappey.Tools.Supadata;
using MCPhappey.Tools.LumaAI;
using MCPhappey.Tools.Lumenfall;
using MCPhappey.Tools.OCRSpace;
using MCPhappey.Tools.JsonReceipt;
using MCPhappey.Tools.YourVoic;
using MCPhappey.Tools.FishAudio;
using MCPhappey.Tools.Cartesia;
using MCPhappey.Tools.SmallestAI;
using MCPhappey.Tools.UnrealSpeech;
using MCPhappey.Tools.TinyFish;
using MCPhappey.Tools.Olostep;
using MCPhappey.Tools.LLMLayer;
using MCPhappey.Tools.Smooth;
using MCPhappey.Tools.WebsearchAPI;
using MCPhappey.Tools.NimbleWay;
using MCPhappey.Tools.Qomplement;
using MCPhappey.Tools.Tensorlake;
using MCPhappey.Tools.NoizAI;
using MCPhappey.Tools.Rime;
using MCPhappey.Tools.Gradium;
using MCPhappey.Tools.Kirha;
using MCPhappey.Tools.MemU;
using MCPhappey.Tools.BlinkUtilities;
using MCPhappey.Tools.SyntheticSearch;
using MCPhappey.Tools.Loreto;
using MCPhappey.Tools.AgentMail;
using MCPhappey.Tools.WebCrawlerAPI;
using MCPhappey.Tools.AgentSandbox;
using Azure.Monitor.OpenTelemetry.AspNetCore;

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
AddApi(builder.Services, appConfig, "api.groq.com", k => new GroqSettings { ApiKey = k });
AddApi(builder.Services, appConfig, "api.aimlapi.com", k => new AIMLSettings { ApiKey = k });

AnthropicHeaders.EnsureManagedAgentsHeaders(appConfig?.DomainHeaders);

builder.Services
.AddAzureSkills(appConfig?.SkillsStorage)
.AddOpenAIResponses(appConfig?.DomainHeaders)
.AddMistral(appConfig?.DomainHeaders)
.AddSyntheticSearch(appConfig?.DomainHeaders)
.AddPerplexity(appConfig?.DomainHeaders)
.AddParallel(appConfig?.DomainHeaders)
.AddImagga(appConfig?.DomainHeaders)
.AddSupadata(appConfig?.DomainHeaders)
.AddAzuce(appConfig?.DomainHeaders)
.AddRunway(appConfig?.DomainHeaders)
.AddReplicate(appConfig?.DomainHeaders)
.AddPinecone(appConfig?.DomainHeaders)
.AddCohere(appConfig?.DomainHeaders)
.AddJinaAI(appConfig?.DomainHeaders)
.AddAzureMaps(appConfig?.DomainHeaders)
.AddAsyncAI(appConfig?.DomainHeaders)
.AddRunware(appConfig?.DomainHeaders)
.AddQuiverAI(appConfig?.DomainHeaders)
.AddVoyageAI(appConfig?.DomainHeaders)
.AddAIML(appConfig?.DomainHeaders)
.AddMiniMax(appConfig?.DomainHeaders)
   .AddAgentSandbox(appConfig?.DomainHeaders)
   .AddRelaxAI(appConfig?.DomainHeaders)
   .AddNebius(appConfig?.DomainHeaders)
   .AddLumaAI(appConfig?.DomainHeaders)
   .AddLumenfall(appConfig?.DomainHeaders)
   .AddFireworks(appConfig?.DomainHeaders)
.AddGreenPT(appConfig?.DomainHeaders)
.AddLoreto(appConfig?.DomainHeaders)
.AddAPIpie(appConfig?.DomainHeaders)
.AddAudixa(appConfig?.DomainHeaders)
.AddDeepgram(appConfig?.DomainHeaders)
.AddGladia(appConfig?.DomainHeaders)
.AddExtendAI(appConfig?.DomainHeaders)
.AddTelnyx(appConfig?.DomainHeaders)
.AddOpperAI(appConfig?.DomainHeaders)
.AddTensorlake(appConfig?.DomainHeaders)
.AddKirha(appConfig?.DomainHeaders)
.AddOCRSpace(appConfig?.DomainHeaders)
.AddJsonReceipt(appConfig?.DomainHeaders)
.AddBergetAI(appConfig?.DomainHeaders)
.AddScaleway(appConfig?.DomainHeaders)
.AddSiliconFlow(appConfig?.DomainHeaders)
.AddDeepL(appConfig?.DomainHeaders)
.AddRekaAI(appConfig?.DomainHeaders)
.AddRecraft(appConfig?.DomainHeaders)
.AddUpstage(appConfig?.DomainHeaders)
.AddSpeechify(appConfig?.DomainHeaders)
.AddRime(appConfig?.DomainHeaders)
.AddSpeechactors(appConfig?.DomainHeaders)
.AddVerbatik(appConfig?.DomainHeaders)
.AddUnrealSpeech(appConfig?.DomainHeaders)
.AddDeAPI(appConfig?.DomainHeaders)
.AddImageRouter(appConfig?.DomainHeaders)
.AddParasail(appConfig?.DomainHeaders)
   .AddCometAPI(appConfig?.DomainHeaders)
   .AddMixedbread(appConfig?.DomainHeaders)
.AddStepFun(appConfig?.DomainHeaders)
.AddNoizAI(appConfig?.DomainHeaders)
.AddGradium(appConfig?.DomainHeaders)
.AddKugu(appConfig?.DomainHeaders)
.AddMorpheus(appConfig?.DomainHeaders)
.AddInfomaniak(appConfig?.DomainHeaders)
.AddDaglo(appConfig?.DomainHeaders)
.AddDumplingAI(appConfig?.DomainHeaders)
.AddMonica(appConfig?.DomainHeaders)
.AddIdeogram(appConfig?.DomainHeaders)
.AddPicsart(appConfig?.DomainHeaders)
.AddTinfoil(appConfig?.DomainHeaders)
.AddSambaNova(appConfig?.DomainHeaders)
.AddYourVoic(appConfig?.DomainHeaders)
.AddSmallestAI(appConfig?.DomainHeaders)
.AddFishAudio(appConfig?.DomainHeaders)
.AddCartesia(appConfig?.DomainHeaders)
.AddTinyFish(appConfig?.DomainHeaders)
.AddSmooth(appConfig?.DomainHeaders)
.AddLLMLayer(appConfig?.DomainHeaders)
.AddOlostep(appConfig?.DomainHeaders)
.AddBlinkUtilities(appConfig?.DomainHeaders)
.AddWebsearchAPI(appConfig?.DomainHeaders)
.AddMagisterium(appConfig?.DomainHeaders)
.AddQomplement(appConfig?.DomainHeaders)
.AddMemU(appConfig?.DomainHeaders)
.AddAgentMail(appConfig?.DomainHeaders)
.AddWebCrawlerAPI(appConfig?.DomainHeaders)
.AddCaseDev(appConfig?.DomainHeaders)
.AddNimbleWay(appConfig?.DomainHeaders)
.AddScrappey(appConfig?.DomainHeaders, appConfig?.DomainQueryStrings)
.AddRijkswaterstaat()
.AddEuropeanUnionVies();

var appInsightsConnectionString =
    builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];

if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
{
    builder.Services.AddOpenTelemetry()
        .UseAzureMonitor(options =>
        {
            options.ConnectionString = appInsightsConnectionString;
        });
}


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

builder.Services.WithHostScrapers(appConfig?.DomainHeaders, appConfig?.DomainQueryStrings);

if (appConfig?.OAuth != null)
{
    builder.Services.WithOboScrapers(servers, appConfig.OAuth);
}

builder.Services.WithDefaultScrapers();
builder.Services.AddMcpCoreServices(servers);

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
