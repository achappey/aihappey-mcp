using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.DeepL;

public static class DeepLDocuments
{
    [Description("Translate a document with DeepL using a file URL, wait for completion, upload translated output, and return a resource link.")]
    [McpServerTool(
        Title = "DeepL Document Translation",
        Name = "deepl_translate_document",
        OpenWorld = true,
        ReadOnly = false,
        Destructive = false)]
    public static async Task<CallToolResult?> DeepL_TranslateDocument(
        [Description("File URL to translate. Protected SharePoint and OneDrive links are supported.")] string fileUrl,
        [Description("Target language.")]
        DeepLTargetLanguage targetLang,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional source language.")]
        DeepLSourceLanguage? sourceLang = null,
        [Description("Optional output format extension.")]
        DeepLOutputFormat? outputFormat = null,
        [Description("Optional formality preference.")]
        DeepLFormality? formality = null,
        [Description("Optional DeepL glossary ID.")]
        string? glossaryId = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
    {
        if (string.IsNullOrWhiteSpace(fileUrl))
            throw new ArgumentNullException(nameof(fileUrl));

        var (typed, _, _) = await requestContext.Server.TryElicit(new DeepLTranslateDocumentRequest
        {
            TargetLang = targetLang,
            SourceLang = sourceLang,
            OutputFormat = outputFormat,
            Formality = formality,
            GlossaryId = glossaryId
        }, cancellationToken);

        if (string.IsNullOrWhiteSpace(fileUrl))
            throw new ArgumentNullException(nameof(fileUrl));

        var deepL = serviceProvider.GetRequiredService<DeepLClient>();
        var downloader = serviceProvider.GetRequiredService<DownloadService>();

        var inputs = await downloader.DownloadContentAsync(
            serviceProvider,
            requestContext.Server,
            fileUrl,
            cancellationToken);

        var input = inputs.FirstOrDefault() ?? throw new Exception("No file found for fileUrl.");
        var inputFilename = string.IsNullOrWhiteSpace(input.Filename) ? "document.bin" : input.Filename!;

        var targetLangValue = typed.TargetLang.GetEnumMemberValue() ?? typed.TargetLang.ToString().ToUpperInvariant();
        var sourceLangValue = typed.SourceLang?.GetEnumMemberValue();
        var outputFormatValue = typed.OutputFormat?.GetEnumMemberValue();
        var formalityValue = typed.Formality?.GetEnumMemberValue();

        var upload = await deepL.UploadDocumentAsync(
            input.Contents,
            inputFilename,
            targetLangValue,
            sourceLangValue,
            outputFormatValue,
            formalityValue,
            typed.GlossaryId,
            cancellationToken) ?? throw new Exception("DeepL returned an empty upload response.");

        var documentId = upload["document_id"]?.ToString();
        var documentKey = upload["document_key"]?.ToString();

        if (string.IsNullOrWhiteSpace(documentId) || string.IsNullOrWhiteSpace(documentKey))
            throw new Exception("DeepL upload response did not include document_id or document_key.");

        int? total = null;
        int pollCounter = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var statusDoc = await deepL.GetDocumentStatusAsync(documentId, documentKey, cancellationToken)
                ?? throw new Exception("DeepL returned an empty status response.");

            var status = statusDoc["status"]?.ToString()?.ToLowerInvariant();
            var secondsRemaining = statusDoc["seconds_remaining"]?.GetValue<int?>();

            if (secondsRemaining.HasValue)
            {
                total ??= Math.Max(1, secondsRemaining.Value);
                var progress = Math.Max(1, total.Value - secondsRemaining.Value);

                await requestContext.Server.SendProgressNotificationAsync(
                    requestContext,
                    progress,
                    $"DeepL status: {status}",
                    total,
                    cancellationToken);
            }

            if (status == "done")
                break;

            if (status == "error")
            {
                var errorMessage = statusDoc["error_message"]?.ToString() ?? "Unknown DeepL document translation error.";
                throw new Exception(errorMessage);
            }

            pollCounter++;
            var delaySeconds = secondsRemaining.HasValue
                ? Math.Clamp(Math.Max(1, secondsRemaining.Value / 4), 1, 10)
                : 2;

            await requestContext.Server.SendMessageNotificationAsync(
                $"DeepL document translation in progress ({status ?? "unknown"}, poll #{pollCounter})",
                LoggingLevel.Info,
                cancellationToken);

            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
        }

        var translated = await deepL.DownloadTranslatedDocumentAsync(documentId, documentKey, cancellationToken);

        var extension = !string.IsNullOrWhiteSpace(outputFormatValue)
            ? outputFormatValue!.Trim().TrimStart('.')
            : Path.GetExtension(inputFilename).TrimStart('.');
        if (string.IsNullOrWhiteSpace(extension))
            extension = "bin";

        var uploaded = await requestContext.Server.Upload(
            serviceProvider,
            requestContext.ToOutputFileName(extension),
            translated,
            cancellationToken);

        if (uploaded == null)
            throw new Exception("Failed to upload translated DeepL document.");

        return uploaded.ToResourceLinkCallToolResponse();
    });

    [Description("Confirm DeepL document translation settings before uploading and translating the file.")]
    public class DeepLTranslateDocumentRequest
    {
        [Required]
        [JsonPropertyName("targetLang")]
        [Description("Target language for translation.")]
        public DeepLTargetLanguage TargetLang { get; set; }

        [JsonPropertyName("sourceLang")]
        [Description("Optional source language. Leave empty to auto-detect.")]
        public DeepLSourceLanguage? SourceLang { get; set; }

        [JsonPropertyName("outputFormat")]
        [Description("Optional output document format.")]
        public DeepLOutputFormat? OutputFormat { get; set; }

        [JsonPropertyName("formality")]
        [Description("Optional formality preference for supported languages.")]
        public DeepLFormality? Formality { get; set; }

        [JsonPropertyName("glossaryId")]
        [Description("Optional DeepL glossary ID.")]
        public string? GlossaryId { get; set; }
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DeepLSourceLanguage
    {
        AR,
        BG,
        CS,
        DA,
        DE,
        EL,
        EN,
        ES,
        ET,
        FI,
        FR,
        HE,
        HU,
        ID,
        IT,
        JA,
        KO,
        LT,
        LV,
        NB,
        NL,
        PL,
        PT,
        RO,
        RU,
        SK,
        SL,
        SV,
        TH,
        TR,
        UK,
        VI,
        ZH
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DeepLTargetLanguage
    {
        AR,
        BG,
        CS,
        DA,
        DE,
        EL,
        [EnumMember(Value = "EN-GB")]
        ENGB,
        [EnumMember(Value = "EN-US")]
        ENUS,
        ES,
        [EnumMember(Value = "ES-419")]
        ES419,
        ET,
        FI,
        FR,
        HE,
        HU,
        ID,
        IT,
        JA,
        KO,
        LT,
        LV,
        NB,
        NL,
        PL,
        [EnumMember(Value = "PT-BR")]
        PTBR,
        [EnumMember(Value = "PT-PT")]
        PTPT,
        RO,
        RU,
        SK,
        SL,
        SV,
        TH,
        TR,
        UK,
        VI,
        ZH,
        [EnumMember(Value = "ZH-HANS")]
        ZHHANS,
        [EnumMember(Value = "ZH-HANT")]
        ZHHANT
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DeepLOutputFormat
    {
        [EnumMember(Value = "docx")]
        Docx,
        [EnumMember(Value = "pptx")]
        Pptx,
        [EnumMember(Value = "xlsx")]
        Xlsx,
        [EnumMember(Value = "pdf")]
        Pdf,
        [EnumMember(Value = "html")]
        Html,
        [EnumMember(Value = "txt")]
        Txt,
        [EnumMember(Value = "xlf")]
        Xlf,
        [EnumMember(Value = "srt")]
        Srt,
        [EnumMember(Value = "jpg")]
        Jpg,
        [EnumMember(Value = "png")]
        Png
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DeepLFormality
    {
        [EnumMember(Value = "default")]
        Default,
        [EnumMember(Value = "more")]
        More,
        [EnumMember(Value = "less")]
        Less,
        [EnumMember(Value = "prefer_more")]
        PreferMore,
        [EnumMember(Value = "prefer_less")]
        PreferLess
    }
}

