using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Common.Models;

namespace MCPhappey.Tools.Dataverse;

public class EntityMetadata
{
    public string LogicalName { get; set; } = default!;
    public string EntitySetName { get; set; } = default!;
    public string PrimaryNameAttribute { get; set; } = default!;
    public string PrimaryIdAttribute { get; set; } = default!;
    public AttributeMetadata[] Attributes { get; set; } = default!;
}

public class AttributeMetadata
{
    public string LogicalName { get; set; } = default!;
    public string SchemaName { get; set; } = default!;

    public string AttributeType { get; set; } = default!;
    public RequiredLevel RequiredLevel { get; set; } = default!;
    public bool IsValidForCreate { get; set; }
    public bool IsPrimaryId { get; set; }
    public bool IsLogical { get; set; }

    public string[]? Targets { get; set; }
    //  public TargetRef[]? Targets { get; set; }          // look-ups
    public OptionSet? OptionSet { get; set; }          // local picklist
    public OptionSet? GlobalOptionSet { get; set; }    // global picklist
}

public class RequiredLevel
{
    public string Value { get; set; } = default!;
}

public class OptionSet
{
    public Option[] Options { get; set; } = default!;
}

public class Option
{
    public int Value { get; set; }

    public LocalizedLabel Label { get; set; } = default!;

}

public class LocalizedLabel { public UserLabel UserLocalizedLabel { get; set; } = default!; }

public class UserLabel { public string Label { get; set; } = default!; }

public record TargetRef(string LogicalName);

[Description("Please fill in the entity name to confirm deletion: {0}")]
public class DeleteDataverseEntity : IHasName
{
    [JsonPropertyName("name")]
    [Required]
    [Description("The name value of the entity.")]
    public string Name { get; set; } = default!;
}