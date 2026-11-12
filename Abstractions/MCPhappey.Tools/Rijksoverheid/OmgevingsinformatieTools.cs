using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Rijksoverheid;

public static class OmgevingsinformatieTools
{
    [Description("Search Omgevingswet and IMRO documents by text, government level, identifiers and/or GeoJSON geometry. Geometry must use RD New (EPSG:28992) metres, e.g. Point [155000,463000]. Returns HAL documents and pagination. Historical/derived drafts are in related versions; beschikbaarOp takes precedence over synchroniseerMetTileset.")]
    [McpServerTool(Name = "omgevingsinformatie_search_documents", Title = "Search environmental documents", ReadOnly = true)]
    public static async Task<CallToolResult?> SearchDocuments(
        [Description("Search title, identification, URI identification or expressionId.")] string? find = null,
        [Description("GEMEENTE, PROVINCIE, WATERSCHAP or RIJK.")] string? bestuurslaag = null,
        [Description("REGELGEVING or OVERIG.")] string? regelgevingOfOverig = null,
        [Description("Optional GeoJSON geometry in RD New metres (EPSG:28992), not longitude/latitude.")] OmgevingswetGeometry? geometrie = null,
        [Description("Document identifications or uriIdentificaties; may be combined with geometry.")] string[]? identificaties = null,
        [Description("Validity date, YYYY-MM-DD. Omit for today.")] string? geldigOp = null,
        [Description("Availability datetime including timezone. Omit for now.")] string? beschikbaarOp = null,
        [Description("Actueel or Volledig. Explicit beschikbaarOp takes precedence.")] string? synchroniseerMetTileset = null,
        [Description("Include known future-valid documents. Deprecated by the upstream API, retained for compatibility.")] bool inclusiefToekomstigGeldig = false,
        [Description("Zero-based page number.")] int page = 0,
        [Description("Results per page, 1–200.")] int size = 20,
        [Description("ASC or DESC.")] string sort = "ASC",
        IServiceProvider sp = null!, RequestContext<CallToolRequestParams> rc = null!, CancellationToken ct = default)
        => await rc.WithStructuredContent(async () =>
        {
            var config = sp.GetServerConfig(rc.Server) ?? throw new InvalidOperationException("Unknown MCP server.");
            if (config.Server.ServerInfo.Name != OmgevingswetClient.InformationServer)
                throw new InvalidOperationException("This tool belongs to the Omgevingsinformatie server.");
            if (bestuurslaag is not null and not ("GEMEENTE" or "PROVINCIE" or "WATERSCHAP" or "RIJK"))
                throw new ArgumentException("Invalid bestuurslaag.");
            if (regelgevingOfOverig is not null and not ("REGELGEVING" or "OVERIG"))
                throw new ArgumentException("Invalid regelgevingOfOverig.");
            if (synchroniseerMetTileset is not null and not ("Actueel" or "Volledig"))
                throw new ArgumentException("Invalid synchroniseerMetTileset.");
            if (sort is not ("ASC" or "DESC")) throw new ArgumentException("Sort must be ASC or DESC.");
            geometrie?.Validate();
            var body = new JsonObject();
            if (find is not null) body["_find"] = find;
            if (bestuurslaag is not null) body["bestuurslaag"] = bestuurslaag;
            if (regelgevingOfOverig is not null) body["regelgevingOfOverig"] = regelgevingOfOverig;
            if (geometrie is not null) body["geometrie"] = JsonSerializer.SerializeToNode(geometrie);
            if (identificaties is not null) body["identificaties"] = JsonSerializer.SerializeToNode(identificaties);
            var query = OmgevingswetQuery.Page(page, size, information: true)
                .Add("geldigOp", geldigOp).Add("beschikbaarOp", beschikbaarOp)
                .Add("synchroniseerMetTileset", synchroniseerMetTileset)
                .Add("inclusiefToekomstigGeldig", inclusiefToekomstigGeldig).Add("_sort", sort);
            return await sp.GetRequiredService<OmgevingswetClient>().PostAsync(sp, config, "/documenten/_zoek", body,
                query, contentCrs: geometrie is null ? null : OmgevingswetClient.RdNew, cancellationToken: ct);
        });
}
