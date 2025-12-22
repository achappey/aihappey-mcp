using System.ComponentModel;
using MCPhappey.Core.Extensions;
using MCPhappey.Common.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MCPhappey.Core.Services;
using MCPhappey.Tools.StabilityAI;
using System.Net.Http.Headers;

namespace MCPhappey.Tools.EdenAI.Translations;

public static class EdenAITranslations
{
    [Description("Detect the language of a text using Eden AI providers.")]
    [McpServerTool(
       Title = "EdenAI Language Detection",
       Name = "edenai_translation_language_detection",
       Destructive = false)]
    public static async Task<CallToolResult?> EdenAI_LanguageDetection(
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       [Description("Text to analyze and detect language from.")] string text,
       [Description("Primary provider (e.g. google, microsoft, amazon, openai, xai).")] string provider = "google",
       [Description("Fallback providers (comma separated, optional).")] string? fallbackProviders = null,
       CancellationToken cancellationToken = default)
       => await requestContext.WithExceptionCheck(async () =>
         await requestContext.WithStructuredContent(async () =>
       {
           if (string.IsNullOrWhiteSpace(text))
               throw new ArgumentNullException(nameof(text));

           var eden = serviceProvider.GetRequiredService<EdenAIClient>();

           // 1️⃣ Build JSON payload
           var payload = new Dictionary<string, object?>
           {
               ["providers"] = provider,
               ["text"] = text,
               ["response_as_dict"] = true,
               ["show_original_response"] = false
           };

           if (!string.IsNullOrWhiteSpace(fallbackProviders))
               payload["fallback_providers"] = fallbackProviders;

           // 2️⃣ Call Eden AI
           return await eden.PostAsync("translation/language_detection/", payload, cancellationToken)
               ?? throw new Exception("Eden AI returned no response.");
       }));
        
    [Description("Translate full documents using Eden AI translation providers.")]
    [McpServerTool(
      Title = "EdenAI Document Translation",
      Name = "edenai_translation_document_translation",
      Destructive = false)]
    public static async Task<CallToolResult?> EdenAI_DocumentTranslation(
      IServiceProvider serviceProvider,
      RequestContext<CallToolRequestParams> requestContext,
      [Description("File URL or SharePoint/OneDrive reference to translate.")] string fileUrl,
      [Description("Target language code (e.g. en, fr, de, nl).")] string targetLanguage,
      [Description("Source language code (optional, e.g. en, fr, de, nl).")] string? sourceLanguage = null,
      [Description("Primary provider (e.g. google, microsoft, amazon, openai).")] string provider = "google",
      [Description("Fallback providers (comma separated, optional).")] string? fallbackProviders = null,
      [Description("Password for PDF if applicable.")] string? filePassword = null,
      CancellationToken cancellationToken = default)
      => await requestContext.WithExceptionCheck(async () =>
      {
          if (string.IsNullOrWhiteSpace(fileUrl))
              throw new ArgumentNullException(nameof(fileUrl));
          if (string.IsNullOrWhiteSpace(targetLanguage))
              throw new ArgumentNullException(nameof(targetLanguage));

          var eden = serviceProvider.GetRequiredService<EdenAIClient>();
          var downloadService = serviceProvider.GetRequiredService<DownloadService>();

          // 1️⃣ Download source document
          var files = await downloadService.DownloadContentAsync(
              serviceProvider, requestContext.Server, fileUrl, cancellationToken);
          var file = files.FirstOrDefault() ?? throw new Exception("No file found for translation input.");

          // 2️⃣ Build multipart form
          using var form = new MultipartFormDataContent();
          var fileContent = new ByteArrayContent(file.Contents.ToArray());
          fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
          form.Add(fileContent, "file", file.Filename ?? "document.pdf");

          form.Add("providers".NamedField(provider));
          form.Add("target_language".NamedField(targetLanguage));
          form.Add("response_as_dict".NamedField("true"));
          form.Add("show_original_response".NamedField("false"));

          if (!string.IsNullOrWhiteSpace(sourceLanguage))
              form.Add("source_language".NamedField(sourceLanguage));
          if (!string.IsNullOrWhiteSpace(fallbackProviders))
              form.Add("fallback_providers".NamedField(fallbackProviders));
          if (!string.IsNullOrWhiteSpace(filePassword))
              form.Add("file_password".NamedField(filePassword));

          // 3️⃣ Send request to EdenAI
          using var req = new HttpRequestMessage(HttpMethod.Post, "translation/document_translation/")
          {
              Content = form
          };
          var resultNode = await eden.SendAsync(req, cancellationToken)
              ?? throw new Exception("Eden AI returned no response.");

          // 4️⃣ Extract translated document (base64 or URL)
          var providerKey = resultNode.AsObject().First().Key;
          var providerResult = resultNode[providerKey];
          var translatedUrl = providerResult?["document_resource_url"]?.GetValue<string>();

          // 5️⃣ Download and upload translated file to Graph
          BinaryData translatedData;

          var downloadedTranslated = await downloadService.DownloadContentAsync(
              serviceProvider, requestContext.Server, translatedUrl!, cancellationToken);
          translatedData = downloadedTranslated.FirstOrDefault()?.Contents
                           ?? throw new Exception("Failed to fetch translated file from URL.");


          var uploaded = await requestContext.Server.Upload(
              serviceProvider,
              requestContext.ToOutputFileName(".pdf"),
              translatedData,
              cancellationToken);

          // 6️⃣ Return structured + readable result
          return new CallToolResult
          {
              StructuredContent = resultNode,
              Content =
              [
                  uploaded!
              ]
          };
      });

    [Description("Translate text automatically using Eden AI translation providers.")]
    [McpServerTool(
        Title = "EdenAI Automatic Translation",
        Name = "edenai_translation_automatic_translation",
        Destructive = false)]
    public static async Task<CallToolResult?> EdenAI_AutomaticTranslation(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Text to translate.")] string text,
        [Description("Target language code (e.g. en, fr, de, nl).")] string targetLanguage,
        [Description("Source language code (optional, e.g. en, fr, de, nl).")] string? sourceLanguage = null,
        [Description("Primary provider (e.g. google, microsoft, amazon, openai).")] string provider = "google",
        [Description("Fallback providers (comma separated, optional).")] string? fallbackProviders = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentNullException(nameof(text));
            if (string.IsNullOrWhiteSpace(targetLanguage))
                throw new ArgumentNullException(nameof(targetLanguage));

            var eden = serviceProvider.GetRequiredService<EdenAIClient>();

            // 1️⃣ Build JSON payload
            var payload = new Dictionary<string, object?>
            {
                ["providers"] = provider,
                ["text"] = text,
                ["target_language"] = targetLanguage,
                ["response_as_dict"] = true,
                ["show_original_response"] = false
            };

            if (!string.IsNullOrWhiteSpace(sourceLanguage))
                payload["source_language"] = sourceLanguage;

            if (!string.IsNullOrWhiteSpace(fallbackProviders))
                payload["fallback_providers"] = fallbackProviders;

            // 2️⃣ Send request to EdenAI
            var resultNode = await eden.PostAsync("translation/automatic_translation/", payload, cancellationToken)
                ?? throw new Exception("Eden AI returned no response.");

            // 3️⃣ Extract translation result
            var providerKey = resultNode.AsObject().First().Key;
            var translation = resultNode[providerKey]?["text"]?.GetValue<string>()
                              ?? resultNode[providerKey]?["items"]?.AsArray().FirstOrDefault()?["text"]?.GetValue<string>()
                              ?? "(no translation returned)";

            // 4️⃣ Upload translation to Graph
            return await requestContext.Server.Upload(
                serviceProvider,
                requestContext.ToOutputFileName("txt"),
                BinaryData.FromString(translation),
                cancellationToken);

        }));
}