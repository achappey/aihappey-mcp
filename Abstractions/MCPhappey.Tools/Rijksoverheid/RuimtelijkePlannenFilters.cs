using System.ComponentModel;

namespace MCPhappey.Tools.Rijksoverheid;

public sealed class RuimtelijkePlannenFilters
{
    [Description("Plan statuses, e.g. ontwerp, vastgesteld, onherroepelijk. Sent as a comma-separated filter.")]
    public string[]? Status { get; set; }
    [Description("Code of the government responsible for policy.")]
    public string? BeleidsmatigVerantwoordelijkeOverheidCode { get; set; }
    [Description("Name of the government responsible for policy.")]
    public string? BeleidsmatigVerantwoordelijkeOverheidNaam { get; set; }
    [Description("Government types: gemeentelijke overheid, deelgemeente/stadsdeel, provinciale overheid, nationale overheid.")]
    public string[]? BeleidsmatigVerantwoordelijkeOverheidType { get; set; }
    [Description("Code of the publishing authority.")]
    public string? PublicerendBevoegdGezagCode { get; set; }
    [Description("Name of the publishing authority.")]
    public string? PublicerendBevoegdGezagNaam { get; set; }
    [Description("Publishing authority types: gemeentelijke overheid, deelgemeente/stadsdeel, provinciale overheid, nationale overheid.")]
    public string[]? PublicerendBevoegdGezagType { get; set; }
    [Description("Dossier identification code.")]
    public string? DossierId { get; set; }
    [Description("Plan types, e.g. bestemmingsplan, wijzigingsplan, structuurvisie.")]
    public string[]? PlanType { get; set; }
    [Description("Part of the plan name or plan identification.")]
    public string? PlanFilter { get; set; }
    [Description("YYYY-MM-DD; filter plan types using the associated end-validity date.")]
    public string? Overgangsrecht { get; set; }
    [Description("in ontwikkeling and/or geldend.")]
    public string[]? RegelStatus { get; set; }
    [Description("burgerbindend, instructieregels and/or beleid.")]
    public string[]? RegelBinding { get; set; }
    [Description("Plan status date after this date, YYYY-MM-DD.")]
    public string? PlanstatusdatumNa { get; set; }
    [Description("Plan status date before this date, YYYY-MM-DD.")]
    public string? PlanstatusdatumVoor { get; set; }
    [Description("Only true or omitted. Filter Wro plans whose rules have been incorporated into an omgevingsplan; requires coordination with the API for current omgevingsplannen.")]
    public bool? PonsOmgevingsplan { get; set; }
    [Description("Historical datetime including timezone. Only dossier.status, regelStatus, verwijderdOp and isHistorisch are historical; other fields retain their latest values.")]
    public string? BeschikbaarOp { get; set; }
    [Description("Filter temporary alternative measure (TAM/IMRO) environmental documents.")]
    public bool? IsTamPlan { get; set; }

    public OmgevingswetQuery Apply(OmgevingswetQuery query)
    {
        if (PonsOmgevingsplan == false) throw new ArgumentException("ponsOmgevingsplan supports only true or omission.");
        return query.Add("status", Status)
            .Add("beleidsmatigVerantwoordelijkeOverheid.code", BeleidsmatigVerantwoordelijkeOverheidCode)
            .Add("beleidsmatigVerantwoordelijkeOverheid.naam", BeleidsmatigVerantwoordelijkeOverheidNaam)
            .Add("beleidsmatigVerantwoordelijkeOverheid.type", BeleidsmatigVerantwoordelijkeOverheidType)
            .Add("publicerendBevoegdGezag.code", PublicerendBevoegdGezagCode)
            .Add("publicerendBevoegdGezag.naam", PublicerendBevoegdGezagNaam)
            .Add("publicerendBevoegdGezag.type", PublicerendBevoegdGezagType)
            .Add("dossier.id", DossierId).Add("planType", PlanType).Add("planFilter", PlanFilter)
            .Add("overgangsrecht", Overgangsrecht).Add("regelStatus", RegelStatus).Add("regelBinding", RegelBinding)
            .Add("planstatusdatumNa", PlanstatusdatumNa).Add("planstatusdatumVoor", PlanstatusdatumVoor)
            .Add("ponsOmgevingsplan", PonsOmgevingsplan).Add("beschikbaarOp", BeschikbaarOp).Add("isTamPlan", IsTamPlan);
    }
}

public sealed class RuimtelijkePlannenSpatialOptions
{
    [Description("Zero-based page number. The API limits pagination to 12,000 results; retain the same POST body on subsequent pages.")]
    public int Page { get; set; }
    [Description("10, 20, 50 or 100. Omit for the endpoint default: 50 for bestemmingsvlakken, 10 for other spatial searches.")]
    public int? PageSize { get; set; }
    [Description("Relations to embed, e.g. geometrie, teksten, children, children.children. Encoded as repeated expand parameters. Large expansions can produce very large responses.")]
    public string[]? Expand { get; set; }
    [Description("Input geometry CRS: epsg:4258 (ETRS89 longitude,latitude) or epsg:28992 (RD New x,y metres). No conversion is performed.")]
    public string ContentCrs { get; set; } = "epsg:4258";
    [Description("Response geometry CRS: epsg:4258 or epsg:28992.")]
    public string AcceptCrs { get; set; } = "epsg:4258";
}
