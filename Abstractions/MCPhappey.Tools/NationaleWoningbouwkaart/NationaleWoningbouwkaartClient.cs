using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MCPhappey.Tools.NationaleWoningbouwkaart;

internal sealed class NationaleWoningbouwkaartClient
{
    private const string ProjectsUrl = "https://nationalewoningbouwkaart.nl/project2.json.gz";
    private const string MunicipalitiesUrl = "https://nationalewoningbouwkaart.nl/gemeente.json.gz";
    private const string WoondealsUrl = "https://nationalewoningbouwkaart.nl/woondeal.json.gz";

    private static readonly NationaleWoningbouwkaartDatasetDefinition ProjectsDefinition = new(
        DatasetKey: "projects",
        DatasetName: "projects",
        SourceUrl: ProjectsUrl);

    private static readonly NationaleWoningbouwkaartDatasetDefinition MunicipalitiesDefinition = new(
        DatasetKey: "gemeenten",
        DatasetName: "gemeenten",
        SourceUrl: MunicipalitiesUrl);

    private static readonly NationaleWoningbouwkaartDatasetDefinition WoondealsDefinition = new(
        DatasetKey: "woondeals",
        DatasetName: "woondeals",
        SourceUrl: WoondealsUrl);

    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(12);
    private static readonly SemaphoreSlim CacheLock = new(1, 1);
    private static readonly Dictionary<string, NationaleWoningbouwkaartDataset> CachedDatasets = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;

    public NationaleWoningbouwkaartClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public Task<NationaleWoningbouwkaartDataset> GetProjectDatasetAsync(CancellationToken cancellationToken = default)
        => GetDatasetAsync(ProjectsDefinition, MapProject, cancellationToken);

    public Task<NationaleWoningbouwkaartDataset> GetMunicipalityDatasetAsync(CancellationToken cancellationToken = default)
        => GetDatasetAsync(MunicipalitiesDefinition, MapMunicipality, cancellationToken);

    public Task<NationaleWoningbouwkaartDataset> GetWoondealDatasetAsync(CancellationToken cancellationToken = default)
        => GetDatasetAsync(WoondealsDefinition, MapWoondeal, cancellationToken);

    private async Task<NationaleWoningbouwkaartDataset> GetDatasetAsync(
        NationaleWoningbouwkaartDatasetDefinition definition,
        Func<JsonObject, NationaleWoningbouwkaartItem?> mapper,
        CancellationToken cancellationToken)
    {
        if (TryGetValidCache(definition.DatasetKey, out var cached))
            return cached;

        await CacheLock.WaitAsync(cancellationToken);

        try
        {
            if (TryGetValidCache(definition.DatasetKey, out cached))
                return cached;

            var fresh = await DownloadDatasetAsync(definition, mapper, cancellationToken);
            CachedDatasets[definition.DatasetKey] = fresh;
            return fresh;
        }
        catch when (TryGetAnyCache(definition.DatasetKey, out cached))
        {
            return cached;
        }
        finally
        {
            CacheLock.Release();
        }
    }

    private async Task<NationaleWoningbouwkaartDataset> DownloadDatasetAsync(
        NationaleWoningbouwkaartDatasetDefinition definition,
        Func<JsonObject, NationaleWoningbouwkaartItem?> mapper,
        CancellationToken cancellationToken)
    {
        var httpClient = _httpClientFactory.CreateClient();

        using var response = await httpClient.GetAsync(definition.SourceUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payloadBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        using var rawStream = new MemoryStream(payloadBytes, writable: false);
        using Stream contentStream = IsGzip(payloadBytes)
            ? new GZipStream(rawStream, CompressionMode.Decompress)
            : rawStream;

        var featureCollection = await JsonSerializer.DeserializeAsync<NationaleWoningbouwkaartFeatureCollection>(contentStream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException($"NationaleWoningbouwkaart {definition.DatasetKey} dataset could not be deserialized.");

        var items = (featureCollection.Features ?? [])
            .Select(feature => feature.Properties)
            .Where(static properties => properties is not null)
            .Select(properties => mapper(properties!))
            .Where(static item => item is not null)
            .Cast<NationaleWoningbouwkaartItem>()
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var cachedAtUtc = DateTimeOffset.UtcNow;

        return new NationaleWoningbouwkaartDataset(
            DatasetKey: definition.DatasetKey,
            DatasetName: definition.DatasetName,
            Items: items,
            CachedAtUtc: cachedAtUtc,
            ExpiresAtUtc: cachedAtUtc.Add(CacheDuration),
            SourceUrl: definition.SourceUrl);
    }

    private static NationaleWoningbouwkaartItem? MapProject(JsonObject properties)
    {
        var projectId = ReadInt(properties, "project_id");
        var name = FirstNotEmpty(ReadString(properties, "naam"), ReadString(properties, "Plannaam"), projectId is int id ? $"Project {id}" : null);
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var planName = ReadString(properties, "Plannaam");
        var municipality = ReadString(properties, "gemeente_naam");
        var province = ReadString(properties, "provincie_naam");
        var status = ReadString(properties, "Planstatus");
        var peilmoment = ReadString(properties, "peilmoment");

        var description = BuildDescription(
            CombineLocation(municipality, province),
            FormatLabel("Status", status),
            FormatLabel("Peilmoment", peilmoment),
            !string.Equals(name, planName, StringComparison.OrdinalIgnoreCase) ? FormatLabel("Plannaam", planName) : null);

        var searchJson = new JsonObject
        {
            ["name"] = name,
            ["description"] = description
        };

        SetIfHasValue(searchJson, "project_id", projectId);
        SetIfNotBlank(searchJson, "municipality", municipality);
        SetIfNotBlank(searchJson, "province", province);
        SetIfNotBlank(searchJson, "status", status);

        return new NationaleWoningbouwkaartItem(
            Name: name,
            Description: description,
            SearchTextNormalized: NormalizeSearchText(name, planName, municipality, province, status, peilmoment),
            NameNormalized: Normalize(name),
            ListJsonTemplate: CreateListJson(name, description),
            SearchJsonTemplate: searchJson,
            AlternativeNameNormalized: Normalize(planName),
            MunicipalityNormalized: Normalize(municipality),
            ProvinceNormalized: Normalize(province),
            StatusNormalized: Normalize(status),
            PeilmomentNormalized: Normalize(peilmoment));
    }

    private static NationaleWoningbouwkaartItem? MapMunicipality(JsonObject properties)
    {
        var name = ReadString(properties, "gemeente_naam");
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var municipalityCode = ReadString(properties, "gemeente_code");
        var province = ReadString(properties, "provincie_naam");
        var region = ReadString(properties, "woondeal_regio");
        var peilmoment = ReadString(properties, "peilmoment");
        var netto = ReadDouble(properties, "Netto");
        var hardTotal = ReadDouble(properties, "Hard totaal");
        var zachtTotal = ReadDouble(properties, "Zacht totaal");

        var description = BuildDescription(
            FormatLabel("Provincie", province),
            FormatLabel("Woondealregio", region),
            FormatLabel("Peilmoment", peilmoment),
            FormatNumberLabel("Netto", netto),
            FormatNumberLabel("Hard totaal", hardTotal),
            FormatNumberLabel("Zacht totaal", zachtTotal));

        var searchJson = new JsonObject
        {
            ["name"] = name,
            ["description"] = description
        };

        SetIfNotBlank(searchJson, "gemeente_code", municipalityCode);
        SetIfNotBlank(searchJson, "province", province);
        SetIfNotBlank(searchJson, "region", region);
        SetIfNotBlank(searchJson, "peilmoment", peilmoment);
        SetIfHasValue(searchJson, "netto", netto);
        SetIfHasValue(searchJson, "hard_totaal", hardTotal);
        SetIfHasValue(searchJson, "zacht_totaal", zachtTotal);

        return new NationaleWoningbouwkaartItem(
            Name: name,
            Description: description,
            SearchTextNormalized: NormalizeSearchText(name, province, municipalityCode, region, peilmoment),
            NameNormalized: Normalize(name),
            ListJsonTemplate: CreateListJson(name, description),
            SearchJsonTemplate: searchJson,
            ProvinceNormalized: Normalize(province),
            PeilmomentNormalized: Normalize(peilmoment),
            CodeNormalized: Normalize(municipalityCode),
            RegionNormalized: Normalize(region));
    }

    private static NationaleWoningbouwkaartItem? MapWoondeal(JsonObject properties)
    {
        var name = ReadString(properties, "woondeal_regio");
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var peilmoment = ReadString(properties, "peilmoment");
        var netto = ReadDouble(properties, "Netto");
        var hardTotal = ReadDouble(properties, "Hard totaal");
        var zachtTotal = ReadDouble(properties, "Zacht totaal");

        var description = BuildDescription(
            FormatLabel("Peilmoment", peilmoment),
            FormatNumberLabel("Netto", netto),
            FormatNumberLabel("Hard totaal", hardTotal),
            FormatNumberLabel("Zacht totaal", zachtTotal));

        var searchJson = new JsonObject
        {
            ["name"] = name,
            ["description"] = description
        };

        SetIfNotBlank(searchJson, "peilmoment", peilmoment);
        SetIfHasValue(searchJson, "netto", netto);
        SetIfHasValue(searchJson, "hard_totaal", hardTotal);
        SetIfHasValue(searchJson, "zacht_totaal", zachtTotal);

        return new NationaleWoningbouwkaartItem(
            Name: name,
            Description: description,
            SearchTextNormalized: NormalizeSearchText(name, peilmoment),
            NameNormalized: Normalize(name),
            ListJsonTemplate: CreateListJson(name, description),
            SearchJsonTemplate: searchJson,
            PeilmomentNormalized: Normalize(peilmoment),
            RegionNormalized: Normalize(name));
    }

    private static JsonObject CreateListJson(string name, string description) => new()
    {
        ["name"] = name,
        ["description"] = description
    };

    private static string BuildDescription(params string?[] parts)
    {
        var filtered = parts.Where(static part => !string.IsNullOrWhiteSpace(part)).ToArray();
        return filtered.Length == 0
            ? "Samenvatting uit de Nationale Woningbouwkaart dataset."
            : string.Join(". ", filtered) + ".";
    }

    private static string? CombineLocation(string? left, string? right)
    {
        var parts = new[] { left, right }.Where(static value => !string.IsNullOrWhiteSpace(value)).ToArray();
        return parts.Length == 0 ? null : string.Join(", ", parts);
    }

    private static string? FormatLabel(string label, string? value)
        => string.IsNullOrWhiteSpace(value) ? null : $"{label}: {value}";

    private static string? FormatNumberLabel(string label, double? value)
        => value is null ? null : $"{label}: {FormatNumber(value.Value)}";

    private static string NormalizeSearchText(params string?[] values)
        => Normalize(string.Join(' ', values.Where(static value => !string.IsNullOrWhiteSpace(value))));

    private static string FirstNotEmpty(params string?[] values)
        => values.Select(Clean).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string? ReadString(JsonObject properties, string key)
    {
        if (!properties.TryGetPropertyValue(key, out var node) || node is null)
            return null;

        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var stringValue))
                return Clean(stringValue);

            if (value.TryGetValue<int>(out var intValue))
                return intValue.ToString(CultureInfo.InvariantCulture);

            if (value.TryGetValue<long>(out var longValue))
                return longValue.ToString(CultureInfo.InvariantCulture);

            if (value.TryGetValue<double>(out var doubleValue))
                return FormatNumber(doubleValue);
        }

        return Clean(node.ToJsonString());
    }

    private static int? ReadInt(JsonObject properties, string key)
    {
        if (!properties.TryGetPropertyValue(key, out var node) || node is not JsonValue value)
            return null;

        if (value.TryGetValue<int>(out var intValue))
            return intValue;

        if (value.TryGetValue<long>(out var longValue) && longValue is >= int.MinValue and <= int.MaxValue)
            return (int)longValue;

        if (value.TryGetValue<double>(out var doubleValue))
            return (int)doubleValue;

        if (value.TryGetValue<string>(out var stringValue) && int.TryParse(stringValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            return parsed;

        return null;
    }

    private static double? ReadDouble(JsonObject properties, string key)
    {
        if (!properties.TryGetPropertyValue(key, out var node) || node is not JsonValue value)
            return null;

        if (value.TryGetValue<double>(out var doubleValue))
            return doubleValue;

        if (value.TryGetValue<int>(out var intValue))
            return intValue;

        if (value.TryGetValue<long>(out var longValue))
            return longValue;

        if (value.TryGetValue<decimal>(out var decimalValue))
            return (double)decimalValue;

        if (value.TryGetValue<string>(out var stringValue) && double.TryParse(stringValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            return parsed;

        return null;
    }

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void SetIfNotBlank(JsonObject obj, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            obj[key] = value;
    }

    private static void SetIfHasValue(JsonObject obj, string key, int? value)
    {
        if (value is not null)
            obj[key] = value.Value;
    }

    private static void SetIfHasValue(JsonObject obj, string key, double? value)
    {
        if (value is not null)
            obj[key] = value.Value;
    }

    private static string FormatNumber(double value)
        => Math.Abs(value % 1) < 0.000001d
            ? value.ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToLowerInvariant(character));
        }

        var withoutDiacritics = builder.ToString().Normalize(NormalizationForm.FormC);
        return string.Join(' ', withoutDiacritics.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static bool IsGzip(IReadOnlyList<byte> payload)
        => payload.Count >= 2 && payload[0] == 0x1F && payload[1] == 0x8B;

    private static bool TryGetValidCache(string datasetKey, [NotNullWhen(true)] out NationaleWoningbouwkaartDataset? dataset)
    {
        if (CachedDatasets.TryGetValue(datasetKey, out var cached) && cached.ExpiresAtUtc > DateTimeOffset.UtcNow)
        {
            dataset = cached;
            return true;
        }

        dataset = null;
        return false;
    }

    private static bool TryGetAnyCache(string datasetKey, [NotNullWhen(true)] out NationaleWoningbouwkaartDataset? dataset)
        => CachedDatasets.TryGetValue(datasetKey, out dataset);
}
