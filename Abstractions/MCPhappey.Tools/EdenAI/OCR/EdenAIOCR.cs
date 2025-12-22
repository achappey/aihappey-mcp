using System.ComponentModel;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MCPhappey.Core.Services;
using System.Net.Http.Headers;

namespace MCPhappey.Tools.EdenAI.OCR;

public static class EdenAIOCR
{

    [Description("Extract structured resume or CV data using Eden AI Resume Parser.")]
    [McpServerTool(
      Name = "edenai_resume_parser",
      Title = "Eden AI Resume Parser"
  )]
    public static async Task<CallToolResult?> EdenAI_ResumeParser(
      IServiceProvider serviceProvider,
      RequestContext<CallToolRequestParams> requestContext,
      [Description("File URL or SharePoint/OneDrive reference")] string fileUrl,
      [Description("Primary provider, e.g. 'affinda' or 'openai'")] string provider = "affinda",
      [Description("Fallback providers (comma separated)")] string? fallbackProviders = null,
      [Description("Document language code, e.g. 'en' or 'nl'")] string? language = null,
      [Description("Convert to PDF before parsing (true/false)")] bool convertToPdf = false,
      CancellationToken cancellationToken = default)
      => await requestContext.WithExceptionCheck(async () =>
          await requestContext.WithStructuredContent(async () =>
      {
          // 1️⃣ Dependencies
          var eden = serviceProvider.GetRequiredService<EdenAIClient>();
          var downloadService = serviceProvider.GetRequiredService<DownloadService>();

          // 2️⃣ Download file
          var files = await downloadService.DownloadContentAsync(
              serviceProvider, requestContext.Server, fileUrl, cancellationToken);
          var file = files.FirstOrDefault()
                     ?? throw new Exception("No file found for Resume Parser input.");

          // 3️⃣ Build multipart/form-data
          using var form = new MultipartFormDataContent();
          var fileContent = new ByteArrayContent(file.Contents.ToArray());
          fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
          form.Add(fileContent, "file", file.Filename!);

          form.Add(new StringContent(provider), "providers");
          form.Add(new StringContent("true"), "response_as_dict");
          form.Add(new StringContent("false"), "show_original_response");
          form.Add(new StringContent(convertToPdf ? "true" : "false"), "convert_to_pdf");

          if (!string.IsNullOrWhiteSpace(language))
              form.Add(new StringContent(language), "language");
          if (!string.IsNullOrWhiteSpace(fallbackProviders))
              form.Add(new StringContent(fallbackProviders), "fallback_providers");

          // 4️⃣ Direct call (auth handled in EdenAIClient)
          using var req = new HttpRequestMessage(HttpMethod.Post, "ocr/resume_parser/")
          {
              Content = form
          };

          // 5️⃣ Send and return structured result
          return await eden.SendAsync(req, cancellationToken);
      }));

    [Description("Extract identity document data (passport, ID, etc.) using Eden AI Identity Parser.")]
    [McpServerTool(
       Name = "edenai_identity_parser",
       Title = "Eden AI Identity Parser"
   )]
    public static async Task<CallToolResult?> EdenAI_IdentityParser(
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       [Description("File URL or SharePoint/OneDrive reference")] string fileUrl,
       [Description("Primary provider, e.g. 'microsoft' or 'openai'")] string provider = "microsoft",
       [Description("Fallback providers (comma separated)")] string? fallbackProviders = null,
       [Description("Document language code, e.g. 'en' or 'nl'")] string? language = null,
       [Description("Convert to PDF before parsing (true/false)")] bool convertToPdf = false,
       CancellationToken cancellationToken = default)
       => await requestContext.WithExceptionCheck(async () =>
           await requestContext.WithStructuredContent(async () =>
       {
           // 1️⃣ Dependencies
           var eden = serviceProvider.GetRequiredService<EdenAIClient>();
           var downloadService = serviceProvider.GetRequiredService<DownloadService>();

           // 2️⃣ Download file
           var files = await downloadService.DownloadContentAsync(
               serviceProvider, requestContext.Server, fileUrl, cancellationToken);
           var file = files.FirstOrDefault()
                      ?? throw new Exception("No file found for Identity Parser input.");

           // 3️⃣ Build multipart/form-data
           using var form = new MultipartFormDataContent();
           var fileContent = new ByteArrayContent(file.Contents.ToArray());
           fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
           form.Add(fileContent, "file", file.Filename!);

           form.Add(new StringContent(provider), "providers");
           form.Add(new StringContent("true"), "response_as_dict");
           form.Add(new StringContent("false"), "show_original_response");
           form.Add(new StringContent(convertToPdf ? "true" : "false"), "convert_to_pdf");

           if (!string.IsNullOrWhiteSpace(language))
               form.Add(new StringContent(language), "language");
           if (!string.IsNullOrWhiteSpace(fallbackProviders))
               form.Add(new StringContent(fallbackProviders), "fallback_providers");

           // 4️⃣ Build HTTP request (auth handled inside EdenAIClient)
           using var req = new HttpRequestMessage(HttpMethod.Post, "ocr/identity_parser/")
           {
               Content = form
           };

           // 5️⃣ Send request through unified client
           return await eden.SendAsync(req, cancellationToken);
       }));

    [Description("Extract structured financial data (invoice or receipt) using Eden AI Financial Parser.")]
    [McpServerTool(
       Name = "edenai_financial_parser",
       Title = "Eden AI Financial Parser"
   )]
    public static async Task<CallToolResult?> EdenAI_FinancialParser(
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       [Description("File URL or SharePoint/OneDrive reference")] string fileUrl,
       [Description("Primary provider, e.g. 'google' or 'microsoft'")] string provider = "google",
       [Description("Fallback providers (comma separated)")] string? fallbackProviders = null,
       [Description("Document language code, e.g. 'en' or 'nl'")] string? language = null,
       [Description("Document type: auto-detect, invoice, or receipt")] string documentType = "invoice",
       [Description("Convert to PDF before parsing (true/false)")] bool convertToPdf = false,
       CancellationToken cancellationToken = default)
       => await requestContext.WithExceptionCheck(async () =>
           await requestContext.WithStructuredContent(async () =>
       {
           var eden = serviceProvider.GetRequiredService<EdenAIClient>();
           var downloadService = serviceProvider.GetRequiredService<DownloadService>();

           // 2️⃣ Download file
           var files = await downloadService.DownloadContentAsync(
               serviceProvider, requestContext.Server, fileUrl, cancellationToken);
           var file = files.FirstOrDefault()
                      ?? throw new Exception("No file found for Financial Parser input.");

           // 3️⃣ Build multipart/form-data
           using var form = new MultipartFormDataContent();
           var fileContent = new ByteArrayContent(file.Contents.ToArray());
           fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
           form.Add(fileContent, "file", file.Filename!);

           form.Add(new StringContent(provider), "providers");
           form.Add(new StringContent("true"), "response_as_dict");
           form.Add(new StringContent("false"), "show_original_response");
           form.Add(new StringContent(documentType), "document_type");
           form.Add(new StringContent(convertToPdf ? "true" : "false"), "convert_to_pdf");

           if (!string.IsNullOrWhiteSpace(language))
               form.Add(new StringContent(language), "language");
           if (!string.IsNullOrWhiteSpace(fallbackProviders))
               form.Add(new StringContent(fallbackProviders), "fallback_providers");

           // 4️⃣ Direct call (multipart path bypasses PostAsync)
           using var req = new HttpRequestMessage(HttpMethod.Post, "ocr/financial_parser/")
           {
               Content = form
           };

           // 5️⃣ Send via EdenAIClient extension (so we keep logging etc)
           return await eden.SendAsync(req, cancellationToken);
       }));

    [Description("Extract text from an image or PDF using Eden AI OCR.")]
    [McpServerTool(
            Name = "edenai_ocr",
            Title = "Eden AI OCR"
        )]
    public static async Task<CallToolResult?> EdenAI_OCR(
            IServiceProvider serviceProvider,
            RequestContext<CallToolRequestParams> requestContext,
            [Description("File URL or SharePoint/OneDrive reference")] string fileUrl,
            [Description("Primary OCR provider, e.g. 'microsoft' or 'google'")] string provider = "microsoft",
            [Description("Fallback providers (comma separated)")] string? fallbackProviders = null,
            [Description("Document language code, e.g. 'en' or 'nl'")] string? language = null,
            CancellationToken cancellationToken = default)
             => await requestContext.WithExceptionCheck(async () =>
               await requestContext.WithStructuredContent(async () =>
        {
            // 1️⃣ Dependencies
            var edenSettings = serviceProvider.GetRequiredService<EdenAISettings>();
            var eden = serviceProvider.GetRequiredService<EdenAIClient>();
            var downloadService = serviceProvider.GetRequiredService<DownloadService>();

            // 2️⃣ Download and prepare the file (handles SharePoint/OneDrive/HTTP)
            var files = await downloadService.DownloadContentAsync(
                serviceProvider, requestContext.Server, fileUrl, cancellationToken);
            var file = files.FirstOrDefault()
                       ?? throw new Exception("No file found for OCR input.");

            // 3️⃣ Build multipart/form-data
            using var form = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(file.Contents.ToArray());
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(fileContent, "file", file.Filename!);

            form.Add(new StringContent(provider), "providers");
            form.Add(new StringContent("true"), "response_as_dict");
            form.Add(new StringContent("false"), "show_original_response");
            if (!string.IsNullOrWhiteSpace(language))
                form.Add(new StringContent(language), "language");
            if (!string.IsNullOrWhiteSpace(fallbackProviders))
                form.Add(new StringContent(fallbackProviders), "fallback_providers");

            // 4️⃣ Direct HTTP call (multipart path bypasses PostAsync)
            using var req = new HttpRequestMessage(HttpMethod.Post, "ocr/ocr/")
            {
                Content = form
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", edenSettings.ApiKey);

            return await eden.SendAsync(req, cancellationToken);
        }));
}