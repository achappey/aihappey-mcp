
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Common.Models;

namespace MCPhappey.Servers.SQL.Tools.Models;

[Description("Please confirm the name of the resource template you want to delete: {0}")]
public class ConfirmDeleteResourceTemplate : IHasName
{
    [JsonPropertyName("name")]
    [Required]
    [Description("Enter the exact name of the resource template to confirm deletion.")]
    public string Name { get; set; } = default!;
}

[Description("Update the resource template.")]
public class UpdateMcpResourceTemplate
{
    [JsonPropertyName("uriTemplate")]
    [Required]
    [Description("The uri template of the resource template.")]
    public string UriTemplate { get; set; } = default!;

    [JsonPropertyName("name")]
    [Required]
    [Description("New name of the resource template.")]
    public string Name { get; set; } = default!;

    [JsonPropertyName("title")]
    [Description("The resource template title.")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    [Description("New description of the resource template (optional).")]
    public string? Description { get; set; }

    [JsonPropertyName("priority")]
    [Range(0, 1)]
    [Description("Optional priority of the resource. Between 0 and 1, where 1 is most important and 0 is least important.")]
    public double? Priority { get; set; }

    [JsonPropertyName("assistantAudience")]
    [DefaultValue(true)]
    [Description("Optional assistant audience target.")]
    public bool? AssistantAudience { get; set; } = true;

    [JsonPropertyName("userAudience")]
    [Description("Optional user audience target.")]
    public bool? UserAudience { get; set; }
}

[Description("Please fill in the details to add a new resource template to the specified MCP server.")]
public class AddMcpResourceTemplate
{
    [JsonPropertyName("uri")]
    [Required]
    [Description("The URI of the resource template to add.")]
    public string UriTemplate { get; set; } = default!;

    [JsonPropertyName("name")]
    [Required]
    [Description("The name of the resource template to add.")]
    public string Name { get; set; } = default!;

    [JsonPropertyName("title")]
    [Description("The resource template title.")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    [Description("Optional description of the resource template.")]
    public string? Description { get; set; }

    [JsonPropertyName("priority")]
    [Range(0, 1)]
    [Description("Optional priority of the resource. Between 0 and 1, where 1 is most important and 0 is least important.")]
    public double? Priority { get; set; }

    [JsonPropertyName("assistantAudience")]
    [DefaultValue(true)]
    [Description("Optional assistant audience target.")]
    public bool? AssistantAudience { get; set; } = true;

    [JsonPropertyName("userAudience")]
    [Description("Optional user audience target.")]
    public bool? UserAudience { get; set; }
}