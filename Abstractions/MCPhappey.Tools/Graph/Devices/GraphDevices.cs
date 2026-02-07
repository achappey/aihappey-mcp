using System.ComponentModel;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Devices;

public static class GraphDevices
{
    [Description("Retire (remove company data from) an Intune managed device by ID.")]
    [McpServerTool(Title = "Retire Intune device", OpenWorld = false, Destructive = true)]
    public static async Task<CallToolResult?> GraphDevices_Retire(
          RequestContext<CallToolRequestParams> requestContext,
          [Description("The device id to retire.")] string deviceId,
          CancellationToken cancellationToken = default) =>
          await requestContext.WithExceptionCheck(async () =>
          await requestContext.WithOboGraphClient(async client =>
          await requestContext.WithStructuredContent(async () =>
        {
            var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
                new GraphRetireDevice
                {
                    DeviceId = deviceId,
                },
                cancellationToken
            );

            await client
                .DeviceManagement
                .ManagedDevices[typed?.DeviceId]
                .Retire
                .PostAsync(cancellationToken: cancellationToken);

            var now = DateTimeOffset.UtcNow;

            // Build a compact, linkable result payload
            var graphResult = new
            {
                deviceId = typed?.DeviceId,
                action = "retire",
                status = "submitted",
                submittedAtUtc = now.ToString("yyyy-MM-dd HH:mm:ss'Z'"),
                graphPath = $"/deviceManagement/managedDevices/{typed?.DeviceId}/retire"
            };

            return graphResult;
        })));

    [Description("Delete an Intune managed device (removes it from Intune).")]
    [McpServerTool(Title = "Delete Intune device", OpenWorld = false, Destructive = true)]
    public static async Task<CallToolResult?> GraphDevices_DeleteIntune(
          [Description("The Intune managedDevice ID to delete.")] string deviceId,
          RequestContext<CallToolRequestParams> requestContext,
          CancellationToken cancellationToken = default) =>
            await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async client =>
    {
        // Fetch a minimal projection to confirm with a human-friendly name
        var device = await client.DeviceManagement.ManagedDevices[deviceId]
            .GetAsync(static rq =>
            {
                rq.QueryParameters.Select = ["id", "deviceName", "userPrincipalName"];
            }, cancellationToken);

        if (device is null)
            throw new InvalidOperationException($"Managed device with ID '{deviceId}' was not found.");

        var displayName = string.IsNullOrWhiteSpace(device.DeviceName)
            ? device.Id
            : device.DeviceName;

        // Ask user to confirm by typing the device name (or ID if name missing)
        return await requestContext.ConfirmAndDeleteAsync<GraphDeleteDevice>(
            expectedName: displayName!,
            deleteAction: async _ =>
            {
                // DELETE /deviceManagement/managedDevices/{managedDeviceId}
                await client.DeviceManagement.ManagedDevices[deviceId]
                    .DeleteAsync(cancellationToken: cancellationToken);
            },
            successText: $"Device '{displayName}' ({device.Id}) has been deleted from Intune.",
            ct: cancellationToken);
    }));


    [Description("Delete an Entra ID device object. Accepts either the Entra deviceId (GUID) or an Intune managedDeviceId (will resolve to azureADDeviceId).")]
    [McpServerTool(Title = "Delete Entra device", OpenWorld = false, Destructive = true, ReadOnly = false)]
    public static async Task<CallToolResult?> GraphDevices_DeleteEntra(
        [Description("Entra device object ID (GUID). Optional if managedDeviceId is provided.")]
        string? entraDeviceId,
        [Description("Intune managedDeviceId (will be resolved to the Entra device’s azureADDeviceId). Optional if entraDeviceId is provided.")]
        string? managedDeviceId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
            await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async client =>
        {
            if (string.IsNullOrWhiteSpace(entraDeviceId) && string.IsNullOrWhiteSpace(managedDeviceId))
                throw new ArgumentException("Provide either entraDeviceId or managedDeviceId.");

            // Resolve Entra deviceId from Intune managed device if needed
            if (string.IsNullOrWhiteSpace(entraDeviceId))
            {
                var md = await client.DeviceManagement.ManagedDevices[managedDeviceId!]
                    .GetAsync(rq =>
                    {
                        rq.QueryParameters.Select = ["id", "azureADDeviceId", "deviceName", "userPrincipalName"];
                    }, cancellationToken);

                if (md is null || md.AzureADDeviceId is null)
                    throw new InvalidOperationException($"Managed device '{managedDeviceId}' not found or has no azureADDeviceId.");

                entraDeviceId = md.AzureADDeviceId;
            }

            // Fetch Entra device display name for human confirmation
            var device = await client.Devices[entraDeviceId!]
                .GetAsync(rq =>
                {
                    rq.QueryParameters.Select = ["id", "displayName", "deviceId", "deviceOwnership", "operatingSystem"];
                }, cancellationToken);

            if (device is null)
                throw new InvalidOperationException($"Entra device '{entraDeviceId}' not found.");

            var display = string.IsNullOrWhiteSpace(device.DisplayName) ? device.Id : device.DisplayName;

            // Optional guardrails: block if operatingSystem is empty and you want to avoid service principals etc.
            if (string.IsNullOrEmpty(device.OperatingSystem))
                throw new InvalidOperationException("Refusing to delete: object does not look like a device (no operatingSystem).");

            return await requestContext.ConfirmAndDeleteAsync<GraphDeleteDevice>(
                expectedName: display!,
                deleteAction: async _ =>
                {
                    // DELETE /devices/{id}
                    await client.Devices[device!.Id!].DeleteAsync(cancellationToken: cancellationToken);
                },
                successText: $"Entra device '{display}' ({device.Id}) deleted.",
                ct: cancellationToken);
        }));
}


