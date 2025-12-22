using System.ComponentModel;
using MCPhappey.Core.Extensions;
using MCPhappey.Common.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;
using MCPhappey.Common.Models;

namespace MCPhappey.Tools.EdenAI.Prompts;

public static class EdenAIPrompts
{
    [Description("Delete a specific prompt from Eden AI.")]
    [McpServerTool(
       Title = "Delete Eden AI Prompt",
       Name = "edenai_prompts_delete",
       Destructive = true,
       ReadOnly = false)]
    public static async Task<CallToolResult?> EdenAIPrompts_Delete(
       [Description("Name of the prompt to delete.")] string name,
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       CancellationToken cancellationToken = default)
       => await requestContext.WithExceptionCheck(async () =>
       {
           // 🧩 Dependencies
           var eden = serviceProvider.GetRequiredService<EdenAIClient>();

           // 🗑️ Use built-in helper for confirm-delete flow
           return await requestContext.ConfirmAndDeleteAsync<EdenAIDeletePrompt>(
               name,
               async _ =>
               {
                   using var req = new HttpRequestMessage(HttpMethod.Delete, $"prompts/{name}/");
                   await eden.SendAsync(req, cancellationToken);
               },
               "Prompt deleted successfully.",
               cancellationToken);
       });

    [Description("Please confirm deletion of the Eden AI prompt: {0}")]
    public class EdenAIDeletePrompt : IHasName
    {
        [Description("The name of the prompt to delete.")]
        [Required]
        public string Name { get; set; } = default!;
    }

    [Description("Execute a saved Eden AI prompt with custom variables.")]
    [McpServerTool(
      Title = "Eden AI Call Prompt",
      Name = "edenai_prompts_call",
      OpenWorld = true,
      ReadOnly = false,
      Destructive = false)]
    public static async Task<CallToolResult?> EdenAIPrompts_Call(
      IServiceProvider serviceProvider,
      RequestContext<CallToolRequestParams> requestContext,
      [Description("Name of the saved prompt to execute.")] string? name = null,
      [Description("Optional model override, e.g. 'openai/gpt-4o'.")] string? model = null,
      [Description("Prompt variables as key-value pairs.")] Dictionary<string, string>? promptContext = null,
      [Description("Optional file URLs for multimodal prompts. Only public URLs are supported")] List<string>? fileUrls = null,
      CancellationToken cancellationToken = default)
      => await requestContext.WithExceptionCheck(async () =>
      {
          // 🧠 1. Elicit missing fields
          var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
              new EdenAICallPrompt
              {
                  Name = name ?? string.Empty,
                  Model = model,
              },
              cancellationToken);

          // 🧱 2. Dependencies
          var eden = serviceProvider.GetRequiredService<EdenAIClient>();

          // 🧩 3. Build payload
          var payload = new Dictionary<string, object?>();

          if (!string.IsNullOrWhiteSpace(typed.Model))
              payload["model"] = typed.Model;

          if (promptContext is { Count: > 0 })
              payload["prompt_context"] = promptContext;

          if (fileUrls is { Count: > 0 })
              payload["file_urls"] = fileUrls.ToArray();

          // 🚀 4. Call endpoint
          var resultNode = await eden.PostAsync($"prompts/{typed.Name}/", payload, cancellationToken)
              ?? throw new Exception("Eden AI returned no response.");

          // 🎯 5. Return structured response
          return new CallToolResult
          {
              StructuredContent = resultNode
          };
      });

    [Description("Please fill in the Eden AI prompt execution details.")]
    public class EdenAICallPrompt
    {
        [JsonPropertyName("name")]
        [Required]
        [Description("Name of the saved Eden AI prompt to execute.")]
        public string Name { get; set; } = default!;

        [JsonPropertyName("model")]
        [Description("Optional model override, e.g. 'openai/gpt-4o'.")]
        public string? Model { get; set; }

    }

    [Description("Create a new prompt template in Eden AI.")]
    [McpServerTool(
       Title = "Eden AI Create Prompt",
       Name = "edenai_prompts_create",
       OpenWorld = true,
       ReadOnly = false,
       Destructive = false)]
    public static async Task<CallToolResult?> EdenAIPrompts_Create(
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       [Description("Unique identifier for the prompt (letters, numbers, _, -).")] string? name = null,
       [Description("Prompt text. Variables can be wrapped in {{ }}.")] string? text = null,
       [Description("Model used for this prompt, e.g. openai/gpt-4o.")] string? model = null,
       [Description("Optional system prompt text.")] string? systemPrompt = null,
       [Description("Optional file URLs for multimodal prompts. Only public URLs are supported")] List<string>? fileUrls = null,
       CancellationToken cancellationToken = default)
       => await requestContext.WithExceptionCheck(async () =>
       await requestContext.WithStructuredContent(async () =>
       {
           // 🧠 1. Elicit missing parameters
           var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
               new EdenAINewPrompt
               {
                   Name = name ?? string.Empty,
                   Text = text ?? string.Empty,
                   Model = model ?? "openai/gpt-4o",
                   SystemPrompt = systemPrompt,
               },
               cancellationToken);

           // 🧩 2. Dependencies
           var eden = serviceProvider.GetRequiredService<EdenAIClient>();

           // 🧱 3. Build payload
           var payload = new Dictionary<string, object?>
           {
               ["name"] = typed.Name,
               ["text"] = typed.Text,
               ["model"] = typed.Model
           };

           if (!string.IsNullOrWhiteSpace(typed.SystemPrompt))
               payload["system_prompt"] = typed.SystemPrompt;

           if (fileUrls != null && fileUrls.Any())
               payload["file_urls"] = fileUrls.ToArray();

           // 🚀 4. Send request
           return await eden.PostAsync("prompts/", payload, cancellationToken)
               ?? throw new Exception("Eden AI returned no response.");
       }));

    [Description("Please fill in the Eden AI prompt creation details.")]
    public class EdenAINewPrompt
    {
        [JsonPropertyName("name")]
        [Required]
        [Description("Unique name for the prompt (letters, numbers, _, -).")]
        public string Name { get; set; } = default!;

        [JsonPropertyName("text")]
        [Required]
        [Description("Prompt text content. Variables allowed with {{ }}.")]
        public string Text { get; set; } = default!;

        [JsonPropertyName("model")]
        [Required]
        [Description("Model identifier, e.g. openai/gpt-4o.")]
        public string Model { get; set; } = "openai/gpt-4o";

        [JsonPropertyName("system_prompt")]
        [Description("Optional system prompt text.")]
        public string? SystemPrompt { get; set; }
    }
}