using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Replicate;

public static class ReplicateService
{
    [Description("Create a prediction using a Replicate model.")]
    [McpServerTool(
        Title = "Create Replicate prediction",
        Name = "replicate_predictions_create",
        Destructive = false)]
    public static async Task<CallToolResult?> ReplicatePredictions_Create(
        [Description("Full model version or owner/model ID (e.g., stability-ai/sdxl).")]
        string version,
        [Description("Input JSON for the model (e.g., { \"prompt\": \"A photo of a cat\" }).")]
        string inputJson,
        IServiceProvider sp,
        RequestContext<CallToolRequestParams> rc,
        [Description("Max runtime before cancel (e.g., 60s, 5m). Minimum 5 seconds.")]
        string? cancelAfter = null,
        [Description("Wait time (1–60s) for intermediate results.")]
        [Range(1, 60)] int? preferWaitSeconds = null,
        CancellationToken ct = default)
        => await rc.WithExceptionCheck(async () =>
        await rc.WithStructuredContent(async () =>
    {
        var replicate = sp.GetRequiredService<ReplicateClient>();

        var (typed, _, _) = await rc.Server.TryElicit(new ReplicatePredictionRequest
        {
            Version = version,
            CancelAfter = cancelAfter,
            PreferWaitSeconds = preferWaitSeconds
        }, ct);

        // Validate input JSON
        JsonElement input;
        try
        {
            input = JsonSerializer.Deserialize<JsonElement>(inputJson);
        }
        catch (Exception ex)
        {
            throw new ValidationException($"Invalid input JSON: {ex.Message}");
        }

        return await replicate.CreatePredictionAsync(
            typed.Version,
            input,
            typed.CancelAfter,
            typed.PreferWaitSeconds,
            ct);
    }));

    [Description("Fill in the parameters for Replicate prediction creation.")]
    public class ReplicatePredictionRequest
    {
        [Required]
        [Description("The model version or identifier, e.g. stability-ai/sdxl")]
        [JsonPropertyName("version")]
        public string Version { get; set; } = default!;

        [JsonPropertyName("cancel_after")]
        [Description("Maximum run time (e.g. 1m, 90s).")]
        public string? CancelAfter { get; set; }

        [JsonPropertyName("prefer_wait_seconds")]
        [Range(1, 60)]
        [Description("Wait up to N seconds for model output before returning.")]
        public int? PreferWaitSeconds { get; set; }
    }
}
