using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MCPhappey.Tools.Graph.Planner.Models;

[Description("Please fill in the Planner bucket details")]
public class GraphNewPlannerBucket
{
    [JsonPropertyName("name")]
    [Required]
    [Description("Name of the new bucket.")]
    public string Name { get; set; } = default!;

    [JsonPropertyName("orderHint")]
    [Description("Order hint for bucket placement (optional, leave empty for default).")]
    public string? OrderHint { get; set; }
}

[Description("Please fill in the Planner plan details")]
public class GraphNewPlannerPlan
{
    [JsonPropertyName("title")]
    [Required]
    [Description("Name of the new Planner plan.")]
    public string Title { get; set; } = default!;
}

[Description("Please fill in the Planner task details")]
public class GraphNewPlannerTask
{
    [JsonPropertyName("title")]
    [Required]
    [Description("The task title.")]
    public string Title { get; set; } = default!;

    [JsonPropertyName("dueDateTime")]
    [Description("Due date.")]
    public DateTimeOffset? DueDateTime { get; set; }

    [JsonPropertyName("priority")]
    [Description("Priority.")]
    [Range(0, 10)]
    public int? Priority { get; set; }

    [JsonPropertyName("percentComplete")]
    [Description("Percent complete")]
    [Range(0, 100)]
    public int? PercentComplete { get; set; }
}


[Description("Copy Plan")]
public class GraphCopyPlanner
{
    [JsonPropertyName("title")]
    [Required]
    [Description("The title of the new Planner.")]
    public string Title { get; set; } = default!;

}