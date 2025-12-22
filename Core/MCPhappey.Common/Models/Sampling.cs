
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;

namespace MCPhappey.Common.Models;

public class MessageResults
{
    [JsonPropertyName("results")]
    public IEnumerable<CreateMessageResult> Results { get; set; } = [];
}