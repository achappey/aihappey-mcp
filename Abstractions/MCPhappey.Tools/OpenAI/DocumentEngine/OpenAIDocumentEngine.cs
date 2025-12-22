using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Common.Constants;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Extensions;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.OpenAI.DocumentEngine;

public static class OpenAIDocumentEngine
{
    [Description("Compose a new document directly from inline JSON input and a selected HTML template, without using storage. Ideal for quick generation, testing, or ephemeral use inside AI-native apps.")]
    [McpServerTool(
        Title = "Compose new document (inline)",
        Name = "openai_document_engine_compose_inline",
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> OpenAIDocumentEngine_ComposeInline(
    RequestContext<CallToolRequestParams> requestContext,
    [Description("URL of the HTML output template file to bind the data. Must be registered as a resource on this server.")] string documentTemplateUrl,
    [Description("Raw JSON content representing the structured data to inject into the template.")] string structuredContent)
    {
        // No AI reasoning, just binding data with template (similar to Bind, but without external file fetch)
        return new CallToolResult()
        {
            StructuredContent = JsonNode.Parse(structuredContent),
            Meta = await requestContext.GetToolMeta(new Dictionary<string, object>()
            {
                { ToolMetadata.OpenAI_OutputTemplate, documentTemplateUrl }
            })
        };
    }


    [Description("Convert any input document (CV, proposal, report, etc.) into structured JSON based on an AI-extracted schema from the selected HTML template. The tool first analyzes the template to infer its data structure, then parses and normalizes the source document into that structure. Returns both structured content and raw model output for rendering or further use.")]
    [McpServerTool(
         Title = "Compose new document",
         Name = "openai_document_engine_compose",
         ReadOnly = true,
         OpenWorld = false)]
    public static async Task<CallToolResult?> OpenAIDocumentEngine_Compose(
         RequestContext<CallToolRequestParams> requestContext,
         IServiceProvider serviceProvider,
         [Description("Url of the html outputTemplate to bind the data. Make sure the file is also added as resource to this server, otherwise the app won't render on the client")] string documentTemplateUrl,
         [Description("Url of the input file that should be used as source document")] string inputFileUrl,
         [Description("Prompt to guide/steer AI on generating the output file content")] string prompt,
         [Description("Url of the JSON schema file defining the expected data structure in the html template file. Optional, but strongly recommended to improve performance and reduce AI processing costs.")] string? jsonSchemaUrl = null,
         CancellationToken cancellationToken = default) =>
         await requestContext.WithExceptionCheck(async () =>
         await requestContext.WithOboGraphClient(async (client) =>
    {
        var toolMeta = await requestContext.GetToolMeta();
        var resourceService = serviceProvider.GetRequiredService<ResourceService>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var samplingService = serviceProvider.GetRequiredService<SamplingService>();

        // 🧠 STEP 1 — Get or create schema using the shared helper
        string jsonSchema;
        if (string.IsNullOrEmpty(jsonSchemaUrl))
        {
            jsonSchema = await ExtractSchemaInternal(requestContext, serviceProvider, documentTemplateUrl, cancellationToken);
        }
        else
        {
            var jsonSchemaFiles = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, jsonSchemaUrl, cancellationToken);
            jsonSchema = jsonSchemaFiles.FirstOrDefault()?.Contents.ToString() ?? "{}";
        }

        var fileInput = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server, inputFileUrl, cancellationToken);
        var file = fileInput.FirstOrDefault();

        var firstRequest = new CreateMessageRequestParams()
        {
            ModelPreferences = "gpt-5.1".ToModelPreferences(),
            MaxTokens = 4096 * 4,
            Metadata = JsonSerializer.SerializeToElement(new Dictionary<string, object>() { { "openai",
                            new {
                                reasoning = new {
                                    effort = "low"
                                }
                            }  } }),
            Messages = [file?.Contents.ToString()!.ToUserSamplingMessage()!,
                prompt.ToUserSamplingMessage()]
        };

        var firstSample = await requestContext.Server.SampleAsync(firstRequest, cancellationToken);

        var finalArgs = new Dictionary<string, JsonElement>()
        {
            {"inputText", JsonSerializer.SerializeToElement( new
            {
                inputText = file?.Contents.ToString()!,
                firstReasoning = firstSample.Content
            }) },
            {"jsonStructure", JsonSerializer.SerializeToElement(jsonSchema) },
            };

        if (!string.IsNullOrEmpty(prompt))
        {
            finalArgs.Add("userHint", JsonSerializer.SerializeToElement(prompt));
        }

        var finalSampling = await samplingService.GetPromptSample(serviceProvider,
            requestContext.Server, "convert-to-structure", finalArgs,
            "gpt-5.1",
            maxTokens: 4096 * 8,
            metadata: new Dictionary<string, object>() { { "openai", new {
                                reasoning = new {
                                    effort = "low"
                                }
                            }  } },
            cancellationToken: cancellationToken);

        var jsonString = finalSampling.ToText()?.CleanJson();

        var result = await client.Upload("document_data".ToOutputFileName() + ".json",
                  BinaryData.FromString(jsonString!), cancellationToken: cancellationToken);

        return new CallToolResult()
        {
            Content = [.. finalSampling.Content, result!],
            StructuredContent = JsonNode.Parse(jsonString ?? string.Empty),
            Meta = await requestContext.GetToolMeta(new Dictionary<string, object>()
            {
                { ToolMetadata.OpenAI_OutputTemplate, documentTemplateUrl}
            })
        };
    }));

    [Description("Bind an existing JSON data file to a specific HTML output template for rendering. This tool does not perform any AI reasoning — it simply links structured data with the selected UI template so the document can be viewed or edited in the app.")]
    [McpServerTool(
      Title = "Bind existing document data",
      Name = "openai_document_engine_bind",
      ReadOnly = true,
      OpenWorld = false)]
    public static async Task<CallToolResult?> OpenAIDocumentEngine_Bind(
      RequestContext<CallToolRequestParams> requestContext,
      IServiceProvider serviceProvider,
      [Description("URL of the HTML output template to use for rendering the document. The file must also be registered as a resource on this server, otherwise it will not render on the client.")] string documentTemplateUrl,
      [Description("URL of the JSON data file containing the structured document content to bind to the template. This file is typically created by the compose tool or stored on OneDrive/SharePoint.")] string jsonDataFileUrl,
      CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var fileInput = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server, jsonDataFileUrl, cancellationToken);
        var file = fileInput.FirstOrDefault();

        return new()
        {
            StructuredContent = JsonNode.Parse(file?.Contents.ToString() ?? string.Empty),
            Meta = await requestContext.GetToolMeta(new Dictionary<string, object>()
            {
                { ToolMetadata.OpenAI_OutputTemplate, documentTemplateUrl }
            })
        };
    });

    [Description("Apply a JSON Patch (RFC 6902) to an existing stored JSON document. The tool retrieves the document from its URL, applies the patch operations, and saves the updated version back to the same location. This allows partial updates to structured document data without re-uploading the full JSON.")]
    [McpServerTool(
         Title = "Patch existing document data",
         Name = "openai_document_engine_patch",
         ReadOnly = false,
         OpenWorld = false)]
    public static async Task<CallToolResult?> OpenAIDocumentEngine_Patch(
         RequestContext<CallToolRequestParams> requestContext,
         IServiceProvider serviceProvider,
         [Description("URL of the stored JSON document to patch (for example, a OneDrive or SharePoint file).")] string documentUrl,
         [Description("JSON Patch array containing one or more RFC 6902 operations (e.g., replace, add, remove).")] string patch,
         CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async (client) =>
        await requestContext.WithStructuredContent(async () =>
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var uploadService = serviceProvider.GetRequiredService<UploadService>();

        // 1️⃣ Download existing JSON file
        var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, documentUrl, cancellationToken);
        var file = files.FirstOrDefault() ?? throw new FileNotFoundException(documentUrl);
        var json = JsonSerializer.Deserialize<JsonNode>(file.Contents.ToString()) ?? new JsonObject();
        var settings = new Newtonsoft.Json.JsonSerializerSettings
        {
            ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver()
        };

        var patchDoc = Newtonsoft.Json.JsonConvert.DeserializeObject<JsonPatchDocument>(patch, settings)
                      ?? throw new InvalidOperationException("Invalid JSON Patch format.");

        var jObj = Newtonsoft.Json.Linq.JObject.Parse(json.ToJsonString());
        patchDoc.ApplyTo(jObj);
        var updatedJson = jObj.ToString(Newtonsoft.Json.Formatting.None);

        var fileName = Path.GetFileName(new Uri(documentUrl).AbsolutePath);
        var result = await client.UploadBinaryDataAsync(documentUrl,
                        BinaryData.FromString(updatedJson), cancellationToken: cancellationToken);

        return JsonNode.Parse(updatedJson);
    })));

    [Description("Validate a JSON document against its JSON Schema definition. This tool checks whether the document structure, field types, and required properties comply with the provided schema and returns validation results. It does not modify the document.")]
    [McpServerTool(
         Title = "Validate document JSON",
         Name = "openai_document_engine_validate",
         ReadOnly = true,
         OpenWorld = false)]
    public static async Task<CallToolResult?> OpenAIDocumentEngine_Validate(
         RequestContext<CallToolRequestParams> requestContext,
         IServiceProvider serviceProvider,
         [Description("URL of the JSON document to validate. Typically points to a stored file on OneDrive or SharePoint.")] string documentUrl,
         [Description("URL of the JSON Schema file describing the expected structure of the document.")] string jsonSchemaUrl,
         CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        // 1️⃣ Download both document and schema
        var docFiles = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, documentUrl, cancellationToken);
        var schemaFiles = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, jsonSchemaUrl, cancellationToken);

        var doc = docFiles.FirstOrDefault()?.Contents.ToString() ?? throw new FileNotFoundException(documentUrl);
        var schema = schemaFiles.FirstOrDefault()?.Contents.ToString() ?? throw new FileNotFoundException(jsonSchemaUrl);

        // 2️⃣ Parse both into JsonNode
        var jsonNode = JsonNode.Parse(doc);
        var schemaNode = JsonNode.Parse(schema);

        var validator = await NJsonSchema.JsonSchema.FromJsonAsync(schema, cancellationToken);
        // Serialize the System.Text.Json.Nodes.JsonNode back into a JSON string
        var errors = validator.Validate(jsonNode?.ToJsonString() ?? "{}");

        // 4️⃣ Prepare result
        var validationResult = new JsonObject
        {
            ["isValid"] = errors.Count == 0,
            ["errorCount"] = errors.Count,
            ["errors"] = new JsonArray(errors.Select(e => (JsonNode)new JsonObject
            {
                ["path"] = e.Path,
                ["message"] = e.ToString()
            }).ToArray())
        };

        return validationResult;
    }));

    [Description("Create and upload a JSON schema from a document template.")]
    [McpServerTool(
      Title = "Create and save document schema",
      Name = "openai_document_engine_create_schema",
      ReadOnly = false,
      OpenWorld = false)]
    public static async Task<CallToolResult?> OpenAIDocumentEngine_CreateSchema(
      RequestContext<CallToolRequestParams> requestContext,
      IServiceProvider serviceProvider,
      [Description("Url of the document template file to create the JSON schema for")] string documentTemplateUrl,
      CancellationToken cancellationToken = default) =>
      await requestContext.WithExceptionCheck(async () =>
      await requestContext.WithOboGraphClient(async (client) =>
  {
      var schemaJson = await ExtractSchemaInternal(
          requestContext, serviceProvider, documentTemplateUrl, cancellationToken);

      var result = await client.Upload("schema".ToOutputFileName() + ".json",
          BinaryData.FromString(schemaJson), cancellationToken: cancellationToken);

      return result?.ToCallToolResult();
  }));


    [Description("Extracts a JSON schema from a document template.")]
    [McpServerTool(
       Title = "Extract document schema",
       Name = "openai_document_engine_extract_schema",
       ReadOnly = true,
       OpenWorld = false)]
    public static async Task<CallToolResult?> OpenAIDocumentEngine_ExtractSchema(
       RequestContext<CallToolRequestParams> requestContext,
       IServiceProvider serviceProvider,
       [Description("Url of the document template file to create the JSON schema for")] string documentTemplateUrl,
       CancellationToken cancellationToken = default) =>
       await requestContext.WithExceptionCheck(async () =>
       await requestContext.WithStructuredContent(async () =>
   {
       var schemaJson = await ExtractSchemaInternal(
           requestContext, serviceProvider, documentTemplateUrl, cancellationToken);

       return JsonNode.Parse(schemaJson);
   }));


    /// <summary>
    /// Internal helper to extract a JSON schema from a document template using the same sampling logic.
    /// Used by both CreateSchema (saves to storage) and ExtractSchema (returns inline).
    /// </summary>
    private static async Task<string> ExtractSchemaInternal(
        RequestContext<CallToolRequestParams> requestContext,
        IServiceProvider serviceProvider,
        string documentTemplateUrl,
        CancellationToken cancellationToken)
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var samplingService = serviceProvider.GetRequiredService<SamplingService>();

        var fileInput = await downloadService.DownloadContentAsync(
            serviceProvider, requestContext.Server, documentTemplateUrl, cancellationToken);

        var file = fileInput.FirstOrDefault() ?? throw new FileNotFoundException(documentTemplateUrl);

        var reportArgs = new Dictionary<string, JsonElement>
        {
            {"templateHtmlJs", file.Contents.ToString()!.ToJsonElement()},
        };

        var reportSampling = await samplingService.GetPromptSample(
            serviceProvider,
            requestContext.Server,
            "extract-template-structure",
            reportArgs,
            "gpt-5-mini",
            maxTokens: 4096 * 4,
            metadata: new Dictionary<string, object>()
            {
                {
                    "openai",
                    new
                    {
                        reasoning = new { effort = "high" }
                    }
                }
            },
            cancellationToken: cancellationToken);

        return reportSampling.ToText()?.CleanJson() ?? "{}";
    }

}

