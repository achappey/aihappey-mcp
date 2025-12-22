using System.ComponentModel;
using MCPhappey.Core.Extensions;
using MCPhappey.Common.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MCPhappey.Core.Services;
using System.Text;
using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;

namespace MCPhappey.Tools.EdenAI.Text;

public static class EdenAIText
{
    [Description("Generate source code using Eden AI text models.")]
    [McpServerTool(
      Title = "Generate code with Eden AI",
      Name = "edenai_text_code_generation",
      OpenWorld = true,
      ReadOnly = false,
      Destructive = false)]
    public static async Task<CallToolResult?> EdenAIText_CodeGeneration_Create(
      [Description("Instruction describing the code to generate.")] string instruction,
      IServiceProvider serviceProvider,
      RequestContext<CallToolRequestParams> requestContext,
      [Description("Optional context or source code to build from.")] string? prompt = null,
      [Description("AI provider to use, e.g. google, openai.")] string providers = "google",
      [Description("Temperature 0-1. Higher = more creative.")] double temperature = 0,
      [Description("Maximum number of tokens to generate.")] int maxTokens = 1000,
      CancellationToken cancellationToken = default)
      => await requestContext.WithExceptionCheck(async () =>
      await requestContext.WithStructuredContent(async () =>
      {
              // 🧠 1. Elicit missing parameters with defaults
          var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
              new EdenAINewCodeGeneration
              {
                  Prompt = prompt,
                  Temperature = temperature,
                  MaxTokens = maxTokens,
                  Instruction = instruction,
                  Providers = providers
              },
              cancellationToken);


          var eden = serviceProvider.GetRequiredService<EdenAIClient>();
          var requestBody = new
          {
              providers = typed.Providers,
              instruction = typed.Instruction,
              prompt = typed.Prompt,
              temperature = typed.Temperature,
              max_tokens = typed.MaxTokens,
              response_as_dict = true,
              attributes_as_list = false,
              show_original_response = false
          };

          return await eden.PostAsync("text/code_generation/", requestBody, cancellationToken);
      }));

    [Description("Please fill in the Eden AI code generation request details.")]
    public class EdenAINewCodeGeneration
    {
        [JsonPropertyName("instruction")]
        [Required]
        [Description("Instruction describing what code to generate.")]
        public string Instruction { get; set; } = default!;

        [JsonPropertyName("prompt")]
        [Description("Optional context or existing code snippet.")]
        public string? Prompt { get; set; }

        [JsonPropertyName("providers")]
        [Required]
        [Description("AI provider to use, e.g. google or openai.")]
        public string Providers { get; set; } = "google";

        [JsonPropertyName("temperature")]
        [Range(0, 1)]
        [Description("Temperature 0-1. Higher = more creative results.")]
        public double Temperature { get; set; } = 0;

        [JsonPropertyName("max_tokens")]
        [Range(1, 3000)]
        [Description("Maximum number of tokens for generated code.")]
        public int MaxTokens { get; set; } = 1000;
    }

    [Description("Detect emotions expressed in text using Eden AI models.")]
    [McpServerTool(
       Title = "Eden AI Emotion Detection",
       Name = "edenai_text_emotion_detection",
       OpenWorld = true,
       ReadOnly = false,
       Destructive = false)]
    public static async Task<CallToolResult?> EdenAI_TextEmotionDetectionAsync(
       [Description("File URLs or SharePoint/OneDrive references containing text to analyze.")] IEnumerable<string> fileUrls,
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       [Description("Primary provider, e.g. 'google', 'openai', or 'microsoft'.")] string provider = "google",
       [Description("Fallback providers (comma separated).")] string? fallbackProviders = null,
       CancellationToken cancellationToken = default)
       => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
       {

    

           // 🧱 1. Dependencies
           var eden = serviceProvider.GetRequiredService<EdenAIClient>();
           var downloadService = serviceProvider.GetRequiredService<DownloadService>();

           // 📥 2. Download and merge text from all provided URLs
           var allTexts = new List<string>();
           foreach (var url in fileUrls)
           {
               var files = await downloadService.ScrapeContentAsync(
                   serviceProvider, requestContext.Server, url, cancellationToken);

               foreach (var file in files.GetTextFiles())
               {
                   try
                   {
                       var text = Encoding.UTF8.GetString(file.Contents.ToArray());
                       if (!string.IsNullOrWhiteSpace(text))
                           allTexts.Add(text);
                   }
                   catch
                   {
                       continue;
                   }
               }
           }

           if (allTexts.Count == 0)
               throw new Exception("No readable text found in provided files.");

           var mergedText = string.Join("\n\n", allTexts);

           // 🧠 3. Build payload for Eden AI API
           var payload = new Dictionary<string, object?>
           {
               ["providers"] = provider,
               ["text"] = mergedText,
               ["response_as_dict"] = true,
               ["show_original_response"] = false
           };

           if (!string.IsNullOrWhiteSpace(fallbackProviders))
               payload["fallback_providers"] = fallbackProviders;

           // 🚀 4. Call Eden AI Emotion Detection endpoint
           return await eden.PostAsync("text/emotion_detection/", payload, cancellationToken)
               ?? throw new Exception("Eden AI returned no response.");
       }));

    [Description("Extract key topics and keywords from text using Eden AI models.")]
    [McpServerTool(
      Title = "Eden AI Keyword Extraction",
      Name = "edenai_text_keyword_extraction",
      OpenWorld = true,
      ReadOnly = false,
      Destructive = false)]
    public static async Task<CallToolResult?> EdenAI_TextKeywordExtractionAsync(
      [Description("File URLs or SharePoint/OneDrive references containing text to analyze.")] IEnumerable<string> fileUrls,
      IServiceProvider serviceProvider,
      RequestContext<CallToolRequestParams> requestContext,
      [Description("Primary provider, e.g. 'google', 'openai', or 'microsoft'.")] string provider = "google",
      [Description("Fallback providers (comma separated).")] string? fallbackProviders = null,
      [Description("Language code, e.g. 'en' or 'nl'.")] string? language = null,
      CancellationToken cancellationToken = default)
      => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
      {
          // 🧱 1. Dependencies
          var eden = serviceProvider.GetRequiredService<EdenAIClient>();
          var downloadService = serviceProvider.GetRequiredService<DownloadService>();

          // 📥 2. Download and merge text
          var allTexts = new List<string>();
          foreach (var url in fileUrls)
          {
              var files = await downloadService.ScrapeContentAsync(
                  serviceProvider, requestContext.Server, url, cancellationToken);

              foreach (var file in files.GetTextFiles())
              {
                  try
                  {
                      var text = Encoding.UTF8.GetString(file.Contents.ToArray());
                      if (!string.IsNullOrWhiteSpace(text))
                          allTexts.Add(text);
                  }
                  catch
                  {
                      continue;
                  }
              }
          }

          if (allTexts.Count == 0)
              throw new Exception("No readable text found in provided files.");

          var mergedText = string.Join("\n\n", allTexts);

          // 🧠 3. Build JSON payload
          var payload = new Dictionary<string, object?>
          {
              ["providers"] = provider,
              ["text"] = mergedText,
              ["response_as_dict"] = true,
              ["show_original_response"] = false
          };

          if (!string.IsNullOrWhiteSpace(language))
              payload["language"] = language;

          if (!string.IsNullOrWhiteSpace(fallbackProviders))
              payload["fallback_providers"] = fallbackProviders;

          // 🚀 4. Call Eden AI
          return await eden.PostAsync("text/keyword_extraction/", payload, cancellationToken)
              ?? throw new Exception("Eden AI returned no response.");
      }));

    [Description("Optimize AI prompts for clarity and performance using Eden AI models.")]
    [McpServerTool(
       Title = "Eden AI Prompt Optimization",
       Name = "edenai_text_prompt_optimization",
       OpenWorld = true,
       ReadOnly = false,
       Destructive = false)]
    public static async Task<CallToolResult?> EdenAI_TextPromptOptimizationAsync(
       [Description("File URLs or SharePoint/OneDrive references containing prompts to optimize.")] IEnumerable<string> fileUrls,
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       [Description("Primary provider, e.g. 'openai' or 'google'.")] string provider = "openai",
       [Description("Target provider for which to optimize the prompt, e.g. 'mistral' or 'anthropic'.")] string targetProvider = "openai",
       [Description("Fallback providers (comma separated).")] string? fallbackProviders = null,
       CancellationToken cancellationToken = default)
       => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
       {
           // 🧱 1. Dependencies
           var eden = serviceProvider.GetRequiredService<EdenAIClient>();
           var downloadService = serviceProvider.GetRequiredService<DownloadService>();

           // 📥 2. Download and merge prompt text
           var allPrompts = new List<string>();
           foreach (var url in fileUrls)
           {
               var files = await downloadService.ScrapeContentAsync(
                   serviceProvider, requestContext.Server, url, cancellationToken);

               foreach (var file in files.GetTextFiles())
               {
                   try
                   {
                       var text = Encoding.UTF8.GetString(file.Contents.ToArray());
                       if (!string.IsNullOrWhiteSpace(text))
                           allPrompts.Add(text);
                   }
                   catch
                   {
                       continue;
                   }
               }
           }

           if (allPrompts.Count == 0)
               throw new Exception("No readable text found in provided files.");

           var mergedText = string.Join("\n\n", allPrompts);

           // 🧠 3. Build JSON payload
           var payload = new Dictionary<string, object?>
           {
               ["providers"] = provider,
               ["text"] = mergedText,
               ["target_provider"] = targetProvider,
               ["response_as_dict"] = true,
               ["show_original_response"] = false
           };

           if (!string.IsNullOrWhiteSpace(fallbackProviders))
               payload["fallback_providers"] = fallbackProviders;

           // 🚀 4. Call Eden AI
           return await eden.PostAsync("text/prompt_optimization/", payload, cancellationToken)
               ?? throw new Exception("Eden AI returned no response.");

       }));

    [Description("Analyze sentiment of text using Eden AI models.")]
    [McpServerTool(
      Title = "Eden AI Sentiment Analysis",
      Name = "edenai_text_sentiment_analysis",
      OpenWorld = true,
      ReadOnly = false,
      Destructive = false)]
    public static async Task<CallToolResult?> EdenAI_TextSentimentAnalysisAsync(
      [Description("File URLs or SharePoint/OneDrive references containing text to analyze.")] IEnumerable<string> fileUrls,
      IServiceProvider serviceProvider,
      RequestContext<CallToolRequestParams> requestContext,
      [Description("Primary provider, e.g. 'google', 'openai', or 'microsoft'.")] string provider = "google",
      [Description("Fallback providers (comma separated).")] string? fallbackProviders = null,
      [Description("Language code, e.g. 'en' or 'nl'.")] string? language = null,
      CancellationToken cancellationToken = default)
      => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
      {
          // 🧱 1. Dependencies
          var eden = serviceProvider.GetRequiredService<EdenAIClient>();
          var downloadService = serviceProvider.GetRequiredService<DownloadService>();

          // 📥 2. Download and merge text
          var allTexts = new List<string>();
          foreach (var url in fileUrls)
          {
              var files = await downloadService.ScrapeContentAsync(
                  serviceProvider, requestContext.Server, url, cancellationToken);

              foreach (var file in files.GetTextFiles())
              {
                  try
                  {
                      var text = Encoding.UTF8.GetString(file.Contents.ToArray());
                      if (!string.IsNullOrWhiteSpace(text))
                          allTexts.Add(text);
                  }
                  catch
                  {
                      continue;
                  }
              }
          }

          if (allTexts.Count == 0)
              throw new Exception("No readable text found in provided files.");

          var mergedText = string.Join("\n\n", allTexts);

          // 🧠 3. Build payload
          var payload = new Dictionary<string, object?>
          {
              ["providers"] = provider,
              ["text"] = mergedText,
              ["response_as_dict"] = true,
              ["show_original_response"] = false
          };

          if (!string.IsNullOrWhiteSpace(language))
              payload["language"] = language;

          if (!string.IsNullOrWhiteSpace(fallbackProviders))
              payload["fallback_providers"] = fallbackProviders;

          // 🚀 4. Call Eden AI
          return await eden.PostAsync("text/sentiment_analysis/", payload, cancellationToken)
              ?? throw new Exception("Eden AI returned no response.");
      }));

    [Description("Check and correct spelling in text using Eden AI models.")]
    [McpServerTool(
      Title = "Eden AI Spell Check",
      Name = "edenai_text_spellcheck",
      OpenWorld = true,
      ReadOnly = false,
      Destructive = false)]
    public static async Task<CallToolResult?> EdenAI_TextSpellCheckAsync(
      [Description("File URLs or SharePoint/OneDrive references containing text to check.")] IEnumerable<string> fileUrls,
      IServiceProvider serviceProvider,
      RequestContext<CallToolRequestParams> requestContext,
      [Description("Primary provider, e.g. 'openai' or 'microsoft'.")] string provider = "openai",
      [Description("Fallback providers (comma separated).")] string? fallbackProviders = null,
      [Description("Language code, e.g. 'en' or 'nl'.")] string? language = null,
      CancellationToken cancellationToken = default)
      => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
      {
          // 🧱 1. Dependencies
          var eden = serviceProvider.GetRequiredService<EdenAIClient>();
          var downloadService = serviceProvider.GetRequiredService<DownloadService>();

          // 📥 2. Download and combine text
          var allTexts = new List<string>();
          foreach (var url in fileUrls)
          {
              var files = await downloadService.ScrapeContentAsync(
                  serviceProvider, requestContext.Server, url, cancellationToken);
              var textFiles = files.GetTextFiles();

              foreach (var file in textFiles)
              {
                  try
                  {
                      var text = Encoding.UTF8.GetString(file.Contents.ToArray());
                      if (!string.IsNullOrWhiteSpace(text))
                          allTexts.Add(text);
                  }
                  catch
                  {
                      continue;
                  }
              }
          }

          if (allTexts.Count == 0)
              throw new Exception("No readable text found in provided files.");

          var mergedText = string.Join("\n\n", allTexts);

          // 🧠 3. Prepare payload
          var payload = new Dictionary<string, object?>
          {
              ["providers"] = provider,
              ["text"] = mergedText,
              ["response_as_dict"] = true,
              ["show_original_response"] = false
          };

          if (!string.IsNullOrWhiteSpace(language))
              payload["language"] = language;

          if (!string.IsNullOrWhiteSpace(fallbackProviders))
              payload["fallback_providers"] = fallbackProviders;

          // 🚀 4. Call EdenAI
          return await eden.PostAsync("text/spell_check/", payload, cancellationToken)
              ?? throw new Exception("Eden AI returned no response.");
      }));

    [Description("Summarize one or more text files using Eden AI text summarization models.")]
    [McpServerTool(
        Title = "Eden AI Summarize Text",
        Name = "edenai_text_summarize",
        OpenWorld = true,
        ReadOnly = false,
        Destructive = false)]
    public static async Task<CallToolResult?> EdenAI_TextSummarizeAsync(
        [Description("File URLs or SharePoint/OneDrive references to summarize.")] IEnumerable<string> fileUrls,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Primary provider, e.g. 'google' or 'amazon'.")] string provider = "google",
        [Description("Fallback providers (comma separated).")] string? fallbackProviders = null,
        [Description("Language code, e.g. 'en' or 'nl'.")] string? language = null,
        [Description("Number of summary sentences. Defaults to 3.")] int outputSentences = 3,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
        {
            // 🧱 1. Dependencies
            var eden = serviceProvider.GetRequiredService<EdenAIClient>();
            var downloadService = serviceProvider.GetRequiredService<DownloadService>();

            // 📥 2. Download and combine all file contents
            var allTexts = new List<string>();
            foreach (var url in fileUrls)
            {
                var files = await downloadService.ScrapeContentAsync(
                    serviceProvider, requestContext.Server, url, cancellationToken);
                var textFiles = files.GetTextFiles();
                foreach (var file in textFiles)
                {
                    try
                    {
                        var text = Encoding.UTF8.GetString(file.Contents.ToArray());
                        if (!string.IsNullOrWhiteSpace(text))
                            allTexts.Add(text);
                    }
                    catch
                    {
                        // skip unreadable binary files
                        continue;
                    }
                }
            }

            if (allTexts.Count == 0)
                throw new Exception("No readable text content found in provided files.");

            var mergedText = string.Join("\n\n", allTexts);

            // 🧠 3. Build JSON payload
            var payload = new Dictionary<string, object?>
            {
                ["providers"] = provider,
                ["text"] = mergedText,
                ["response_as_dict"] = true,
                ["show_original_response"] = false,
                ["output_sentences"] = outputSentences
            };

            if (!string.IsNullOrWhiteSpace(language))
                payload["language"] = language;

            if (!string.IsNullOrWhiteSpace(fallbackProviders))
                payload["fallback_providers"] = fallbackProviders;

            // 🚀 4. Send request
            return await eden.PostAsync("text/summarize/", payload, cancellationToken)
                ?? throw new Exception("Eden AI returned no response.");

        }));
}