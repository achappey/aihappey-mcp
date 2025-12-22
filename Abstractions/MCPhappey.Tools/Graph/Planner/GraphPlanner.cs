using System.ComponentModel;
using System.Text.Json;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using MCPhappey.Tools.Graph.Planner.Models;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Planner;

public static partial class GraphPlanner
{
    [Description("Create a new Microsoft Planner task")]
    [McpServerTool(Title = "Create a new Microsoft Planner task", OpenWorld = false, Destructive = true)]
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
            await requestContext.WithExceptionCheck(async () =>
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

        if (notAccepted != null) throw new Exception(JsonSerializer.Serialize(notAccepted));

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
    [McpServerTool(Title = "Create a new Planner bucket in a plan", OpenWorld = false, Destructive = true)]
    public static async Task<CallToolResult?> GraphPlanner_CreateBucket(
        [Description("Planner id (plan to add bucket to)")]
        string plannerId,
        [Description("Name of the new bucket")]
        string bucketName,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Order hint for bucket placement (optional, leave empty for default).")]
        string? orderHint = null,
        CancellationToken cancellationToken = default) =>
            await requestContext.WithExceptionCheck(async () =>
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
    [McpServerTool(Title = "Create a new Planner plan", OpenWorld = false, Destructive = true)]
    public static async Task<CallToolResult?> GraphPlanner_CreatePlan(
        [Description("Group id (Microsoft 365 group that will own the plan)")]
        string groupId,
        [Description("Title of the new Planner plan")]
        string planTitle,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
            await requestContext.WithExceptionCheck(async () =>
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