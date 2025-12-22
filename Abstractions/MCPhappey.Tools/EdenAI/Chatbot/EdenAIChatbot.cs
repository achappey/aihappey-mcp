using System.ComponentModel;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;
using MCPhappey.Common.Extensions;

namespace MCPhappey.Tools.EdenAI.Chatbot;

public static class EdenAIChatbot
{
    [Description("Ask a question to an existing Ask YODA AI project using its LLM.")]
    [McpServerTool(
      Title = "EdenAI Chatbot Ask",
      Name = "edenai_chatbot_ask_llm",
      Destructive = false)]
    public static async Task<CallToolResult?> EdenAIChatbot_AskLLM(
      IServiceProvider serviceProvider,
      RequestContext<CallToolRequestParams> requestContext,
      [Description("Project ID of the Ask YODA project.")] string projectId,
      [Description("User question or query about the project data.")] string query,
      [Description("LLM provider to use (optional, e.g., openai, cohere, mistral).")] string? llmProvider = null,
      [Description("LLM model to use (optional, e.g., gpt-4o-mini, mistral-medium).")] string? llmModel = null,
      [Description("Number of results to return. Default: 3.")] int k = 3,
      [Description("Minimum score threshold for valid responses (0–1). Default: 0.")] double minScore = 0,
      [Description("Temperature (0–2). Default: 0.")] double temperature = 0,
      [Description("Max tokens to generate. Default: 1024.")] int maxTokens = 1024,
      [Description("Global system message that defines chatbot behavior.")] string? chatbotGlobalAction = null,
      [Description("Enable experimental reranking. Default: false.")] bool useReranking = false,
      [Description("Top-N documents returned when reranking. Default: 3.")] int topN = 3,
      CancellationToken cancellationToken = default)
      => await requestContext.WithExceptionCheck(async () =>
     await requestContext.WithStructuredContent(async () =>
      {
          // 1️⃣ Dependencies
          var eden = serviceProvider.GetRequiredService<EdenAIClient>();

          if (string.IsNullOrWhiteSpace(projectId))
              throw new ArgumentNullException(nameof(projectId), "Project ID is required.");
          if (string.IsNullOrWhiteSpace(query))
              throw new ArgumentNullException(nameof(query), "Query is required.");

          // 2️⃣ Build payload
          var payload = new Dictionary<string, object?>
          {
              ["query"] = query,
              ["k"] = k,
              ["min_score"] = minScore,
              ["temperature"] = temperature,
              ["max_tokens"] = maxTokens,
              ["use_reranking"] = useReranking,
              ["top_n"] = topN
          };

          if (!string.IsNullOrWhiteSpace(llmProvider))
              payload["llm_provider"] = llmProvider;

          if (!string.IsNullOrWhiteSpace(llmModel))
              payload["llm_model"] = llmModel;

          if (!string.IsNullOrWhiteSpace(chatbotGlobalAction))
              payload["chatbot_global_action"] = chatbotGlobalAction;

          // 3️⃣ Build endpoint URL
          var endpoint = $"aiproducts/askyoda/v2/{projectId}/ask_llm/";

          // 4️⃣ Make the Eden AI call
          return await eden.PostAsync(endpoint, payload, cancellationToken)
              ?? throw new Exception("Eden AI returned no response.");
      }));

    [Description("Create a new Ask YODA AI project using Eden AI.")]
    [McpServerTool(
          Title = "EdenAI Chatbot Create Project",
          Name = "edenai_chatbot_create_project",
          Destructive = false)]
    public static async Task<CallToolResult?> EdenAIChatbot_CreateProject(
          IServiceProvider serviceProvider,
          RequestContext<CallToolRequestParams> requestContext,
          [Description("Name of the project to create.")] string projectName,
          [Description("Database collection name to store your data.")] string collectionName,
          [Description("Embeddings provider (openai, cohere, google, mistral, jina).")] string embeddingsProvider = "openai",
          [Description("LLM provider for chat (openai, mistral, cohere, google).")] string llmProvider = "openai",
          [Description("LLM model name (e.g., gpt-4o-mini, mistral-medium).")] string llmModel = "gpt-4o-mini",
          [Description("Database provider (qdrant or supabase). Default: qdrant.")] string dbProvider = "qdrant",
          [Description("OCR provider (default: amazon).")] string ocrProvider = "amazon",
          [Description("Speech-to-text provider (default: openai).")] string speechToTextProvider = "openai",
          [Description("Chunk size (1–10000). Default: 1000.")] int chunkSize = 1000,
          [Description("Optional text separators used for chunking.")] string? chunkSeparators = null,
          [Description("Optional credential resource name.")] string? credential = null,
          [Description("Optional asset sub-resource name.")] string? asset = null,
          CancellationToken cancellationToken = default)
          => await requestContext.WithExceptionCheck(async () =>
           await requestContext.WithStructuredContent(async () =>
          {
              // 1️⃣ Elicit or confirm all project parameters
              var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                  new EdenAIChatbotProjectRequest
                  {
                      ProjectName = projectName,
                      CollectionName = collectionName,
                      EmbeddingsProvider = embeddingsProvider,
                      LlmProvider = llmProvider,
                      LlmModel = llmModel,
                      DbProvider = dbProvider,
                      OcrProvider = ocrProvider,
                      SpeechToTextProvider = speechToTextProvider,
                      ChunkSize = chunkSize,
                      ChunkSeparators = chunkSeparators,
                      Credential = credential,
                      Asset = asset
                  },
                  cancellationToken);

              // 2️⃣ Get Eden client
              var eden = serviceProvider.GetRequiredService<EdenAIClient>();

              // 3️⃣ Build JSON payload
              var payload = new Dictionary<string, object?>
              {
                  ["project_name"] = typed.ProjectName,
                  ["collection_name"] = typed.CollectionName,
                  ["embeddings_provider"] = typed.EmbeddingsProvider,
                  ["llm_provider"] = typed.LlmProvider,
                  ["llm_model"] = typed.LlmModel,
                  ["db_provider"] = typed.DbProvider,
                  ["ocr_provider"] = typed.OcrProvider,
                  ["speech_to_text_provider"] = typed.SpeechToTextProvider,
                  ["chunk_size"] = typed.ChunkSize,
              };

              if (!string.IsNullOrWhiteSpace(typed.ChunkSeparators))
                  payload["chunk_separators"] = typed.ChunkSeparators.Split(',').Select(x => x.Trim()).ToArray();

              if (!string.IsNullOrWhiteSpace(typed.Credential))
                  payload["credential"] = typed.Credential;

              if (!string.IsNullOrWhiteSpace(typed.Asset))
                  payload["asset"] = typed.Asset;

              // 4️⃣ Call Eden AI API
              var resultNode = await eden.PostAsync("aiproducts/askyoda/v2/", payload, cancellationToken)
                  ?? throw new Exception("Eden AI returned no response.");

              // 5️⃣ Extract success info
              var projectId = resultNode["project_id"]?.GetValue<string>()
                           ?? resultNode["id"]?.GetValue<string>()
                           ?? "unknown";

              return resultNode;
          }));

    [Description("Please fill in the Eden AI chatbot project creation request.")]
    public class EdenAIChatbotProjectRequest
    {
        [Required]
        [JsonPropertyName("project_name")]
        [Description("Project name to create.")]
        public string ProjectName { get; set; } = default!;

        [Required]
        [JsonPropertyName("collection_name")]
        [Description("Database collection name.")]
        public string CollectionName { get; set; } = default!;

        [Required]
        [JsonPropertyName("embeddings_provider")]
        [Description("Embeddings provider (openai, cohere, google, mistral, jina).")]
        public string EmbeddingsProvider { get; set; } = "openai";

        [Required]
        [JsonPropertyName("llm_provider")]
        [Description("Default LLM provider (e.g., openai, mistral, cohere, google).")]
        public string LlmProvider { get; set; } = "openai";

        [Required]
        [JsonPropertyName("llm_model")]
        [Description("LLM model name, e.g., gpt-4o-mini or mistral-medium.")]
        public string LlmModel { get; set; } = "gpt-4o-mini";

        [JsonPropertyName("db_provider")]
        [Description("Database provider (qdrant or supabase).")]
        public string DbProvider { get; set; } = "qdrant";

        [JsonPropertyName("ocr_provider")]
        [Description("OCR provider for documents (default: amazon).")]
        public string OcrProvider { get; set; } = "amazon";

        [JsonPropertyName("speech_to_text_provider")]
        [Description("Speech-to-text provider (default: openai).")]
        public string SpeechToTextProvider { get; set; } = "openai";

        [JsonPropertyName("chunk_size")]
        [Range(1, 10000)]
        [Description("Chunk size for splitting data. Default: 1000.")]
        public int ChunkSize { get; set; } = 1000;

        [JsonPropertyName("chunk_separators")]
        [Description("Comma-separated list of chunk separators.")]
        public string? ChunkSeparators { get; set; }

        [JsonPropertyName("credential")]
        [Description("Credential resource name (optional).")]
        public string? Credential { get; set; }

        [JsonPropertyName("asset")]
        [Description("Asset sub-resource name (optional).")]
        public string? Asset { get; set; }
    }
}