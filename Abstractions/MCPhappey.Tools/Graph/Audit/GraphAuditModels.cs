using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.Graph.Beta.Models.Security;

namespace MCPhappey.Tools.Graph.Audit;

[Description("Please fill in the audit log query details.")]
public class GraphNewAuditLogQuery
{
    [JsonPropertyName("displayName")]
    [Description("Optional display name for the saved query.")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("startDateTime")]
    [Description("Start of time range (UTC). Default: now-7d.")]
    public DateTimeOffset? StartDateTime { get; set; }

    [JsonPropertyName("endDateTime")]
    [Description("End of time range (UTC). Default: now.")]
    public DateTimeOffset? EndDateTime { get; set; }

    [JsonPropertyName("filter")]
    [Required]
    [Description("The query filter.")]
    public string Filter { get; set; } = default!;

    [JsonPropertyName("serviceFilter")]
    [Description("Workload/service filter, e.g. 'SharePoint', 'Exchange'.")]
    public string? ServiceFilter { get; set; }

    // Collections
    [JsonPropertyName("recordTypeFilters")]
    [Description("Record types, e.g. ['sharePoint','sharePointFileOperation','oneDrive'].")]
    public IEnumerable<AuditLogRecordType?>? RecordTypeFilters { get; set; }

    [JsonPropertyName("operationFilters")]
    [Description("Operation names, e.g. ['FileRecycled','FileDeleted','FileDownloaded'].")]
    public IEnumerable<string>? OperationFilters { get; set; }

    [JsonPropertyName("userPrincipalNameFilters")]
    [Description("UPNs to match, e.g. ['user@contoso.com'].")]
    public IEnumerable<string>? UserPrincipalNameFilters { get; set; }

    [JsonPropertyName("ipAddressFilters")]
    [Description("IP addresses to match.")]
    public IEnumerable<string>? IpAddressFilters { get; set; }

    [JsonPropertyName("objectIdFilters")]
    [Description("For SharePoint/OneDrive: full path(s) of the file/folder (e.g. '/sites/TeamA/SitePages/Home.aspx').")]
    public IEnumerable<string>? ObjectIdFilters { get; set; }

    [JsonPropertyName("administrativeUnitIdFilters")]
    [Description("Administrative unit IDs (if you scope to AUs).")]
    public IEnumerable<string>? AdministrativeUnitIdFilters { get; set; }
}
