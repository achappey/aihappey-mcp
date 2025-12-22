
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Common.Models;

namespace MCPhappey.Servers.SQL.Tools.Models;

[Description("Please confirm the name of the prompt you want to delete: {0}")]
public class ConfirmDeletePrompt : IHasName
{
    [JsonPropertyName("name")]
    [Required]
    [Description("Enter the exact name of the prompt to confirm deletion.")]
    public string Name { get; set; } = default!;
}

[Description("Update the prompt.")]
public class UpdateMcpPrompt
{
    [JsonPropertyName("name")]
    [Required]
    [Description("The prompt name.")]
    public string Name { get; set; } = default!;

    [JsonPropertyName("prompt")]
    [Required]
    [Description("The prompt.")]
    public string Prompt { get; set; } = default!;

    [JsonPropertyName("title")]
    [Description("The prompt title.")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    [Description("The prompt description.")]
    public string? Description { get; set; }

}

[Description("Update a prompt argument")]
public class UpdateMcpPromptArgument
{

    [JsonPropertyName("description")]
    [Description("New description of the prompt argument (optional).")]
    public string? Description { get; set; }

    [JsonPropertyName("required")]
    [Description("If the argument is required (optional).")]
    public bool? Required { get; set; }
}

[Description("Please fill in the details to add a new prompt to the specified MCP server.")]
public class AddMcpPrompt
{
    [JsonPropertyName("name")]
    [Required]
    [Description("The name of the prompt to add.")]
    public string Name { get; set; } = default!;

    [JsonPropertyName("prompt")]
    [Required]
    [Description("The prompt to add. You can use {argument} style placeholders for prompt arguments.")]
    public string Prompt { get; set; } = default!;

    [JsonPropertyName("title")]
    [Description("The prompt title.")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    [Description("Optional description of the resource.")]
    public string? Description { get; set; }
}