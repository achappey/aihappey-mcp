using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.PDFData;

public static class PDFDataDocuments
{
    [Description("Upload a PDF document to PDFData using fileUrl input, select document type, optionally request specific extraction fields, and return structured extraction output.")]
    [McpServerTool(Title = "PDFData upload document", Name = "pdfdata_upload_document", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> PDFData_Upload_Document(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("PDF file URL to upload. Supports SharePoint, OneDrive, and HTTP(S) sources.")] string fileUrl,
        [Description("Document type such as receipt, invoice, or resume.")] string doc_type,
        [Description("Optional comma-separated list of fields to extract.")] string? fields = null,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                if (string.IsNullOrWhiteSpace(fileUrl))
                    throw new ValidationException("fileUrl is required.");

                if (string.IsNullOrWhiteSpace(doc_type))
                    throw new ValidationException("doc_type is required.");

                var client = serviceProvider.GetRequiredService<PDFDataClient>();
                var downloadService = serviceProvider.GetRequiredService<DownloadService>();

                var files = await downloadService.DownloadContentAsync(
                    serviceProvider,
                    requestContext.Server,
                    fileUrl,
                    cancellationToken);

                var file = files.FirstOrDefault()
                    ?? throw new ValidationException("fileUrl could not be downloaded.");

                using var form = new MultipartFormDataContent();
                var fileContent = new ByteArrayContent(file.Contents.ToArray());
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.MimeType ?? "application/pdf");

                form.Add(fileContent, "pdf", file.Filename ?? "document.pdf");
                form.Add(new StringContent(doc_type.Trim()), "doc_type");

                if (!string.IsNullOrWhiteSpace(fields))
                    form.Add(new StringContent(fields.Trim()), "fields");

                var response = await client.PostMultipartAsync("v1/documents", form, cancellationToken)
                    ?? throw new Exception("PDFData returned no response.");

                return new JsonObject
                {
                    ["provider"] = "pdfdata",
                    ["type"] = "document_upload",
                    ["fileUrl"] = fileUrl,
                    ["doc_type"] = doc_type,
                    ["fields"] = string.IsNullOrWhiteSpace(fields) ? null : fields.Trim(),
                    ["document"] = response,
                    ["raw"] = response
                };
            }));
}
