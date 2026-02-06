using System.ComponentModel;
using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MCPhappey.Core.Extensions;

namespace MCPhappey.Tools.Rijkswaterstaat;

public static class WaterDataService
{ // base endpoints are relative to WaterDataClient.BaseAddress
    private const string CatalogEndpoint = "METADATASERVICES/OphalenCatalogus";
    private const string ObservationsEndpoint = "ONLINEWAARNEMINGENSERVICES/OphalenWaarnemingen";
    private const string CheckEndpoint = "ONLINEWAARNEMINGENSERVICES/CheckWaarnemingenAanwezig";
    private const string LatestEndpoint = "ONLINEWAARNEMINGENSERVICES/OphalenLaatsteWaarnemingen";

    // ---------- TOOL 1 ----------
    [Description("Retrieve available water data catalog metadata.")]
    [McpServerTool(Name = "rijkswaterstaat_waterdata_get_catalog", Title = "Get WaterData catalog", ReadOnly = true)]
    public static async Task<CallToolResult?> GetCatalog(
        bool includeCompartimenten,
        bool includeGrootheden,
        bool includeParameters = false,
        bool includeProcesTypes = false,
        bool includeGroeperingen = false,
        bool includeEenheden = false,
        bool includeHoedanigheden = false,
        bool includeTyperingen = false,
        bool includeWaardeBewerkingsMethoden = false,
        bool includeBioTaxon = false,
        bool includeOrganen = false,
        IServiceProvider sp = null!,
        RequestContext<CallToolRequestParams> rc = null!,
        CancellationToken ct = default)
        => await rc.WithExceptionCheck(async () =>
        await rc.WithStructuredContent(async () =>
        {
            var filter = new JsonObject();
            AddBool(filter, "Compartimenten", includeCompartimenten);
            AddBool(filter, "Grootheden", includeGrootheden);
            AddBool(filter, "Parameters", includeParameters);
            AddBool(filter, "ProcesTypes", includeProcesTypes);
            AddBool(filter, "Groeperingen", includeGroeperingen);
            AddBool(filter, "Eenheden", includeEenheden);
            AddBool(filter, "Hoedanigheden", includeHoedanigheden);
            AddBool(filter, "Typeringen", includeTyperingen);
            AddBool(filter, "WaardeBewerkingsMethoden", includeWaardeBewerkingsMethoden);
            AddBool(filter, "BioTaxon", includeBioTaxon);
            AddBool(filter, "Organen", includeOrganen);

            var body = new JsonObject { ["CatalogusFilter"] = filter };

            var client = sp.GetRequiredService<WaterDataClient>();
            return await client.PostAsync(CatalogEndpoint, body, ct);
        }));

    // ---------- TOOL 2 ----------
    [Description("Retrieve water measurements by location, period and AQUO filters.")]
    [McpServerTool(Name = "rijkswaterstaat_waterdata_get_observations", Title = "Get WaterData observations", ReadOnly = true)]
    public static async Task<CallToolResult?> GetObservations(
        string locatieCode,
        DateTimeOffset beginTijd,
        DateTimeOffset eindTijd,
        string? compartimentCode = null,
        string? grootheidCode = null,
        string? parameterCode = null,
        string? procesType = null,
        string? groeperingCode = null,
        List<string>? kwaliteitswaardecodes = null,
        List<string>? bemonsteringshoogtes = null,
        List<string>? opdrachtgevendeInstanties = null,
        IServiceProvider sp = null!,
        RequestContext<CallToolRequestParams> rc = null!,
        CancellationToken ct = default)
        => await rc.WithExceptionCheck(async () =>
        await rc.WithStructuredContent(async () =>
        {
            var aquoMetadata = new JsonObject();
            AddCode(aquoMetadata, "Compartiment", compartimentCode);
            AddCode(aquoMetadata, "Grootheid", grootheidCode);
            AddCode(aquoMetadata, "Parameter", parameterCode);
            AddStr(aquoMetadata, "ProcesType", procesType);
            AddCode(aquoMetadata, "Groepering", groeperingCode);

            var waarnemingMeta = new JsonObject();
            AddArr(waarnemingMeta, "KwaliteitswaardecodeLijst", kwaliteitswaardecodes);
            AddArr(waarnemingMeta, "BemonsteringshoogteLijst", bemonsteringshoogtes);
            AddArr(waarnemingMeta, "OpdrachtgevendeInstantieLijst", opdrachtgevendeInstanties);

            var aquoPlus = new JsonObject { ["AquoMetadata"] = aquoMetadata };
            if (waarnemingMeta.Count > 0) aquoPlus["WaarnemingMetadata"] = waarnemingMeta;

            var body = new JsonObject
            {
                ["Locatie"] = new JsonObject { ["Code"] = locatieCode },
                ["AquoPlusWaarnemingMetadata"] = aquoPlus,
                ["Periode"] = new JsonObject
                {
                    ["Begindatumtijd"] = Iso(beginTijd),
                    ["Einddatumtijd"] = Iso(eindTijd)
                }
            };

            var client = sp.GetRequiredService<WaterDataClient>();
            return await client.PostAsync(ObservationsEndpoint, body, ct);
        }));

    // ---------- TOOL 3 ----------
    [Description("Check if observations exist for locations and period.")]
    [McpServerTool(Name = "rijkswaterstaat_waterdata_check_available", Title = "Check WaterData availability", ReadOnly = true)]
    public static async Task<CallToolResult?> CheckAvailable(
        List<string> locatieCodes,
        DateTimeOffset beginTijd,
        DateTimeOffset eindTijd,
        string? compartimentCode = null,
        string? grootheidCode = null,
        IServiceProvider sp = null!,
        RequestContext<CallToolRequestParams> rc = null!,
        CancellationToken ct = default)
        => await rc.WithExceptionCheck(async () =>
        await rc.WithStructuredContent(async () =>
        {
            var locatieLijst = new JsonArray(
                locatieCodes.Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => (JsonNode)new JsonObject { ["Code"] = s })
                    .ToArray());

            var aquoMeta = new JsonObject();
            AddCode(aquoMeta, "Compartiment", compartimentCode);
            AddCode(aquoMeta, "Grootheid", grootheidCode);

            var aquoLijst = new JsonArray();
            if (aquoMeta.Count > 0)
                aquoLijst.Add(new JsonObject { ["AquoMetadata"] = aquoMeta });

            var body = new JsonObject
            {
                ["LocatieLijst"] = locatieLijst,
                ["Periode"] = new JsonObject
                {
                    ["Begindatumtijd"] = Iso(beginTijd),
                    ["Einddatumtijd"] = Iso(eindTijd)
                }
            };
            if (aquoLijst.Count > 0)
                body["AquoMetadataLijst"] = aquoLijst;

            var client = sp.GetRequiredService<WaterDataClient>();
            return await client.PostAsync(CheckEndpoint, body, ct);
        }));

    // ---------- TOOL 4 ----------
    [Description("Get latest valid measurements per location and AQUO metadata combinations.")]
    [McpServerTool(Name = "rijkswaterstaat_waterdata_get_latest", Title = "Get latest WaterData observations", ReadOnly = true)]
    public static async Task<CallToolResult?> GetLatest(
        List<string> locatieCodes,
        List<LatestAquoSelector> aquoSelectors,
        IServiceProvider sp = null!,
        RequestContext<CallToolRequestParams> rc = null!,
        CancellationToken ct = default)
        => await rc.WithExceptionCheck(async () =>
        await rc.WithStructuredContent(async () =>
        {
            var locaties = new JsonArray(
                locatieCodes.Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => (JsonNode)new JsonObject { ["Code"] = s })
                    .ToArray());

            var aquoPlus = new JsonArray();
            foreach (var sel in aquoSelectors ?? [])
            {
                var meta = new JsonObject();
                AddCode(meta, "Compartiment", sel.CompartimentCode);
                AddCode(meta, "Grootheid", sel.GrootheidCode);
                AddCode(meta, "Parameter", sel.ParameterCode);
                aquoPlus.Add(new JsonObject { ["AquoMetadata"] = meta });
            }

            var body = new JsonObject
            {
                ["LocatieLijst"] = locaties,
                ["AquoPlusWaarnemingMetadataLijst"] = aquoPlus
            };

            var client = sp.GetRequiredService<WaterDataClient>();
            return await client.PostAsync(LatestEndpoint, body, ct);
        }));


    // ---------- Helper methods ----------
    private static void AddBool(JsonObject obj, string name, bool value) { if (value) obj[name] = true; }
    private static void AddStr(JsonObject obj, string name, string? val) { if (!string.IsNullOrWhiteSpace(val)) obj[name] = val; }
    private static void AddCode(JsonObject obj, string prop, string? code)
    { if (!string.IsNullOrWhiteSpace(code)) obj[prop] = new JsonObject { ["Code"] = code }; }
    private static void AddArr(JsonObject obj, string name, List<string>? vals)
    {
        if (vals == null || vals.Count == 0) return;
        var arr = new JsonArray(vals.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => (JsonNode)v).ToArray());
        if (arr.Count > 0) obj[name] = arr;
    }
    private static string Iso(DateTimeOffset dto) => dto.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz");

    // ================================================================
    // DTO’s
    // ================================================================
    public class LatestAquoSelector
    {
        [Description("Compartiment code, e.g. 'OW'")]
        public string? CompartimentCode { get; set; }

        [Description("Grootheid code, e.g. 'T', 'WATHTE'")]
        public string? GrootheidCode { get; set; }

        [Description("Parameter code, e.g. 'Cd'")]
        public string? ParameterCode { get; set; }
    }
}
