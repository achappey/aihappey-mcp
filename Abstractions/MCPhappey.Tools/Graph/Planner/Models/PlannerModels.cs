using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.Graph.Beta.Models;

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


[Description("Please confirm the Planner task changes")]
public class GraphUpdatePlannerTask
{
    [JsonPropertyName("title")]
    [Description("New title of the task.")]
    public string? Title { get; set; }

    [JsonPropertyName("startDateTime")]
    [Description("New start date and time.")]
    public DateTimeOffset? StartDateTime { get; set; }

    [JsonPropertyName("dueDateTime")]
    [Description("New due date and time.")]
    public DateTimeOffset? DueDateTime { get; set; }

    [JsonPropertyName("percentComplete")]
    [Description("Completion percentage from 0 through 100.")]
    [Range(0, 100)]
    public int? PercentComplete { get; set; }

    [JsonPropertyName("priority")]
    [Description("Task priority from 0 through 10.")]
    [Range(0, 10)]
    public int? Priority { get; set; }
}


[Description("Please confirm the Planner task detail changes")]
public class GraphUpdatePlannerTaskDetails
{
    [JsonPropertyName("description")]
    [Description("New description of the Planner task.")]
    public string? Description { get; set; }

    [JsonPropertyName("previewType")]
    [Description("Content displayed as the task card preview.")]
    public PlannerPreviewType? PreviewType { get; set; }
}


[Description("Please confirm the Planner bucket changes")]
public class GraphUpdatePlannerBucket
{
    [JsonPropertyName("name")]
    [Description("New name of the bucket.")]
    public string? Name { get; set; }
}

[Description("Please confirm the Planner task id to delete: {0}")]
public sealed class GraphDeletePlannerTask : MCPhappey.Common.Models.IHasName
{
    [Required]
    [Description("Planner task id.")]
    public string Name { get; set; } = default!;
}

[Description("Please confirm the Planner bucket id to delete: {0}")]
public sealed class GraphDeletePlannerBucket : MCPhappey.Common.Models.IHasName
{
    [Required]
    [Description("Planner bucket id.")]
    public string Name { get; set; } = default!;
}
