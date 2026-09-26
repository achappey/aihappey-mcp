using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MCPhappey.Auth.Extensions;
using MCPhappey.Auth.Models;
using MCPhappey.Common;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Dataverse;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.PowerAutomate;

public static partial class PowerAutomateFlows
{
    private const string Schema = "https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#";

    [Description("Please confirm deletion of flow: {0}")]
    public sealed class ConfirmDeleteFlow : IHasName
    {
        [Required]
        public string Name { get; set; } = string.Empty;
    }

    private static async Task<CallToolResult?> Run(
        IServiceProvider services, RequestContext<CallToolRequestParams> context, string host,
        Func<HttpClient, Task<CallToolResult?>> operation)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            if (string.IsNullOrWhiteSpace(host) || host.Contains('/') || host.Contains('\\') || host.Contains('?') || host.Contains('#') || host.Contains('@') || host.Contains(':'))
                throw new ValidationException("dynamicsHost must be a Dataverse hostname, not a URL.");

            var bearer = services.GetService<HeaderProvider>()?.Bearer;
            var server = services.GetServerConfig(context.Server);
            if (string.IsNullOrWhiteSpace(bearer) || server is null) return null;

            using var client = await services.GetRequiredService<IHttpClientFactory>()
                .GetOboHttpClient(bearer, host, server.Server, services.GetRequiredService<OAuthSettings>());
            return await operation(client);
        });

    private static string Url(string host, string path)
        => $"https://{host}{DataversePluginExtensions.API_URL}{path}";

    private static string FlowPath(string id) => $"workflows({Guid.Parse(id):D})";

    private static async Task<JsonObject> Read(HttpClient client, string url, CancellationToken ct)
    {
        using var response = await client.GetAsync(url, ct);
        await EnsureSuccess(response, ct);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: ct)) as JsonObject
            ?? throw new InvalidOperationException("Dataverse returned an invalid JSON object.");
    }

    private static async Task EnsureSuccess(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Dataverse error {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}");
    }

    private static async Task<JsonObject> GetFlow(HttpClient client, string host, string id, CancellationToken ct)
    {
        var flow = await Read(client, Url(host, FlowPath(id) + "?$select=workflowid,name,description,category,type,statecode,statuscode,clientdata,ismanaged,modifiedon"), ct);
        if (flow["category"]?.GetValue<int>() != 5 || flow["type"]?.GetValue<int>() != 1 || flow["modernflowtype"]?.GetValue<int>() is > 0)
            throw new ValidationException("The workflow is not a Power Automate cloud flow definition.");
        return flow;
    }

    private static void RequireEditable(JsonObject flow)
    {
        if (flow["ismanaged"]?.GetValue<bool>() == true)
            throw new ValidationException("Managed flows cannot be edited with these tools.");
        if (flow["statecode"]?.GetValue<int>() != 0)
            throw new ValidationException("Disable the flow before editing its definition or connection references.");
    }

    private static JsonObject ClientData(JsonObject flow)
    {
        var raw = flow["clientdata"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(raw)) throw new ValidationException("The flow has no clientdata.");
        return JsonNode.Parse(raw) as JsonObject ?? throw new ValidationException("Flow clientdata is not a JSON object.");
    }

    private static JsonObject Definition(JsonObject data)
        => data["properties"]?["definition"] as JsonObject
            ?? throw new ValidationException("Flow clientdata has no WDL definition.");

    private static JsonObject Section(JsonObject definition, string name)
        => definition[name] as JsonObject ?? throw new ValidationException($"WDL {name} must be an object.");

    private static async Task<CallToolResult?> Edit(
        HttpClient client, string host, string id, Action<JsonObject, JsonObject> edit, CancellationToken ct)
    {
        var flow = await GetFlow(client, host, id, ct);
        RequireEditable(flow);
        var data = ClientData(flow);
        edit(data, Definition(data));
        return await Patch(client, host, id, flow, new JsonObject { ["clientdata"] = data.ToJsonString() }, ct);
    }

    private static async Task<CallToolResult?> Patch(HttpClient client, string host, string id, JsonObject flow, JsonObject body, CancellationToken ct)
    {
        // The weak Dataverse entity ETag still prevents overwriting changes made by another editor.
        var etag = flow["@odata.etag"]?.GetValue<string>()
            ?? throw new InvalidOperationException("Dataverse did not provide an ETag for the flow.");
        using var request = new HttpRequestMessage(HttpMethod.Patch, Url(host, FlowPath(id)))
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        using var response = await client.SendAsync(request, ct);
        await EnsureSuccess(response, ct);
        return new JsonObject { ["workflowid"] = Guid.Parse(id).ToString("D"), ["updated"] = true }.ToCallToolResponse();
    }

    private static JsonObject NewClientData()
        => new()
        {
            ["properties"] = new JsonObject
            {
                ["connectionReferences"] = new JsonObject(),
                ["definition"] = new JsonObject
                {
                    ["$schema"] = Schema,
                    ["contentVersion"] = "1.0.0.0",
                    ["parameters"] = new JsonObject
                    {
                        ["$connections"] = new JsonObject { ["defaultValue"] = new JsonObject(), ["type"] = "Object" },
                        ["$authentication"] = new JsonObject { ["defaultValue"] = new JsonObject(), ["type"] = "SecureObject" }
                    },
                    ["triggers"] = new JsonObject
                    {
                        ["manual"] = new JsonObject
                        {
                            ["type"] = "Request", ["kind"] = "Button",
                            ["inputs"] = new JsonObject
                            {
                                ["schema"] = new JsonObject
                                {
                                    ["type"] = "object", ["properties"] = new JsonObject(), ["required"] = new JsonArray()
                                }
                            }
                        }
                    },
                    ["actions"] = new JsonObject()
                }
            },
            ["schemaVersion"] = "1.0.0.0"
        };

    private static string NonEmpty(string value, string name)
        => !string.IsNullOrWhiteSpace(value) ? value.Trim() : throw new ValidationException($"{name} is required.");

    private static string WdlName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z][A-Za-z0-9_]*$"))
            throw new ValidationException("WDL names must start with a letter and contain only letters, digits and underscores.");
        return name;
    }

    private static JsonObject ValidateStep(JsonObject step)
    {
        if (step["type"] is not JsonValue type || !type.TryGetValue<string>(out var value) || string.IsNullOrWhiteSpace(value))
            throw new ValidationException("A WDL trigger or action requires a nonempty string type.");
        if (step["inputs"] is not null && step["inputs"] is not JsonObject)
            throw new ValidationException("WDL inputs must be an object.");
        if (step["runAfter"] is not null && step["runAfter"] is not JsonObject)
            throw new ValidationException("WDL runAfter must be an object mapping action names to status arrays.");
        return (JsonObject)step.DeepClone();
    }
}
