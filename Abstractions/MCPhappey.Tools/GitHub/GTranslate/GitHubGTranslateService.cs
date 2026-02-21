using System.ComponentModel;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using global::GTranslate.Translators;

namespace MCPhappey.Tools.GitHub.GTranslate;

public static class GitHubGTranslateService
{
    private const string SourceUrl = "https://www.nuget.org/packages/GTranslate";
    private const string IconSource = "https://avatars.githubusercontent.com/u/9919?s=64&v=4";

    private enum TranslationProvider
    {
        Auto,
        GoogleV2,
        GoogleV1,
        Microsoft,
        Bing,
        Yandex
    }

    private static readonly TranslationProvider[] AutoFallbackOrder =
    [
        TranslationProvider.GoogleV2,
        TranslationProvider.GoogleV1,
        TranslationProvider.Microsoft,
        TranslationProvider.Bing,
        TranslationProvider.Yandex
    ];

    [Description("List all supported target languages and available providers for GTranslate tools.")]
    [McpServerTool(
        Title = "GTranslate list supported languages",
        Name = "github_gtranslate_list_languages",
        IconSource = IconSource,
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GitHubGTranslate_ListLanguages(
        [Description("Provider selector: auto|google_v2|google_v1|microsoft|bing|yandex. Defaults to auto.")]
        string provider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async ()
        => await requestContext.WithStructuredContent(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var requestedProvider = ParseProvider(provider);
            var providers = AutoFallbackOrder.Select(ToProviderString).ToArray();

            var languages = global::GTranslate.Language.LanguageDictionary
                .OrderBy(x => x.Key)
                .Select(x => new
                {
                    code = x.Key,
                    name = x.Value.Name,
                    nativeName = x.Value.NativeName,
                    providers
                })
                .ToList();

            var response = new
            {
                providerRequested = ToProviderString(requestedProvider),
                languageCount = languages.Count,
                languages
            };

            return response;
        }));

    [Description("Translate one text into a target language using a selected provider or automatic provider fallback.")]
    [McpServerTool(
        Title = "GTranslate translate text",
        Name = "github_gtranslate_translate_text",
        IconSource = IconSource,
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GitHubGTranslate_TranslateText(
        [Description("Text to translate.")] string text,
        [Description("Target language code (for example: en, nl, de, fr).")]
        string targetLanguage,
        [Description("Provider selector: auto|google_v2|google_v1|microsoft|bing|yandex. Defaults to auto.")]
        string provider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async ()
        => await requestContext.WithStructuredContent(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(text);
            var targetLanguageCode = NormalizeTargetLanguage(targetLanguage);
            var requestedProvider = ParseProvider(provider);

            var (translatedText, usedProvider, failedAttempts) =
                await TranslateWithFallbackAsync(text, targetLanguageCode, requestedProvider, cancellationToken);

            var response = new
            {
                providerRequested = ToProviderString(requestedProvider),
                providerUsed = ToProviderString(usedProvider),
                targetLanguage = targetLanguageCode,
                sourceText = text,
                translatedText,
                failedAttempts
            };

            return response;
        }));

    [Description("Translate multiple texts into a target language using a selected provider or automatic provider fallback.")]
    [McpServerTool(
        Title = "GTranslate batch translate text",
        Name = "github_gtranslate_translate_batch",
        IconSource = IconSource,
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GitHubGTranslate_TranslateBatch(
        [Description("Texts to translate.")] string[] texts,
        [Description("Target language code (for example: en, nl, de, fr).")]
        string targetLanguage,
        [Description("Provider selector: auto|google_v2|google_v1|microsoft|bing|yandex. Defaults to auto.")]
        string provider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async ()
        => await requestContext.WithStructuredContent(async () =>
        {
            ArgumentNullException.ThrowIfNull(texts);
            if (texts.Length == 0)
                throw new ArgumentException("At least one text is required.", nameof(texts));

            var cleanedTexts = texts
                .Select(t => t?.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Cast<string>()
                .ToList();

            if (cleanedTexts.Count == 0)
                throw new ArgumentException("At least one non-empty text is required.", nameof(texts));

            var targetLanguageCode = NormalizeTargetLanguage(targetLanguage);
            var requestedProvider = ParseProvider(provider);

            var results = new List<object>(cleanedTexts.Count);

            for (var i = 0; i < cleanedTexts.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sourceText = cleanedTexts[i];

                var (translatedText, usedProvider, failedAttempts) =
                    await TranslateWithFallbackAsync(sourceText, targetLanguageCode, requestedProvider, cancellationToken);

                results.Add(new
                {
                    index = i,
                    sourceText,
                    translatedText,
                    providerUsed = ToProviderString(usedProvider),
                    failedAttempts
                });
            }

            var response = new
            {
                providerRequested = ToProviderString(requestedProvider),
                targetLanguage = targetLanguageCode,
                itemCount = results.Count,
                results
            };

            return response;
        }));

    private static async Task<(string TranslatedText, TranslationProvider UsedProvider, List<object> FailedAttempts)> TranslateWithFallbackAsync(
        string text,
        string targetLanguage,
        TranslationProvider requestedProvider,
        CancellationToken cancellationToken)
    {
        var providerSequence = requestedProvider == TranslationProvider.Auto
            ? AutoFallbackOrder
            : [requestedProvider];

        var failures = new List<object>();

        foreach (var provider in providerSequence)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var translator = CreateTranslator(provider);
                var result = await translator.TranslateAsync(text, targetLanguage);
                var translated = result?.Translation;

                if (string.IsNullOrWhiteSpace(translated))
                    throw new Exception("Provider returned an empty translation.");

                return (translated, provider, failures);
            }
            catch (Exception ex)
            {
                failures.Add(new
                {
                    provider = ToProviderString(provider),
                    error = ex.Message
                });

                if (requestedProvider != TranslationProvider.Auto)
                    throw new Exception($"Provider '{ToProviderString(provider)}' failed: {ex.Message}", ex);
            }
        }

        var triedProviders = string.Join(", ", providerSequence.Select(ToProviderString));
        throw new Exception($"Translation failed for all providers in fallback chain: {triedProviders}.");
    }

    private static ITranslator CreateTranslator(TranslationProvider provider)
        => provider switch
        {
            TranslationProvider.GoogleV2 => new GoogleTranslator2(),
            TranslationProvider.GoogleV1 => new GoogleTranslator(),
            TranslationProvider.Microsoft => new MicrosoftTranslator(),
            TranslationProvider.Bing => new BingTranslator(),
            TranslationProvider.Yandex => new YandexTranslator(),
            TranslationProvider.Auto => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Auto provider is not directly translatable."),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
        };

    private static TranslationProvider ParseProvider(string? provider)
    {
        var normalized = (provider ?? "auto").Trim().ToLowerInvariant();

        return normalized switch
        {
            "auto" => TranslationProvider.Auto,
            "google_v2" or "googlev2" or "google-v2" => TranslationProvider.GoogleV2,
            "google_v1" or "googlev1" or "google-v1" => TranslationProvider.GoogleV1,
            "microsoft" => TranslationProvider.Microsoft,
            "bing" => TranslationProvider.Bing,
            "yandex" => TranslationProvider.Yandex,
            _ => throw new ArgumentException(
                "Invalid provider. Allowed values: auto|google_v2|google_v1|microsoft|bing|yandex.",
                nameof(provider))
        };
    }

    private static string NormalizeTargetLanguage(string targetLanguage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLanguage);

        var code = targetLanguage.Trim().ToLowerInvariant();
        if (!global::GTranslate.Language.LanguageDictionary.ContainsKey(code))
            throw new ArgumentException(
                $"Target language '{targetLanguage}' is not supported. Use github_gtranslate_list_languages to inspect available language codes.",
                nameof(targetLanguage));

        return code;
    }

    private static string ToProviderString(TranslationProvider provider)
        => provider switch
        {
            TranslationProvider.Auto => "auto",
            TranslationProvider.GoogleV2 => "google_v2",
            TranslationProvider.GoogleV1 => "google_v1",
            TranslationProvider.Microsoft => "microsoft",
            TranslationProvider.Bing => "bing",
            TranslationProvider.Yandex => "yandex",
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
        };
}
