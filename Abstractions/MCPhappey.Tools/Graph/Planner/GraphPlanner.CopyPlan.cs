using System.ComponentModel;
using System.Net.Mime;
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
    [Description("Copy a Planner")]
    [McpServerTool(Title = "Copy a Planner", OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> GraphPlanner_CopyPlan(
        [Description("The id of the original Planner to copy.")]
        string plannerId,
        [Description("Target group id. Where the new Planner should be created.")]
        string groupId,
         [Description("The title of the new Planner.")]
        string title,
     IServiceProvider serviceProvider,
     RequestContext<CallToolRequestParams> requestContext,
     CancellationToken cancellationToken = default) =>
            await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async graphClient =>
    {
        var plan = await graphClient.Planner.Plans[plannerId].GetAsync((config) => { }, cancellationToken);
        var targetGroup = await graphClient.Groups[groupId].GetAsync((config) => { }, cancellationToken);

        var (typed, notAccepted, result) = await requestContext.Server.TryElicit<GraphCopyPlanner>(
            new GraphCopyPlanner
            {
                Title = title
            },
            cancellationToken
        );
        if (notAccepted != null) return notAccepted;
        if (typed == null) return "Invalid result".ToErrorCallToolResponse();

        var httpClient = await serviceProvider.GetGraphHttpClient(requestContext.Server);
        var buckets = await graphClient.Planner.Plans[plannerId].Buckets.GetAsync((config) => { }, cancellationToken);
        var tasks = await graphClient.Planner.Plans[plannerId].Tasks.GetAsync((config) => { }, cancellationToken);

        var newPlan = await graphClient.Planner.Plans
            .PostAsync(new PlannerPlan
            {
                Title = typed.Title,
                Owner = groupId
            }, cancellationToken: cancellationToken);

        var bucketMap = new Dictionary<string, string>();
        var bucketsToCopy = buckets?.Value ?? [];
        bucketsToCopy.Reverse();

        foreach (var bucket in bucketsToCopy ?? [])
        {
            var newBucket = await graphClient.Planner.Buckets
                .PostAsync(new PlannerBucket
                {
                    Name = bucket.Name,
                    PlanId = newPlan?.Id
                }, cancellationToken: cancellationToken);

            var markdown =
                  $"<details><summary>Bucket {bucket.Name} created</summary>\n\n```\n{JsonSerializer.Serialize(newBucket)}\n```\n</details>";
            await requestContext.Server.SendMessageNotificationAsync(markdown, LoggingLevel.Info);

            bucketMap[bucket?.Id!] = newBucket?.Id!;
        }

        foreach (var task in tasks?.Value ?? [])
        {
            var newTask = await graphClient.Planner.Tasks
                .PostAsync(new PlannerTask
                {
                    Title = task.Title,
                    PlanId = newPlan?.Id,
                    BucketId = bucketMap[task?.BucketId!],
                    Assignments = task?.Assignments,
                    StartDateTime = task?.StartDateTime,
                    DueDateTime = task?.DueDateTime,
                    PercentComplete = task?.PercentComplete,
                }, cancellationToken: cancellationToken);

            var markdown =
                  $"<details><summary>Task {task?.Title} created</summary>\n\n```\n{JsonSerializer.Serialize(newTask)}\n```\n</details>";

            await requestContext.Server.SendMessageNotificationAsync(markdown, LoggingLevel.Info);

            // --- GET de ORIGINELE checklist/details ---
            var oldDetailsUrl = $"https://graph.microsoft.com/beta/planner/tasks/{task.Id}/details";
            var oldDetailsResp = await httpClient.GetAsync(oldDetailsUrl, cancellationToken);
            oldDetailsResp.EnsureSuccessStatusCode();
            var oldDetailsContent = await oldDetailsResp.Content.ReadAsStringAsync(cancellationToken);
            var oldDetailsJObj = Newtonsoft.Json.Linq.JObject.Parse(oldDetailsContent);

            // Alleen de properties die Graph accepteert:
            var cleanChecklistJObj = new Newtonsoft.Json.Linq.JObject();

            if (oldDetailsJObj["checklist"] is Newtonsoft.Json.Linq.JObject srcChecklist)
            {
                foreach (var prop in srcChecklist.Properties())
                {
                    var itemObj = prop.Value as Newtonsoft.Json.Linq.JObject;
                    if (itemObj == null) continue;

                    var cleanItem = new Newtonsoft.Json.Linq.JObject
                    {
                        ["@odata.type"] = "#microsoft.graph.plannerChecklistItem",
                        ["title"] = itemObj["title"] ?? "",
                        ["isChecked"] = itemObj["isChecked"] ?? false
                    };

                    var newKey = Guid.NewGuid().ToString();
                    cleanChecklistJObj[newKey] = cleanItem;
                }
            }

            // GET ETag van nieuwe task details
            var newDetailsUrl = $"https://graph.microsoft.com/beta/planner/tasks/{newTask.Id}/details";
            var newDetailsResp = await httpClient.GetAsync(newDetailsUrl, cancellationToken);
            newDetailsResp.EnsureSuccessStatusCode();
            var newEtag = newDetailsResp.Headers.ETag?.Tag;

            // PATCH-body
            var patchJObj = new Newtonsoft.Json.Linq.JObject
            {
                ["description"] = oldDetailsJObj["description"] ?? "",
                ["previewType"] = "checklist"
            };

            if (cleanChecklistJObj.Count > 0)
                patchJObj["checklist"] = cleanChecklistJObj;

            var patchContent = new StringContent(
                patchJObj.ToString(Newtonsoft.Json.Formatting.None),
                System.Text.Encoding.UTF8, MediaTypeNames.Application.Json);

            var patchReq = new HttpRequestMessage(HttpMethod.Patch, newDetailsUrl) { Content = patchContent };
            patchReq.Headers.TryAddWithoutValidation("If-Match", newEtag);

            var patchResp = await httpClient.SendAsync(patchReq, cancellationToken);
            patchResp.EnsureSuccessStatusCode();
        }

        var newPlanner = await graphClient.Planner.Plans[newPlan?.Id].GetAsync((config) => { }, cancellationToken);

        return newPlanner.ToJsonContentBlock($"https://graph.microsoft.com/beta/planner/plans/{newPlanner?.Id}").ToCallToolResult();
    }));

}