using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.Mem0;

public static class Mem0Service
{
    public static async Task<JsonNode?> SendAsync(this
    IServiceProvider serviceProvider,
    string endpoint,
    object body,
    CancellationToken ct)
    {
        var mem0Settings = serviceProvider.GetRequiredService<Mem0Settings>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        using var client = clientFactory.CreateClient();
        var jsonContent = JsonSerializer.Serialize(body);

        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(jsonContent, Encoding.UTF8, MimeTypes.Json)
        };

        req.Headers.Authorization = new AuthenticationHeaderValue("Token", mem0Settings.ApiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));

        using var resp = await client.SendAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode) throw new Exception($"{resp.StatusCode}: {json}");

        JsonNode? parsed = await JsonNode.ParseAsync(BinaryData.FromString(json).ToStream(), cancellationToken: ct);

        if (parsed is JsonArray arr)
        {
            return new JsonObject
            {
                ["results"] = arr
            };
        }

        return parsed;
    }

    // ---------------------------------------------------------------------
    //  SHARED INTERNAL HELPERS
    // ---------------------------------------------------------------------
    private static async Task<JsonNode?> SendMem0RequestAsync(
        IServiceProvider serviceProvider,
        string endpoint,
        object body,
        CancellationToken cancellationToken)
    {
        var mem0Settings = serviceProvider.GetRequiredService<Mem0Settings>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        using var client = clientFactory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, MimeTypes.Json)
        };

        req.Headers.Authorization = new AuthenticationHeaderValue("Token", mem0Settings.ApiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));

        using var resp = await client.SendAsync(req, cancellationToken);
        var json = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{resp.StatusCode}: {json}");

        JsonNode? parsed = await JsonNode.ParseAsync(BinaryData.FromString(json).ToStream(), cancellationToken: cancellationToken);

        if (parsed is JsonArray arr)
        {
            return new JsonObject
            {
                ["results"] = arr
            };
        }

        return parsed;
    }

    public static string? FormatDate(this DateTime? date)
        => date.HasValue && date.Value > DateTime.MinValue ? date.Value.ToString("yyyy-MM-dd") : null;


}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Mem0RoleType
{
    [EnumMember(Value = "user")]
    user,

    [EnumMember(Value = "assistant")]
    assistant
}


[Description("Please fill in the memory id: {0}")]
public class Mem0DeleteMemory : IHasName
{
    [JsonPropertyName("name")]
    [Description("Id of the memory.")]
    public string Name { get; set; } = default!;
}

// ---------------------------------------------------------------------
//  SHARED INPUT CLASS
// ---------------------------------------------------------------------

[Description("Please fill in the memory details to be stored in Mem0.")]
public class Mem0AddMemory
{
    [Required]
    [Description("Role of the message sender ('user' or 'assistant').")]
    [JsonPropertyName("role")]
    public Mem0RoleType Role { get; set; }

    [Required]
    [Description("Message text to store as memory.")]
    [JsonPropertyName("content")]
    public string Content { get; set; } = default!;

    [Required]
    [Description("Whether to infer the memories or directly store the message.")]
    [JsonPropertyName("infer")]
    public bool Infer { get; set; } = true;

    [Required]
    [Description("Whether the memory is immutable.")]
    [JsonPropertyName("immutable")]
    public bool Immutable { get; set; } = false;

    [Description("The date when the memory will expire (YYYY-MM-DD).")]
    [JsonPropertyName("expiration_date")]
    public DateTime? ExpirationDate { get; set; }
}
// minimal structured model for update
public class Mem0UpdateMemory
{
    [Required]
    [JsonPropertyName("text")]
    [Description("Updated message text to store in the memory.")]
    public string Text { get; set; } = default!;
}

public class Mem0Settings
{
    public string ApiKey { get; set; } = default!;
}
