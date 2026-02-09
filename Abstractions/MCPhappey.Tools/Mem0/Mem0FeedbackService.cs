using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Auth.Extensions;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Mem0;

public static class Mem0FeedbackService
{
    [Description("Submit feedback for an existing memory in Mem0.")]
    [McpServerTool(
      Title = "Add memory feedback",
      Name = "mem0_add_memory_feedback",
      ReadOnly = false,
      OpenWorld = false,
      Destructive = false)]
    public static async Task<CallToolResult?> Mem0_AddMemoryFeedback(
      [Description("The ID of the memory to provide feedback for.")] string memoryId,
      [Description("Type of feedback: POSITIVE, NEGATIVE, or VERY_NEGATIVE.")] Mem0FeedbackType feedback,
      [Description("Optional reason or explanation for this feedback.")] string? feedbackReason,
      IServiceProvider serviceProvider,
      RequestContext<CallToolRequestParams> requestContext,
      CancellationToken cancellationToken = default)
      => await requestContext.WithExceptionCheck(async () =>
      await requestContext.WithStructuredContent(async () =>
      {
          ArgumentException.ThrowIfNullOrWhiteSpace(memoryId);

          var mem0Settings = serviceProvider.GetRequiredService<Mem0Settings>();
          var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
          var userId = serviceProvider.GetUserId();

          var (typed, notAccepted, _) = await requestContext.Server.TryElicit(new Mem0Feedback
          {
              Feedback = feedback,
              FeedbackReason = feedbackReason
          }, cancellationToken);

          if (notAccepted != null) throw new Exception(JsonSerializer.Serialize(notAccepted));
          if (typed == null) throw new Exception("Invalid input");

          var body = new
          {
              memory_id = memoryId,
              feedback = typed.Feedback,
              feedback_reason = typed.FeedbackReason,
              metadata = new { createdBy = userId }
          };

          var jsonBody = JsonSerializer.Serialize(body);

          using var client = clientFactory.CreateClient();
          using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.mem0.ai/v1/feedback/")
          {
              Content = new StringContent(jsonBody, Encoding.UTF8, MimeTypes.Json)
          };

          req.Headers.Authorization = new AuthenticationHeaderValue("Token", mem0Settings.ApiKey);
          req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));

          using var resp = await client.SendAsync(req, cancellationToken);
          var json = await resp.Content.ReadAsStringAsync(cancellationToken);

          if (!resp.IsSuccessStatusCode)
              throw new Exception($"{resp.StatusCode}: {json}");

          return await JsonNode.ParseAsync(BinaryData.FromString(json).ToStream());
      }));

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum Mem0FeedbackType
    {
        [EnumMember(Value = "POSITIVE")]
        Positive,

        [EnumMember(Value = "NEGATIVE")]
        Negative,

        [EnumMember(Value = "VERY_NEGATIVE")]
        VeryNegative
    }


    [Description("Please fill in the feedback details for a specific memory.")]
    public class Mem0Feedback
    {
        [Required]
        [JsonPropertyName("feedback")]
        [Description("Type of feedback: POSITIVE, NEGATIVE, or VERY_NEGATIVE.")]
        public Mem0FeedbackType Feedback { get; set; }

        [JsonPropertyName("feedback_reason")]
        [Description("Optional reason for this feedback.")]
        public string? FeedbackReason { get; set; }
    }



}

