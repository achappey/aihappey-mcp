using System.ComponentModel;
using System.Text.Json.Serialization;
using MCPhappey.Common.Models;

namespace MCPhappey.Tools.Graph.Devices;

[Description("Please fill in the retire device details.")]
public class GraphRetireDevice
{
    [JsonPropertyName("deviceId")]
    [Description("Id of the device.")]
    public string DeviceId { get; set; } = default!;
}


[Description("Please fill in the device name: {0}")]
public class GraphDeleteDevice : IHasName
{
    [JsonPropertyName("name")]
    [Description("Name of the device.")]
    public string Name { get; set; } = default!;
}
