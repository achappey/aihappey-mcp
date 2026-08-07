using System.ComponentModel;
using System.Text.Json;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using MCPhappey.Tools.Graph.Planner.Models;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Planner;

public static partial class GraphPlanner
{
    [Description("Delete a Microsoft Planner task.")]
    [McpServerTool(Title = "Delete Microsoft Planner task", Name = "graph_planner_delete_task",
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphPlanner_DeleteTask(
        [Description("Planner task id to delete.")] string taskId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        {
            var current = await client.Planner.Tasks[taskId].GetAsync(cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException($"Planner task '{taskId}' was not found.");
            var etag = GetPlannerEtag(current.AdditionalData, "Planner task");

            return await requestContext.ConfirmAndDeleteAsync<GraphDeletePlannerTask>(
                taskId,
                async _ => await client.Planner.Tasks[taskId].DeleteAsync(
                    config => config.Headers.Add("If-Match", etag), cancellationToken),
                "Planner task deleted.", cancellationToken);
        }));

    [Description("Delete a Microsoft Planner bucket. The bucket must not contain tasks.")]
    [McpServerTool(Title = "Delete Microsoft Planner bucket", Name = "graph_planner_delete_bucket",
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphPlanner_DeleteBucket(
        [Description("Planner bucket id to delete.")] string bucketId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        {
            var current = await client.Planner.Buckets[bucketId].GetAsync(cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException($"Planner bucket '{bucketId}' was not found.");
            var etag = GetPlannerEtag(current.AdditionalData, "Planner bucket");

            return await requestContext.ConfirmAndDeleteAsync<GraphDeletePlannerBucket>(
                bucketId,
                async _ => await client.Planner.Buckets[bucketId].DeleteAsync(
                    config => config.Headers.Add("If-Match", etag), cancellationToken),
                "Planner bucket deleted.", cancellationToken);
        }));

    [Description("Update a Microsoft Planner task")]
    [McpServerTool(
           Title = "Update a Microsoft Planner task",
           OpenWorld = false,
           UseStructuredContent = true,
           OutputSchemaType = typeof(PlannerTask),
           Destructive = true)]
    public static async Task<CallToolResult?> GraphPlanner_UpdateTask(
           [Description("Planner task id")]
        string taskId,
           RequestContext<CallToolRequestParams> requestContext,
           [Description("New task title")]
        string? title = null,
           [Description("New start date and time")]
        DateTimeOffset? startDateTime = null,
           [Description("New due date and time")]
        DateTimeOffset? dueDateTime = null,
           [Description("Completion percentage from 0 through 100")]
        int? percentComplete = null,
           [Description("Priority from 0 through 10")]
        int? priority = null,
           CancellationToken cancellationToken = default) =>
           await ModelContextToolExtensions.WithExceptionCheck(async () =>
           await requestContext.WithOboGraphClient(async client =>
           await requestContext.WithStructuredContent(async () =>
           {
               if (title is null &&
                   startDateTime is null &&
                   dueDateTime is null &&
                   percentComplete is null &&
                   priority is null)
               {
                   throw new ArgumentException(
                       "At least one task property must be provided.");
               }

               if (percentComplete is < 0 or > 100)
               {
                   throw new ArgumentOutOfRangeException(
                       nameof(percentComplete),
                       "Percent complete must be between 0 and 100.");
               }

               if (priority is < 0 or > 10)
               {
                   throw new ArgumentOutOfRangeException(
                       nameof(priority),
                       "Priority must be between 0 and 10.");
               }

               var currentTask = await client.Planner.Tasks[taskId]
                   .GetAsync(cancellationToken: cancellationToken)
                   ?? throw new InvalidOperationException(
                       $"Planner task '{taskId}' was not found.");

               var etag = GetPlannerEtag(
                   currentTask.AdditionalData,
                   "Planner task");

               return await client.Planner.Tasks[taskId].PatchAsync(
                   new PlannerTask
                   {
                       Title = title,
                       StartDateTime = startDateTime,
                       DueDateTime = dueDateTime,
                       PercentComplete = percentComplete,
                       Priority = priority
                   },
                   config =>
                   {
                       config.Headers.Add("If-Match", etag);
                       config.Headers.Add(
                           "Prefer",
                           "return=representation");
                   },
                   cancellationToken);
           })));


    [Description("Complete or reopen a Microsoft Planner task")]
    [McpServerTool(
        Title = "Complete or reopen a Microsoft Planner task",
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(PlannerTask),
        Destructive = true)]
    public static async Task<CallToolResult?> GraphPlanner_SetTaskCompletion(
        [Description("Planner task id")]
        string taskId,
        [Description("True completes the task; false reopens it")]
        bool completed,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var currentTask = await client.Planner.Tasks[taskId]
                .GetAsync(cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Planner task '{taskId}' was not found.");

            var etag = GetPlannerEtag(
                currentTask.AdditionalData,
                "Planner task");

            return await client.Planner.Tasks[taskId].PatchAsync(
                new PlannerTask
                {
                    PercentComplete = completed ? 100 : 0
                },
                config =>
                {
                    config.Headers.Add("If-Match", etag);
                    config.Headers.Add(
                        "Prefer",
                        "return=representation");
                },
                cancellationToken);
        })));


    [Description("Move a Microsoft Planner task to another bucket")]
    [McpServerTool(
        Title = "Move a Microsoft Planner task",
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(PlannerTask),
        Destructive = true)]
    public static async Task<CallToolResult?> GraphPlanner_MoveTask(
        [Description("Planner task id")]
        string taskId,
        [Description("Destination Planner bucket id")]
        string bucketId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            if (string.IsNullOrWhiteSpace(bucketId))
            {
                throw new ArgumentException(
                    "Destination bucket id is required.",
                    nameof(bucketId));
            }

            var currentTask = await client.Planner.Tasks[taskId]
                .GetAsync(cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Planner task '{taskId}' was not found.");

            var etag = GetPlannerEtag(
                currentTask.AdditionalData,
                "Planner task");

            return await client.Planner.Tasks[taskId].PatchAsync(
                new PlannerTask
                {
                    BucketId = bucketId
                },
                config =>
                {
                    config.Headers.Add("If-Match", etag);
                    config.Headers.Add(
                        "Prefer",
                        "return=representation");
                },
                cancellationToken);
        })));


    [Description(
        "Update the description and card preview type of a Microsoft Planner task")]
    [McpServerTool(
        Title = "Update Microsoft Planner task details",
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(PlannerTaskDetails),
        Destructive = true)]
    public static async Task<CallToolResult?> GraphPlanner_UpdateTaskDetails(
        [Description("Planner task id")]
        string taskId,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("New task description")]
        string? description = null,
        [Description("New task card preview type")]
        PlannerPreviewType? previewType = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            if (description is null &&
                previewType is null)
            {
                throw new ArgumentException(
                    "A description or preview type must be provided.");
            }

            var currentDetails = await client.Planner.Tasks[taskId]
                .Details
                .GetAsync(cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Planner task details for '{taskId}' were not found.");

            var etag = GetPlannerEtag(
                currentDetails.AdditionalData,
                "Planner task details");

            return await client.Planner.Tasks[taskId]
                .Details
                .PatchAsync(
                    new PlannerTaskDetails
                    {
                        Description = description,
                        PreviewType = previewType
                    },
                    config =>
                    {
                        config.Headers.Add("If-Match", etag);
                        config.Headers.Add(
                            "Prefer",
                            "return=representation");
                    },
                    cancellationToken);
        })));


    [Description("Rename a Microsoft Planner bucket")]
    [McpServerTool(
        Title = "Rename a Microsoft Planner bucket",
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(PlannerBucket),
        Destructive = true)]
    public static async Task<CallToolResult?> GraphPlanner_RenameBucket(
        [Description("Planner bucket id")]
        string bucketId,
        [Description("New bucket name")]
        string name,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException(
                    "Bucket name is required.",
                    nameof(name));
            }

            var currentBucket = await client.Planner.Buckets[bucketId]
                .GetAsync(cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Planner bucket '{bucketId}' was not found.");

            var etag = GetPlannerEtag(
                currentBucket.AdditionalData,
                "Planner bucket");

            return await client.Planner.Buckets[bucketId].PatchAsync(
                new PlannerBucket
                {
                    Name = name
                },
                config =>
                {
                    config.Headers.Add("If-Match", etag);
                    config.Headers.Add(
                        "Prefer",
                        "return=representation");
                },
                cancellationToken);
        })));


    private static string GetPlannerEtag(
        IDictionary<string, object> additionalData,
        string resourceName)
    {
        if (!additionalData.TryGetValue(
                "@odata.etag",
                out var etagValue))
        {
            throw new InvalidOperationException(
                $"{resourceName} did not contain an ETag.");
        }

        var etag = etagValue switch
        {
            string value => value,

            JsonElement
            {
                ValueKind: JsonValueKind.String
            } element => element.GetString(),

            _ => etagValue?.ToString()
        };

        if (string.IsNullOrWhiteSpace(etag))
        {
            throw new InvalidOperationException(
                $"{resourceName} contained an invalid ETag.");
        }

        return etag;
    }

    [Description("Create a new Microsoft Planner task")]
    [McpServerTool(Title = "Create a new Microsoft Planner task",
    OpenWorld = false,
    UseStructuredContent = true,
    OutputSchemaType = typeof(PlannerTask),
    Destructive = true)]
    public static async Task<CallToolResult?> GraphPlanner_CreateTask(
            [Description("Planner id")]
            string plannerId,
            [Description("Bucket id")]
            string bucketId,
            [Description("New task title")]
            string title,
            RequestContext<CallToolRequestParams> requestContext,
            DateTimeOffset? dueDateTime = null,
            int? percentComplete = null,
            int? priority = null,
            CancellationToken cancellationToken = default) =>
            await ModelContextToolExtensions.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async client =>
            await requestContext.WithStructuredContent(async () =>
    {
        var plan = await client.Planner.Plans[plannerId].GetAsync((config) => { }, cancellationToken);
        var bucket = await client.Planner.Plans[plannerId].Buckets[bucketId].GetAsync((config) => { }, cancellationToken);
        var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
            new GraphNewPlannerTask
            {
                Title = title,
                PercentComplete = percentComplete,
                DueDateTime = dueDateTime,
                Priority = priority
            },
            cancellationToken
        );

        return await client.Planner.Tasks.PostAsync(new PlannerTask
        {
            Title = typed?.Title,
            PlanId = plannerId,
            BucketId = bucketId,
            Priority = typed?.Priority,
            PercentComplete = typed?.PercentComplete,
            DueDateTime = typed?.DueDateTime
        }, cancellationToken: cancellationToken);

    })));


    [Description("Create a new Planner bucket in a plan")]
    [McpServerTool(Title = "Create a new Planner bucket in a plan",
    OpenWorld = false,
    UseStructuredContent = true,
    OutputSchemaType = typeof(PlannerBucket),
    Destructive = true)]
    public static async Task<CallToolResult?> GraphPlanner_CreateBucket(
        [Description("Planner id (plan to add bucket to)")]
        string plannerId,
        [Description("Name of the new bucket")]
        string bucketName,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Order hint for bucket placement (optional, leave empty for default).")]
        string? orderHint = null,
        CancellationToken cancellationToken = default) =>
            await ModelContextToolExtensions.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async client =>
            await requestContext.WithStructuredContent(async () =>
    {
        var planner = await client.Planner.Plans[plannerId]
                               .GetAsync(cancellationToken: cancellationToken);

        var (typed, notAccepted, result) = await requestContext.Server.TryElicit(new GraphNewPlannerBucket()
        {
            Name = bucketName,
            OrderHint = orderHint
        }, cancellationToken);

        return await client.Planner.Buckets.PostAsync(new PlannerBucket
        {
            Name = typed.Name,
            PlanId = plannerId,
            OrderHint = typed.OrderHint
        }, cancellationToken: cancellationToken);
    })));

    [Description("Create a new Planner plan")]
    [McpServerTool(Title = "Create a new Planner plan",
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(PlannerPlan),
        Destructive = true)]
    public static async Task<CallToolResult?> GraphPlanner_CreatePlan(
        [Description("Group id (Microsoft 365 group that will own the plan)")]
        string groupId,
        [Description("Title of the new Planner plan")]
        string planTitle,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
            await ModelContextToolExtensions.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async client =>
            await requestContext.WithStructuredContent(async () =>
    {
        var group = await client.Groups[groupId]
                         .GetAsync(cancellationToken: cancellationToken);

        var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
            new GraphNewPlannerPlan
            {
                Title = planTitle
            },
            cancellationToken
        );

        if (notAccepted != null) throw new Exception(JsonSerializer.Serialize(notAccepted));
        if (typed == null) throw new Exception("Invalid result");

        return await client.Planner.Plans.PostAsync(new PlannerPlan
        {
            Title = typed.Title,
            Owner = groupId
        }, cancellationToken: cancellationToken);
    })));

}
