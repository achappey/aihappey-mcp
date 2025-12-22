
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Common.Models;

namespace MCPhappey.Servers.SQL.Tools.Models;

[Description("Please confirm the name of the resource you want to delete: {0}")]
public class ConfirmDeleteResource : IHasName
{
    [JsonPropertyName("name")]
    [Required]
    [Description("Enter the exact name of the resource to confirm deletion.")]
    public string Name { get; set; } = default!;
}

[Description("Update the resource.")]
public class UpdateMcpResource
{
    [JsonPropertyName("uri")]
    [Required]
    [Description("The URI of the resource.")]
    public string Uri { get; set; } = default!;

    [JsonPropertyName("name")]
    [Required]
    [Description("The resource name.")]
    public string Name { get; set; } = default!;

    [JsonPropertyName("title")]
    [Description("The resource title.")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    [Description("New description of the resource.")]
    public string? Description { get; set; }

    [JsonPropertyName("mimeType")]
    [Description("Optional mimetype.")]
    public string? MimeType { get; set; }

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


[Description("Please fill in the details to add a new resource to the specified MCP server.")]
public class AddMcpResource
{
    [JsonPropertyName("uri")]
    [Required]
    [Description("The URI of the resource to add.")]
    public string Uri { get; set; } = default!;

    [JsonPropertyName("name")]
    [Required]
    [Description("The name of the resource to add.")]
    public string Name { get; set; } = default!;

    [JsonPropertyName("title")]
    [Description("The resource title.")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    [Description("Optional description of the resource.")]
    public string? Description { get; set; }

    [JsonPropertyName("mimeType")]
    [Description("Optional mimetype.")]
    public string? MimeType { get; set; }

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