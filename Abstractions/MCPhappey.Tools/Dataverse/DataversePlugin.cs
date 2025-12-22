using System.ComponentModel;
using MCPhappey.Common;
using MCPhappey.Common.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MCPhappey.Auth.Extensions;
using MCPhappey.Auth.Models;
using MCPhappey.Core.Extensions;
using System.Text.Json;
using System.Net.Http.Json;

namespace MCPhappey.Tools.Dataverse;

public static class DataversePlugin
{
    [Description("Update an existing entity in a Dataverse table")]
    [McpServerTool(Destructive = true,
          Title = "Update entity in Dataverse table",
          OpenWorld = false)]
    public static async Task<CallToolResult?> Dataverse_UpdateEntity(
          IServiceProvider serviceProvider,
          RequestContext<CallToolRequestParams> requestContext,
          [Description("Name of the dynamics host (e.g. companyName.crm4.dynamics.com)")] string dynamicsHost,
          [Description("Name of the table containing the entity")] string tableLogicalName,
          [Description("GUID of the entity to update")] string entityId,
          CancellationToken cancellationToken = default)
            => await requestContext.WithExceptionCheck(async () =>
        {
            var tokenService = serviceProvider.GetService<HeaderProvider>();
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var oAuthSettings = serviceProvider.GetRequiredService<OAuthSettings>();
            var serverConfig = serviceProvider.GetServerConfig(requestContext.Server);

            if (string.IsNullOrEmpty(tokenService?.Bearer) || serverConfig == null) return null;

            using var httpClient = await httpClientFactory.GetOboHttpClient(tokenService.Bearer, dynamicsHost,
                serverConfig.Server, oAuthSettings);


            // --- Metadata -----------------------------------------------------------------------------
            var metadata = await httpClient.GetEntityMetadataAsync(dynamicsHost, tableLogicalName, cancellationToken);
            if (metadata == null || metadata.EntitySetName == null || metadata.Attributes == null)
            {
                return "Unable to load table metadata".ToErrorCallToolResponse();
            }

            // --- Retrieve current record ---------------------------------------------------------------
            var retrieveUri = $"https://{dynamicsHost}{DataversePluginExtensions.API_URL}{metadata.EntitySetName}({entityId})";
            using var retrieveRes = await httpClient.GetAsync(retrieveUri, cancellationToken);
            if (!retrieveRes.IsSuccessStatusCode)
            {
                var body = await retrieveRes.Content.ReadAsStringAsync(cancellationToken);
                return $"Dataverse retrieve error {retrieveRes.StatusCode}: {body}".ToErrorCallToolResponse();
            }

            var currentRecord = await retrieveRes.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

            // --- Build Elicit form with current values pre‑filled --------------------------------------
            var attributes = metadata.Attributes
                .GetSupportedAttributes();

            var properties = await attributes
                .MapMetadataToElicit(dynamicsHost, httpClient, tableLogicalName, cancellationToken);

            var availableItems = attributes.Where(z => currentRecord.TryGetProperty(z.LogicalName, out var jsonElement)).Select(z => z.LogicalName);
            var promptResult = await requestContext.Server.ElicitAsync(new ElicitRequestParams
            {
                Message = $"Update values for the {tableLogicalName} item (ID {entityId}). Fields left blank will remain unchanged.",
                /*        Message = $"Update values for the {tableLogicalName} item (ID {entityId}). Fields left blank will remain unchanged."
                            .ToElicitDefaultData(attributes
                                .Where(a => availableItems.Contains(a.LogicalName))
                                .ToDictionary(a => a.LogicalName ?? a.SchemaName,
                                a => currentRecord.GetProperty(a.LogicalName).ToString())),*/
                RequestedSchema = new ElicitRequestParams.RequestSchema
                {
                    Properties = properties,
                    Required = [.. attributes
                            .Where(a => a.RequiredLevel.Value == "ApplicationRequired")
                            .Select(a => a.LogicalName ?? a.SchemaName)]
                }
            }, cancellationToken);

            var answers = promptResult.Content ?? new Dictionary<string, JsonElement>();

            // --- Build payload only with changed fields ------------------------------------------------
            var payload = await answers.MapElicitToPayload(metadata.Attributes, httpClient, dynamicsHost, tableLogicalName, cancellationToken);
            if (payload.Count == 0)
            {
                return "No changes detected; entity not updated.".ToErrorCallToolResponse();
            }

            // --- Send PATCH request -------------------------------------------------------------------
            var updateUri = $"https://{dynamicsHost}/api/data/v9.2/{metadata.EntitySetName}({entityId})";
            using var updateRes = await httpClient.PatchAsJsonAsync(updateUri, payload, cancellationToken);
            if (!updateRes.IsSuccessStatusCode)
            {
                var body = await updateRes.Content.ReadAsStringAsync(cancellationToken);
                return $"Dataverse update error {updateRes.StatusCode}: {body}".ToErrorCallToolResponse();
            }

            return payload.ToJsonContentBlock(updateUri).ToCallToolResult();
        });


    [Description("Delete an entity from a Dataverse table")]
    [McpServerTool(Title = "Delete entity from Dataverse table",
       OpenWorld = false)]
    public static async Task<CallToolResult?> Dataverse_DeleteEntity(
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       [Description("Name of the dynamics host (eg companyName.crm4.dynamics.com)")] string dynamicsHost,
       [Description("Name of the table containing the entity")] string tableLogicalName,
       [Description("Guid of the entity to delete")] string entityId,
       CancellationToken cancellationToken = default)
           => await requestContext.WithExceptionCheck(async () =>
    {
        var tokenService = serviceProvider.GetService<HeaderProvider>();
        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var oAuthSettings = serviceProvider.GetRequiredService<OAuthSettings>();
        var serverConfig = serviceProvider.GetServerConfig(requestContext.Server);

        if (string.IsNullOrEmpty(tokenService?.Bearer) || serverConfig == null) return null;

        using var httpClient = await httpClientFactory.GetOboHttpClient(tokenService.Bearer, dynamicsHost,
                serverConfig.Server, oAuthSettings);

        // Retrieve metadata so we know the entity set and primary id
        var metadata = await httpClient.GetEntityMetadataAsync(dynamicsHost, tableLogicalName, cancellationToken);
        if (metadata == null || metadata.EntitySetName == null || metadata.PrimaryNameAttribute == null)
        {
            return "Unable to load table metadata".ToErrorCallToolResponse();
        }

        var retrieveUri = $"https://{dynamicsHost}{DataversePluginExtensions.API_URL}{metadata.EntitySetName}({entityId})?$select={metadata.PrimaryNameAttribute}";

        using var retrieveRes = await httpClient.GetAsync(retrieveUri, cancellationToken);
        if (!retrieveRes.IsSuccessStatusCode)
        {
            var body = await retrieveRes.Content.ReadAsStringAsync(cancellationToken);
            return $"Dataverse retrieve error {retrieveRes.StatusCode}: {body}".ToErrorCallToolResponse();
        }

        var record = await retrieveRes.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        if (!record.TryGetProperty(metadata.PrimaryNameAttribute, out var nameProp))
        {
            return $"Record {entityId} does not contain primary name attribute {metadata.PrimaryNameAttribute}.".ToErrorCallToolResponse();
        }

        var primaryName = nameProp.GetString() ?? null;
        var confirmationString = string.IsNullOrEmpty(primaryName) ? entityId : primaryName;

        return await requestContext.ConfirmAndDeleteAsync<DeleteDataverseEntity>(
            confirmationString!,
            async _ =>
            {
                var deleteUri = $"https://{dynamicsHost}{DataversePluginExtensions.API_URL}{metadata.EntitySetName}({entityId})";
                using var deleteRes = await httpClient.DeleteAsync(deleteUri, cancellationToken);
                if (!deleteRes.IsSuccessStatusCode)
                {
                    var body = await deleteRes.Content.ReadAsStringAsync(cancellationToken);

                    throw new Exception(body);
                }
            },
            "Entity deleted.",
            cancellationToken);

    });

    [Description("Create a new entity in a Dataverse table")]
    [McpServerTool(Title = "New entity in Dataverse table",
        OpenWorld = false)]
    public static async Task<CallToolResult?> Dataverse_CreateEntity(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Name of the dynamics host (eg companyName.crm4.dynamics.com)")] string dynamicsHost,
        [Description("Name of the table to create the entity in")] string tableLogicalName,
        [Description("Default values for the entity. Format: key is argument name (without braces), value is default value.")] Dictionary<string, string>? replacements = null,
        CancellationToken cancellationToken = default)
          => await requestContext.WithExceptionCheck(async () =>
    {
        var tokenService = serviceProvider.GetService<HeaderProvider>();
        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var oAuthSettings = serviceProvider.GetRequiredService<OAuthSettings>();
        var serverConfig = serviceProvider.GetServerConfig(requestContext.Server);

        if (string.IsNullOrEmpty(tokenService?.Bearer) || serverConfig == null) return null;

        using var httpClient = await httpClientFactory.GetOboHttpClient(tokenService.Bearer, dynamicsHost,
                serverConfig.Server, oAuthSettings);


        var metadata = await httpClient.GetEntityMetadataAsync(dynamicsHost, tableLogicalName, cancellationToken);

        Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition> properties =
            metadata.Attributes
                .ToDictionary(a => a.LogicalName, a => (ElicitRequestParams.PrimitiveSchemaDefinition)
                new ElicitRequestParams.StringSchema()
                {
                    Title = a.LogicalName
                });

        if (metadata.Attributes == null)
        {
            return "Error".ToErrorCallToolResponse();
        }

        var result = await requestContext.Server.ElicitAsync(new ElicitRequestParams()
        {
            Message = $"Please fill in the details for the {tableLogicalName} item",
            RequestedSchema = new ElicitRequestParams.RequestSchema()
            {
                Properties = await metadata.Attributes
                    .GetSupportedAttributes()
                    .MapMetadataToElicit(dynamicsHost, httpClient, tableLogicalName, cancellationToken),
                Required = [.. metadata.Attributes
                        .GetSupportedAttributes()
                        .Where(a => a.RequiredLevel.Value == "ApplicationRequired")
                        .Select(a => a.LogicalName ?? a.SchemaName)]
            },
        }, cancellationToken);

        var answers = result.Content ?? new Dictionary<string, JsonElement>();
        var payload = await answers.MapElicitToPayload(metadata.Attributes, httpClient, dynamicsHost, tableLogicalName, cancellationToken: cancellationToken);
        var createUri = $"https://{dynamicsHost}{DataversePluginExtensions.API_URL}{metadata.EntitySetName}";

        Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        }));

        using var res = await httpClient.PostAsJsonAsync(createUri, payload, cancellationToken);

        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(cancellationToken);
            return $"Dataverse error {res.StatusCode}: {body}".ToErrorCallToolResponse();
        }

        return payload.ToJsonContentBlock(res.Headers.Location?.AbsoluteUri.ToString() ?? string.Empty)
            .ToCallToolResult();


    });

}

