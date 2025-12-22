using System.Net.Mime;
using System.Text.RegularExpressions;
using MCPhappey.Auth.Models;
using MCPhappey.Common;
using MCPhappey.Common.Constants;
using MCPhappey.Common.Models;
using MCPhappey.Scrapers.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Server;

namespace MCPhappey.Scrapers.Microsoft;

public class PlannerScraper(IHttpClientFactory httpClientFactory, ServerConfig serverConfig,
    OAuthSettings oAuthSettings) : IContentScraper
{
    public bool SupportsHost(ServerConfig currentConfig, string url)
        => new Uri(url).Host.Equals("planner.cloud.microsoft", StringComparison.OrdinalIgnoreCase)
            && serverConfig.Server.OBO?.ContainsKey(Hosts.MicrosoftGraph) == true;

    public async Task<IEnumerable<FileItem>?> GetContentAsync(McpServer mcpServer, IServiceProvider serviceProvider,
           string url, CancellationToken cancellationToken = default)
    {
        var tokenService = serviceProvider.GetService<HeaderProvider>();
        var scrapers = serviceProvider.GetService<IEnumerable<IContentScraper>>();

        if (string.IsNullOrEmpty(tokenService?.Bearer))
            return null;

        using var graphClient = await httpClientFactory.GetOboGraphClient(
            tokenService.Bearer, serverConfig.Server, oAuthSettings);

        // ── URL ontleden ───────────────────────────────────────────────────────────
        // Accept patterns (robust t.o.v. view/varianten):
        //  - /webui/plan/{planId}
        //  - /webui/plan/{planId}/view/{anything}
        //  - /webui/plan/{planId}/view/{anything}/task/{taskId}
        // Query param 'tid' (tenant) is optioneel en verder niet nodig voor Graph calls
        var uri = new Uri(url);
        var m = Regex.Match(uri.AbsolutePath,
            @"^/webui/plan/(?<plan>[^/]+)(?:/view/[^/]+(?:/task/(?<task>[^/]+))?)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (!m.Success) return null;

        var planId = m.Groups["plan"].Value;
        var taskId = m.Groups["task"]?.Success == true ? m.Groups["task"].Value : null;

        var fileItems = new List<FileItem>();

        // ── Plan ophalen (altijd handig als context) ───────────────────────────────
        PlannerPlan? plan = null;
        try
        {
            plan = await graphClient.Planner.Plans[planId].GetAsync(cancellationToken: cancellationToken);
        }
        catch
        {
            // planId ongeldig of geen rechten
        }

        // Probeer ook owner group te pakken voor context
        string? ownerGroupName = null;
        if (!string.IsNullOrEmpty(plan?.Owner))
        {
            try
            {
                var grp = await graphClient.Groups[plan.Owner].GetAsync(cancellationToken: cancellationToken);
                ownerGroupName = grp?.DisplayName;
            }
            catch { /* ignore */ }
        }

        if (string.IsNullOrEmpty(taskId))
        {
            // ── Alleen plan-URL: geef plan + buckets + indicatieve tasks terug ─────
            var buckets = new List<PlannerBucket>();
            try
            {
                var bucketsResp = await graphClient.Planner.Plans[planId].Buckets.GetAsync(cancellationToken: cancellationToken);
                if (bucketsResp?.Value != null) buckets.AddRange(bucketsResp.Value);
            }
            catch { /* ignore */ }

            var tasksLite = new List<object>();
            try
            {
                var tasksResp = await graphClient.Planner.Plans[planId].Tasks.GetAsync(q =>
                {
                    // Kleine payload
                    q.QueryParameters.Select = ["id", "title", "bucketId", "percentComplete", "dueDateTime", "createdDateTime"];
                }, cancellationToken);
                if (tasksResp?.Value != null)
                {
                    tasksLite.AddRange(tasksResp.Value.Select(t => new
                    {
                        t.Id,
                        t.Title,
                        t.BucketId,
                        t.PercentComplete,
                        t.DueDateTime,
                        t.CreatedDateTime
                    }));
                }
            }
            catch { /* ignore */ }

            fileItems.Add(new FileItem
            {
                Filename = $"PlannerPlan_{planId}.json",
                Uri = url,
                MimeType = MediaTypeNames.Application.Json,
                Contents = BinaryData.FromObjectAsJson(new
                {
                    Context = new
                    {
                        PlanId = planId,
                        PlanTitle = plan?.Title,
                        OwnerGroupId = plan?.Owner,
                        OwnerGroupName = ownerGroupName
                    },
                    Buckets = buckets.Select(b => new { b.Id, b.Name, b.OrderHint }),
                    Tasks = tasksLite
                })
            });

            return fileItems;
        }

        // ── Task-deep link: haal task + details + bucket op ────────────────────────
        PlannerTask? task = null;
        PlannerTaskDetails? details = null;
        PlannerBucket? bucket = null;

        try
        {
            task = await graphClient.Planner.Tasks[taskId].GetAsync(q =>
            {
                q.QueryParameters.Select = ["id", "title", "bucketId", "planId", "percentComplete",
                                            "startDateTime", "dueDateTime", "createdDateTime",
                                            "assignments", "priority", "appliedCategories"];
            }, cancellationToken);
        }
        catch { /* ignore */ }

        if (task == null)
            return null;

        try
        {
            details = await graphClient.Planner.Tasks[taskId].Details.GetAsync(q =>
            {
                q.QueryParameters.Select = ["description", "references", "checklist"];
            }, cancellationToken);
        }
        catch { /* ignore */ }

        if (!string.IsNullOrEmpty(task.BucketId))
        {
            try
            {
                bucket = await graphClient.Planner.Buckets[task.BucketId].GetAsync(cancellationToken: cancellationToken);
            }
            catch { /* ignore */ }
        }

        // Assignees (resolve display names)
        var assignees = new List<object>();
        if (task.Assignments != null)
        {
            foreach (var kv in task.Assignments.AdditionalData)
            {
                var userId = kv.Key; // in assignments map is key = userId
                try
                {
                    var u = await graphClient.Users[userId].GetAsync(q =>
                    {
                        q.QueryParameters.Select = ["id", "displayName", "mail"];
                    }, cancellationToken);
                    if (u != null)
                    {
                        assignees.Add(new { u.Id, u.DisplayName, u.Mail });
                    }
                }
                catch
                {
                    assignees.Add(new { Id = userId, DisplayName = (string?)null, Mail = (string?)null });
                }
            }
        }

        // Labels (appliedCategories) → bool map; zet om naar lijst met true
        var labels = new List<string>();
        if (task.AppliedCategories != null)
        {
            foreach (var kv in task.AppliedCategories.AdditionalData)
            {
                if (kv.Value is bool b && b)
                    labels.Add(kv.Key);
            }
        }

        // Hoofd JSON-artifact
        fileItems.Add(new FileItem
        {
            Filename = $"PlannerTask_{taskId}.json",
            Uri = url,
            MimeType = MediaTypeNames.Application.Json,
            Contents = BinaryData.FromObjectAsJson(new
            {
                Context = new
                {
                    task.PlanId,
                    PlanTitle = plan?.Title,
                    OwnerGroupId = plan?.Owner,
                    OwnerGroupName = ownerGroupName,
                    BucketId = bucket?.Id,
                    BucketName = bucket?.Name
                },
                Task = new
                {
                    task.Id,
                    task.Title,
                    task.PercentComplete,
                    task.Priority,
                    task.StartDateTime,
                    task.DueDateTime,
                    task.CreatedDateTime,
                    Labels = labels,
                    Assignees = assignees
                },
                Details = new
                {
                    details?.Description,
                    Checklist = details?.Checklist?.AdditionalData?.Select(kv => new
                    {
                        Id = kv.Key,
                        Item = (kv.Value as PlannerChecklistItem)?.Title,
                        (kv.Value as PlannerChecklistItem)?.IsChecked
                    }),
                    // Alleen de ruwe refs als preview; volledige content via sub-scrapers hieronder
                    References = details?.References?.AdditionalData?.Select(kv => new
                    {
                        Id = kv.Key,
                        Ref = (kv.Value as PlannerExternalReference)?.Alias,
                        //Url = (kv.Value as PlannerExternalReference)?. ?? (kv.Value as PlannerExternalReference)?.Url
                    })
                }
            })
        });

        return fileItems;
    }
}
