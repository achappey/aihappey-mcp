using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Rijksoverheid;

public static class RuimtelijkePlannenTools
{
    [Description("Search spatial plans using GeoJSON geometry (maximum 99 km²) and optional plan filters. The default input CRS is ETRS89 longitude,latitude. Results include HAL links; paginate with the same geometry and filters, not a GET of the POST next link.")]
    [McpServerTool(Name = "ruimtelijkeplannen_search_plans", Title = "Search spatial plans", ReadOnly = true)]
    public static Task<CallToolResult> SearchPlans(OmgevingswetGeometry geometry,
        [Description("contains, intersects, intersectAndNotTouches or within; applied as _geo.{relation}.")] string relation = "contains",
        RuimtelijkePlannenFilters? filters = null, RuimtelijkePlannenSpatialOptions? options = null,
        IServiceProvider sp = null!, RequestContext<CallToolRequestParams> rc = null!, CancellationToken ct = default)
        => Invoke(sp, rc, () =>
        {
            options ??= new();
            var query = Query(options);
            filters?.Apply(query);
            return ("/plannen/_zoek", SpatialBody(geometry, relation), query,
                OmgevingswetClient.ValidateCrs(options.ContentCrs), OmgevingswetClient.ValidateCrs(options.AcceptCrs));
        }, ct);

    [Description("Search all plan objects in one plan using a GeoJSON geometry. Input CRS defaults to epsg:4258 (ETRS89 longitude,latitude); use epsg:28992 for RD New metres. Maximum search geometry 99 km².")]
    [McpServerTool(Name = "ruimtelijkeplannen_search_plan_objects", Title = "Search plan objects", ReadOnly = true)]
    public static Task<CallToolResult> SearchPlanObjects(string planId, OmgevingswetGeometry geometry,
        [Description("contains, intersects, intersectAndNotTouches or within.")] string relation = "contains",
        string contentCrs = "epsg:4258", IServiceProvider sp = null!, RequestContext<CallToolRequestParams> rc = null!, CancellationToken ct = default)
        => Invoke(sp, rc, () => (PlanPath(planId, "planobjecten/_zoek"), SpatialBody(geometry, relation), null,
            OmgevingswetClient.ValidateCrs(contentCrs), null), ct);

    [Description("Find subjects within a GeoJSON search geometry (maximum 99 km²), optionally at a historical availability datetime. Input is ETRS89 longitude,latitude by default; choose epsg:28992 for RD New metres.")]
    [McpServerTool(Name = "ruimtelijkeplannen_search_subjects", Title = "Search spatial subjects", ReadOnly = true)]
    public static Task<CallToolResult> SearchSubjects(OmgevingswetGeometry geometry,
        [Description("contains, intersects, intersectAndNotTouches or within.")] string relation = "contains",
        string contentCrs = "epsg:4258", string? beschikbaarOp = null,
        IServiceProvider sp = null!, RequestContext<CallToolRequestParams> rc = null!, CancellationToken ct = default)
        => Invoke(sp, rc, () => ("/onderwerpen/_zoek", SpatialBody(geometry, relation),
            new OmgevingswetQuery().Add("beschikbaarOp", beschikbaarOp), OmgevingswetClient.ValidateCrs(contentCrs), null), ct);

    [Description("Find plan articles applicable within the supplied GeoJSON geometry (maximum 99 km²), including general articles applicable to the whole plan. Best suited to bestemmingsplan, inpassingsplan, uitwerkingsplan and wijzigingsplan; decisions and visions may return paragraphs instead and cannot reliably identify general rules. Default input CRS: ETRS89 longitude,latitude.")]
    [McpServerTool(Name = "ruimtelijkeplannen_search_articles", Title = "Search applicable plan articles", ReadOnly = true)]
    public static Task<CallToolResult> SearchArticles(string planId, OmgevingswetGeometry geometry,
        [Description("contains, intersects, intersectAndNotTouches or within.")] string relation = "contains",
        [Description("Zero-based page number.")] int page = 0,
        [Description("10, 20, 50 or 100.")] int pageSize = 10,
        string[]? expand = null, string contentCrs = "epsg:4258",
        IServiceProvider sp = null!, RequestContext<CallToolRequestParams> rc = null!, CancellationToken ct = default)
        => Invoke(sp, rc, () => (PlanPath(planId, "artikelen/_zoek"), SpatialBody(geometry, relation),
            OmgevingswetQuery.Page(page, pageSize).Add("expand", expand, explode: true),
            OmgevingswetClient.ValidateCrs(contentCrs), null), ct);

    [Description("Retrieve zoning areas in a plan by object IDs (not geometry). Returns a HAL collection. Default page size is 50.")]
    [McpServerTool(Name = "ruimtelijkeplannen_get_zoning_areas_by_ids", Title = "Get zoning areas by IDs", ReadOnly = true)]
    public static Task<CallToolResult> GetZoningAreasByIds(string planId, string[] ids, string[]? bestemmingshoofdgroep = null,
        int page = 0, int pageSize = 50, string[]? expand = null, string acceptCrs = "epsg:4258",
        IServiceProvider sp = null!, RequestContext<CallToolRequestParams> rc = null!, CancellationToken ct = default)
        => Invoke(sp, rc, () => (PlanPath(planId, "bestemmingsvlakken"), IdBody(ids),
            OmgevingswetQuery.Page(page, pageSize).Add("bestemmingshoofdgroep", bestemmingshoofdgroep).Add("expand", expand, explode: true),
            null, OmgevingswetClient.ValidateCrs(acceptCrs)), ct);

    [Description("Retrieve cartography summaries in a plan by object IDs (not geometry). Default page size is 50.")]
    [McpServerTool(Name = "ruimtelijkeplannen_get_cartography_summaries_by_ids", Title = "Get cartography summaries by IDs", ReadOnly = true)]
    public static Task<CallToolResult> GetCartographySummariesByIds(string planId, string[] ids, int page = 0, int pageSize = 50,
        IServiceProvider sp = null!, RequestContext<CallToolRequestParams> rc = null!, CancellationToken ct = default)
        => Invoke(sp, rc, () => (PlanPath(planId, "cartografiesummaries"), IdBody(ids), OmgevingswetQuery.Page(page, pageSize), null, null), ct);

    [Description("Search zoning areas (enkelbestemming/dubbelbestemming) in a plan by GeoJSON geometry, maximum 99 km². Default input CRS is ETRS89 longitude,latitude. Default page size is 50.")]
    [McpServerTool(Name = "ruimtelijkeplannen_search_zoning_areas", Title = "Search zoning areas", ReadOnly = true)]
    public static Task<CallToolResult> SearchZoningAreas(string planId, OmgevingswetGeometry geometry,
        [Description("contains, intersects, intersectAndNotTouches or within.")] string relation = "contains",
        string[]? bestemmingshoofdgroep = null, RuimtelijkePlannenSpatialOptions? options = null,
        IServiceProvider sp = null!, RequestContext<CallToolRequestParams> rc = null!, CancellationToken ct = default)
        => SearchObjects(sp, rc, planId, "bestemmingsvlakken", geometry, relation, options, ct, bestemmingshoofdgroep);

    [Description("Search building envelopes (bouwvlakken) in a plan by GeoJSON geometry, maximum 99 km². Default input CRS is ETRS89 longitude,latitude.")]
    [McpServerTool(Name = "ruimtelijkeplannen_search_building_envelopes", Title = "Search building envelopes", ReadOnly = true)]
    public static Task<CallToolResult> SearchBuildingEnvelopes(string planId, OmgevingswetGeometry geometry,
        [Description("contains, intersects, intersectAndNotTouches or within.")] string relation = "contains", RuimtelijkePlannenSpatialOptions? options = null,
        IServiceProvider sp = null!, RequestContext<CallToolRequestParams> rc = null!, CancellationToken ct = default)
        => SearchObjects(sp, rc, planId, "bouwvlakken", geometry, relation, options, ct);

    [Description("Search function designations (functieaanduidingen) in a plan by GeoJSON geometry, maximum 99 km². Default input CRS is ETRS89 longitude,latitude.")]
    [McpServerTool(Name = "ruimtelijkeplannen_search_function_designations", Title = "Search function designations", ReadOnly = true)]
    public static Task<CallToolResult> SearchFunctionDesignations(string planId, OmgevingswetGeometry geometry,
        [Description("contains, intersects, intersectAndNotTouches or within.")] string relation = "contains", RuimtelijkePlannenSpatialOptions? options = null,
        IServiceProvider sp = null!, RequestContext<CallToolRequestParams> rc = null!, CancellationToken ct = default)
        => SearchObjects(sp, rc, planId, "functieaanduidingen", geometry, relation, options, ct);

    [Description("Search building designations (bouwaanduidingen) in a plan by GeoJSON geometry, maximum 99 km². Default input CRS is ETRS89 longitude,latitude.")]
    [McpServerTool(Name = "ruimtelijkeplannen_search_building_designations", Title = "Search building designations", ReadOnly = true)]
    public static Task<CallToolResult> SearchBuildingDesignations(string planId, OmgevingswetGeometry geometry,
        [Description("contains, intersects, intersectAndNotTouches or within.")] string relation = "contains", RuimtelijkePlannenSpatialOptions? options = null,
        IServiceProvider sp = null!, RequestContext<CallToolRequestParams> rc = null!, CancellationToken ct = default)
        => SearchObjects(sp, rc, planId, "bouwaanduidingen", geometry, relation, options, ct);

    [Description("Search letter designations (lettertekenaanduidingen) in a plan by GeoJSON geometry, maximum 99 km². Default input CRS is ETRS89 longitude,latitude.")]
    [McpServerTool(Name = "ruimtelijkeplannen_search_letter_designations", Title = "Search letter designations", ReadOnly = true)]
    public static Task<CallToolResult> SearchLetterDesignations(string planId, OmgevingswetGeometry geometry,
        [Description("contains, intersects, intersectAndNotTouches or within.")] string relation = "contains", RuimtelijkePlannenSpatialOptions? options = null,
        IServiceProvider sp = null!, RequestContext<CallToolRequestParams> rc = null!, CancellationToken ct = default)
        => SearchObjects(sp, rc, planId, "lettertekenaanduidingen", geometry, relation, options, ct);

    [Description("Search dimensions (maatvoeringen), including height and other numeric restrictions, by GeoJSON geometry within a plan. Maximum 99 km²; default input CRS is ETRS89 longitude,latitude.")]
    [McpServerTool(Name = "ruimtelijkeplannen_search_dimensions", Title = "Search plan dimensions", ReadOnly = true)]
    public static Task<CallToolResult> SearchDimensions(string planId, OmgevingswetGeometry geometry,
        [Description("contains, intersects, intersectAndNotTouches or within.")] string relation = "contains", RuimtelijkePlannenSpatialOptions? options = null,
        IServiceProvider sp = null!, RequestContext<CallToolRequestParams> rc = null!, CancellationToken ct = default)
        => SearchObjects(sp, rc, planId, "maatvoeringen", geometry, relation, options, ct);

    [Description("Search figures (figuren), such as pipeline centre lines and relationship arrows, by GeoJSON geometry within a plan. Maximum 99 km²; default input CRS is ETRS89 longitude,latitude.")]
    [McpServerTool(Name = "ruimtelijkeplannen_search_figures", Title = "Search plan figures", ReadOnly = true)]
    public static Task<CallToolResult> SearchFigures(string planId, OmgevingswetGeometry geometry,
        [Description("contains, intersects, intersectAndNotTouches or within.")] string relation = "contains", RuimtelijkePlannenSpatialOptions? options = null,
        IServiceProvider sp = null!, RequestContext<CallToolRequestParams> rc = null!, CancellationToken ct = default)
        => SearchObjects(sp, rc, planId, "figuren", geometry, relation, options, ct);

    [Description("Search area designations (gebiedsaanduidingen), such as noise or safety zones, by GeoJSON geometry within a plan. Maximum 99 km²; default input CRS is ETRS89 longitude,latitude.")]
    [McpServerTool(Name = "ruimtelijkeplannen_search_area_designations", Title = "Search area designations", ReadOnly = true)]
    public static Task<CallToolResult> SearchAreaDesignations(string planId, OmgevingswetGeometry geometry,
        [Description("contains, intersects, intersectAndNotTouches or within.")] string relation = "contains", RuimtelijkePlannenSpatialOptions? options = null,
        IServiceProvider sp = null!, RequestContext<CallToolRequestParams> rc = null!, CancellationToken ct = default)
        => SearchObjects(sp, rc, planId, "gebiedsaanduidingen", geometry, relation, options, ct);

    [Description("Search structure-vision areas (structuurvisiegebieden) by GeoJSON geometry within a plan. Maximum 99 km²; default input CRS is ETRS89 longitude,latitude.")]
    [McpServerTool(Name = "ruimtelijkeplannen_search_structure_vision_areas", Title = "Search structure-vision areas", ReadOnly = true)]
    public static Task<CallToolResult> SearchStructureVisionAreas(string planId, OmgevingswetGeometry geometry,
        [Description("contains, intersects, intersectAndNotTouches or within.")] string relation = "contains", RuimtelijkePlannenSpatialOptions? options = null,
        IServiceProvider sp = null!, RequestContext<CallToolRequestParams> rc = null!, CancellationToken ct = default)
        => SearchObjects(sp, rc, planId, "structuurvisiegebieden", geometry, relation, options, ct);

    [Description("Search structure-vision complexes (structuurvisiecomplexen) by GeoJSON geometry within a plan. Maximum 99 km²; default input CRS is ETRS89 longitude,latitude.")]
    [McpServerTool(Name = "ruimtelijkeplannen_search_structure_vision_complexes", Title = "Search structure-vision complexes", ReadOnly = true)]
    public static Task<CallToolResult> SearchStructureVisionComplexes(string planId, OmgevingswetGeometry geometry,
        [Description("contains, intersects, intersectAndNotTouches or within.")] string relation = "contains", RuimtelijkePlannenSpatialOptions? options = null,
        IServiceProvider sp = null!, RequestContext<CallToolRequestParams> rc = null!, CancellationToken ct = default)
        => SearchObjects(sp, rc, planId, "structuurvisiecomplexen", geometry, relation, options, ct);

    [Description("Search structure-vision explanatory features (structuurvisieverklaringen) by GeoJSON geometry within a plan. Maximum 99 km²; default input CRS is ETRS89 longitude,latitude.")]
    [McpServerTool(Name = "ruimtelijkeplannen_search_structure_vision_explanations", Title = "Search structure-vision explanations", ReadOnly = true)]
    public static Task<CallToolResult> SearchStructureVisionExplanations(string planId, OmgevingswetGeometry geometry,
        [Description("contains, intersects, intersectAndNotTouches or within.")] string relation = "contains", RuimtelijkePlannenSpatialOptions? options = null,
        IServiceProvider sp = null!, RequestContext<CallToolRequestParams> rc = null!, CancellationToken ct = default)
        => SearchObjects(sp, rc, planId, "structuurvisieverklaringen", geometry, relation, options, ct);

    [Description("Search decision areas (besluitvlakken) by GeoJSON geometry within a plan. Maximum 99 km²; default input CRS is ETRS89 longitude,latitude.")]
    [McpServerTool(Name = "ruimtelijkeplannen_search_decision_areas", Title = "Search decision areas", ReadOnly = true)]
    public static Task<CallToolResult> SearchDecisionAreas(string planId, OmgevingswetGeometry geometry,
        [Description("contains, intersects, intersectAndNotTouches or within.")] string relation = "contains", RuimtelijkePlannenSpatialOptions? options = null,
        IServiceProvider sp = null!, RequestContext<CallToolRequestParams> rc = null!, CancellationToken ct = default)
        => SearchObjects(sp, rc, planId, "besluitvlakken", geometry, relation, options, ct);

    [Description("Search decision subareas (besluitsubvlakken) by GeoJSON geometry within a plan. Maximum 99 km²; default input CRS is ETRS89 longitude,latitude.")]
    [McpServerTool(Name = "ruimtelijkeplannen_search_decision_subareas", Title = "Search decision subareas", ReadOnly = true)]
    public static Task<CallToolResult> SearchDecisionSubareas(string planId, OmgevingswetGeometry geometry,
        [Description("contains, intersects, intersectAndNotTouches or within.")] string relation = "contains", RuimtelijkePlannenSpatialOptions? options = null,
        IServiceProvider sp = null!, RequestContext<CallToolRequestParams> rc = null!, CancellationToken ct = default)
        => SearchObjects(sp, rc, planId, "besluitsubvlakken", geometry, relation, options, ct);

    private static Task<CallToolResult> SearchObjects(IServiceProvider sp, RequestContext<CallToolRequestParams> rc,
        string planId, string collection, OmgevingswetGeometry geometry, string relation,
        RuimtelijkePlannenSpatialOptions? options, CancellationToken ct, string[]? bestemmingshoofdgroep = null)
        => Invoke(sp, rc, () =>
        {
            options ??= new();
            var query = Query(options, collection == "bestemmingsvlakken" ? 50 : 10)
                .Add("bestemmingshoofdgroep", bestemmingshoofdgroep);
            return (PlanPath(planId, collection + "/_zoek"), SpatialBody(geometry, relation), query,
                OmgevingswetClient.ValidateCrs(options.ContentCrs), OmgevingswetClient.ValidateCrs(options.AcceptCrs));
        }, ct);

    private static OmgevingswetQuery Query(RuimtelijkePlannenSpatialOptions options, int defaultSize = 10)
        => OmgevingswetQuery.Page(options.Page, options.PageSize ?? defaultSize).Add("expand", options.Expand, explode: true);

    private static string PlanPath(string planId, string suffix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        return $"/plannen/{Uri.EscapeDataString(planId)}/{suffix}";
    }

    private static JsonObject SpatialBody(OmgevingswetGeometry geometry, string relation)
    {
        geometry.Validate();
        if (relation is not ("contains" or "intersects" or "intersectAndNotTouches" or "within"))
            throw new ArgumentException("Relation must be contains, intersects, intersectAndNotTouches or within.");
        return new() { ["_geo"] = new JsonObject { [relation] = JsonSerializer.SerializeToNode(geometry) } };
    }

    private static JsonObject IdBody(string[] ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("IDs cannot contain empty values.");
        return new() { ["ids"] = JsonSerializer.SerializeToNode(ids) };
    }

    private static Task<CallToolResult> Invoke(IServiceProvider sp, RequestContext<CallToolRequestParams> rc,
        Func<(string Path, JsonObject Body, OmgevingswetQuery? Query, string? ContentCrs, string? AcceptCrs)> build,
        CancellationToken ct)
        => rc.WithStructuredContent(async () =>
        {
            var config = sp.GetServerConfig(rc.Server) ?? throw new InvalidOperationException("Unknown MCP server.");
            if (config.Server.ServerInfo.Name != OmgevingswetClient.PlansServer)
                throw new InvalidOperationException("This tool belongs to the Ruimtelijke Plannen server.");
            var request = build();
            return await sp.GetRequiredService<OmgevingswetClient>().PostAsync(sp, config, request.Path, request.Body,
                request.Query, request.ContentCrs, request.AcceptCrs, ct);
        });
}
