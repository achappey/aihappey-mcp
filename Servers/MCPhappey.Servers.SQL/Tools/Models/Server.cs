
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Common.Models;

namespace MCPhappey.Servers.SQL.Tools.Models;

[Description("Please fill in the MCP Server details.")]
public class NewMcpServer
{
    [JsonPropertyName("name")]
    [Required]
    [Description("The MCP server name.")]
    public string Name { get; set; } = default!;

    [JsonPropertyName("description")]
    [Required]
    [Description("The MCP server description. Description will be PUBLICLY available and visible to anyone who queries or browses the registry.")]
    public string Description { get; set; } = null!;

    [JsonPropertyName("title")]
    [Description("The MCP server title.")]
    public string? Title { get; set; }

    [JsonPropertyName("websiteUrl")]
    [Description("The MCP website url.")]
    public Uri? WebsiteUrl { get; set; }

    [JsonPropertyName("instructions")]
    [Description("The MCP server instructions.")]
    public string? Instructions { get; set; }

    [JsonPropertyName("secured")]
    [DefaultValue(true)]
    [Description("If the MCP server is secured and needs authentication")]
    public bool? Secured { get; set; }

    [JsonPropertyName("hidden")]
    [DefaultValue(false)]
    [Description("If the MCP server should be hidden from listings and named statistics")]
    public bool? Hidden { get; set; }

}

[Description("Please fill in the MCP Server plugin details.")]
public class McpServerPlugin
{
    [JsonPropertyName("pluginName")]
    [Required]
    [Description("The name of the plugin.")]
    public string PluginName { get; set; } = default!;
}


[Description("Please fill in the MCP Server tool template details.")]
public class McpServerToolTemplate
{
    [JsonPropertyName("toolName")]
    [Required]
    [Description("The name of the tool.")]
    public string ToolName { get; set; } = default!;

    [JsonPropertyName("outputTemplate")]
    [Required]
    [Description("Uri of the output template.")]
    public string OutputTemplate { get; set; } = default!;

}

[Description("Please fill in the MCP Server details.")]
public class CloneMcpServer
{
    [JsonPropertyName("name")]
    [Required]
    [Description("The MCP server name.")]
    public string Name { get; set; } = default!;
}

[Description("Please fill in the MCP Server owner details.")]
public class McpServerOwner
{
    [JsonPropertyName("userId")]
    [Required]
    [Description("The user id of the MCP server owner.")]
    public string UserId { get; set; } = default!;
}

[Description("Please fill in the MCP Server name to confirm deletion: {0}")]
public class DeleteMcpServer : IHasName
{
    [JsonPropertyName("name")]
    [Required]
    [Description("The MCP server name.")]
    public string Name { get; set; } = default!;
}

[Description("Update the MCP server.")]
public class UpdateMcpServer
{
    [JsonPropertyName("name")]
    [Description("New name of the server (optional).")]
    public string? Name { get; set; }

    [JsonPropertyName("title")]
    [Description("The MCP server title.")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    [Description("The MCP server description. Be aware: the server description will be publicly available and visible to anyone who queries or browses the registry.")]
    public string? Description { get; set; }

    [JsonPropertyName("websiteUrl")]
    [Description("The MCP website url.")]
    public Uri? WebsiteUrl { get; set; }

    [JsonPropertyName("instructions")]
    [Description("The MCP server instructions.")]
    public string? Instructions { get; set; }

    [JsonPropertyName("hidden")]
    [DefaultValue(false)]
    [Description("If the MCP server should be hidden from listings and names statistics")]
    public bool? Hidden { get; set; }
}

[Description("Please fill in the security group details.")]
public class McpSecurityGroup
{
    [JsonPropertyName("groupId")]
    [Required]
    [Description("The object ID of the security group.")]
    public string GroupId { get; set; } = default!;
}

[Description("Update one or more fields. Leave blank to skip updating that field. Use a single space to clear the value.")]
public class UpdateMcpServerSecurity
{
    [JsonPropertyName("secured")]
    [Description("Enable if you would like to secure the MCP server.")]
    public bool? Secured { get; set; }
}