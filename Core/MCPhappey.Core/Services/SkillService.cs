using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using YamlDotNet.Serialization;

namespace MCPhappey.Core.Services;

public sealed class SkillService(SkillSourceResolver resolver, SkillSnapshotCache snapshots)
{
    private const int TtlMs = 300_000;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public sealed record SkillFile(string Uri, byte[] Bytes, string MimeType);
    public sealed record SkillSnapshot(JsonObject Entry, IReadOnlyDictionary<string, SkillFile> Files);

    public static bool Enabled(ServerConfig config) => config.SkillSources?.Skills.Any(s => s.MimeType == SkillSource.MediaType) == true;

    public async Task<IReadOnlyList<SkillSnapshot>> ResolveAsync(IServiceProvider services, McpServer server,
        CancellationToken ct)
    {
        var config = services.GetServerConfig(server) ?? throw new InvalidOperationException("Unknown server.");
        return await snapshots.GetAsync(config, services,
            token => ResolveUncachedAsync(services, config, server, token), ct);
    }

    private Task<IReadOnlyList<SkillSnapshot>> ResolveAsync(IServiceProvider services, ServerConfig config,
        CancellationToken ct)
        => snapshots.GetAsync(config, services,
            token => ResolveUncachedAsync(services, config, null, token), ct);

    private async Task<IReadOnlyList<SkillSnapshot>> ResolveUncachedAsync(IServiceProvider services, ServerConfig config,
        McpServer? server, CancellationToken ct)
    {
        var result = new Dictionary<string, SkillSnapshot>(StringComparer.Ordinal);
        foreach (var source in config.SkillSources?.Skills.Where(s => s.MimeType == SkillSource.MediaType) ?? [])
        {
            var files = await resolver.ResolveAsync(services, server, config, source.Uri, ct);
            if (!files.ContainsKey("SKILL.md") || files.Count > 512 || files.Values.Sum(b => (long)b.Length) > 16 * 1024 * 1024)
                throw new InvalidOperationException("Skill folder must contain SKILL.md and fit within the supported limits.");
            var rootHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source.Uri)))[..16];
            var roots = files.Keys.Where(p => p == "SKILL.md" || p.EndsWith("/SKILL.md", StringComparison.Ordinal))
                .OrderBy(p => p.Length).ToArray();
            foreach (var rootFile in roots)
            {
                var prefix = rootFile[..^"SKILL.md".Length];
                var frontmatter = ParseFrontmatter(files[rootFile]);
                var name = frontmatter["name"]?.GetValue<string>()!;
                // Keep name as the final segment and use the stable source URL hash as an organizational prefix.
                var rootUri = $"skill://source-{rootHash}/{(prefix.Length == 0 ? "" : prefix)}{name}";
                if (prefix.Length != 0 && prefix.TrimEnd('/').Split('/')[^1] != name)
                    throw new InvalidOperationException("Nested SKILL.md name must match its parent directory.");
                var skillFiles = new Dictionary<string, SkillFile>(StringComparer.Ordinal);
                var manifest = new JsonArray();
                foreach (var (path, bytes) in files.Where(f => f.Key.StartsWith(prefix, StringComparison.Ordinal))
                    .OrderBy(f => f.Key == rootFile ? "" : f.Key, StringComparer.Ordinal))
                {
                    if (!SkillSourceResolver.ValidPath(path)) throw new InvalidOperationException("Invalid skill file path.");
                    var relative = path[prefix.Length..];
                    var fileUri = rootUri + "/" + relative;
                    var mime = Mime(relative);
                    skillFiles.Add(fileUri, new SkillFile(fileUri, bytes, mime));
                    manifest.Add(new JsonObject
                    {
                        ["uri"] = fileUri,
                        ["digest"] = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes)),
                        ["size"] = bytes.Length
                    });
                }
                var entryUri = rootUri + "/SKILL.md";
                var entry = new JsonObject { ["uri"] = entryUri, ["frontmatter"] = frontmatter, ["resources"] = manifest };
                if (!result.TryAdd(entryUri, new SkillSnapshot(entry, skillFiles)))
                    throw new InvalidOperationException("Two skill sources map to the same public URI.");
            }
        }
        return result.Values.OrderBy(s => s.Entry["uri"]!.GetValue<string>(), StringComparer.Ordinal).ToArray();
    }

    public async Task<JsonObject> ListAsync(IServiceProvider services, McpServer server, string? cursor, CancellationToken ct)
    {
        var skills = await ResolveAsync(services, server, ct);
        var offset = 0;
        if (cursor != null && (!int.TryParse(cursor, out offset) || offset < 0 || offset > skills.Count))
            throw new McpProtocolException("Invalid skills cursor.", McpErrorCode.InvalidParams);
        var result = new JsonObject
        {
            ["resultType"] = "complete",
            ["skills"] = new JsonArray(skills.Skip(offset).Take(50)
                .Select(s => (JsonNode?)s.Entry.DeepClone()).ToArray()),
            ["ttlMs"] = TtlMs,
            ["cacheScope"] = "private"
        };
        if (offset + 50 < skills.Count) result["nextCursor"] = (offset + 50).ToString();
        return result;
    }

    public Task<JsonObject> ListAsync(IServiceProvider services, ServerConfig config, string? cursor, CancellationToken ct)
        => ListCoreAsync(services, config, cursor, ct);

    private async Task<JsonObject> ListCoreAsync(IServiceProvider services, ServerConfig config, string? cursor, CancellationToken ct)
    {
        var skills = await ResolveAsync(services, config, ct);
        var offset = 0;
        if (cursor != null && (!int.TryParse(cursor, out offset) || offset < 0 || offset > skills.Count))
            throw new McpProtocolException("Invalid skills cursor.", McpErrorCode.InvalidParams);
        var result = new JsonObject
        {
            ["resultType"] = "complete",
            ["skills"] = new JsonArray(skills.Skip(offset).Take(50).Select(s => (JsonNode?)s.Entry.DeepClone()).ToArray()),
            ["ttlMs"] = TtlMs,
            ["cacheScope"] = "private"
        };
        if (offset + 50 < skills.Count) result["nextCursor"] = (offset + 50).ToString();
        return result;
    }

    public async Task<JsonObject> GetAsync(IServiceProvider services, McpServer server, string? uri, CancellationToken ct)
    {
        if (!ValidUri(uri)) throw new McpProtocolException("Invalid skill URI.", McpErrorCode.InvalidParams);
        var skill = (await ResolveAsync(services, server, ct)).FirstOrDefault(s => s.Entry["uri"]!.GetValue<string>() == uri);
        if (skill == null) throw new McpProtocolException("Unknown skill URI.", McpErrorCode.InvalidParams);
        return new JsonObject
        {
            ["resultType"] = "complete",
            ["skill"] = skill.Entry.DeepClone(),
            ["ttlMs"] = TtlMs,
            ["cacheScope"] = "private"
        };
    }

    public async Task<JsonObject> GetAsync(IServiceProvider services, ServerConfig config, string? uri, CancellationToken ct)
    {
        if (!ValidUri(uri)) throw new McpProtocolException("Invalid skill URI.", McpErrorCode.InvalidParams);
        var skill = (await ResolveAsync(services, config, ct)).FirstOrDefault(s => s.Entry["uri"]!.GetValue<string>() == uri);
        if (skill == null) throw new McpProtocolException("Unknown skill URI.", McpErrorCode.InvalidParams);
        return new JsonObject { ["resultType"] = "complete", ["skill"] = skill.Entry.DeepClone(),
            ["ttlMs"] = TtlMs, ["cacheScope"] = "private" };
    }

    public async Task<ReadResourceResult> ReadAsync(IServiceProvider services, McpServer server, string uri, CancellationToken ct)
    {
        if (!ValidUri(uri)) throw new McpProtocolException("Invalid skill resource URI.", McpErrorCode.InvalidParams);
        var file = (await ResolveAsync(services, server, ct)).SelectMany(s => s.Files.Values)
            .FirstOrDefault(f => f.Uri == uri);
        if (file == null) throw new McpProtocolException("Unknown skill file.", McpErrorCode.InvalidParams);
        ResourceContents content;
        try
        {
            // Text is sent only when losslessly round-trippable, preserving the manifest's raw bytes.
            var text = Utf8.GetString(file.Bytes);
            content = new TextResourceContents { Uri = uri, MimeType = file.MimeType, Text = text };
        }
        catch (DecoderFallbackException)
        {
            content = new BlobResourceContents
            {
                Uri = uri,
                MimeType = file.MimeType,
                Blob = file.Bytes
            };
        }
        return new ReadResourceResult { Contents = [content], TimeToLive = TimeSpan.FromMilliseconds(TtlMs), CacheScope = CacheScope.Private };
    }

    private static bool ValidUri(string? uri) => uri != null && uri.StartsWith("skill://source-", StringComparison.Ordinal) &&
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Query.Length == 0 && parsed.Fragment.Length == 0 &&
        parsed.AbsolutePath.Split('/').Skip(1).All(s => s.Length > 0 && s is not "." and not "..");

    private static string Mime(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".md" => "text/markdown",
        ".txt" => "text/plain",
        ".json" => "application/json",
        ".yaml" or ".yml" => "application/yaml",
        ".py" => "text/x-python",
        ".js" => "text/javascript",
        ".html" => "text/html",
        ".css" => "text/css",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".pdf" => "application/pdf",
        _ => "application/octet-stream"
    };

    internal static JsonObject ParseFrontmatter(byte[] bytes)
    {
        var text = Utf8.GetString(bytes);
        if (!text.StartsWith("---\n", StringComparison.Ordinal) && !text.StartsWith("---\r\n", StringComparison.Ordinal))
            throw new InvalidOperationException("SKILL.md must start with YAML frontmatter.");
        var lines = text.Split('\n');
        var end = Array.FindIndex(lines, 1, l => l.TrimEnd('\r') == "---");
        if (end < 2) throw new InvalidOperationException("Missing SKILL.md frontmatter delimiter.");
        var yaml = string.Join('\n', lines[1..end]);
        var deserializer = new DeserializerBuilder().Build();
        var raw = deserializer.Deserialize<object>(yaml);
        var node = JsonSerializer.SerializeToNode(Normalize(raw)) as JsonObject
            ?? throw new InvalidOperationException("SKILL.md frontmatter must be a YAML object.");
        var name = node["name"]?.GetValue<string>();
        var description = node["description"]?.GetValue<string>();
        if (name == null || name.Length is < 1 or > 64 || name != name.ToLowerInvariant() ||
            name[0] is < 'a' or > 'z' && name[0] is < '0' or > '9' ||
            name[^1] == '-' || name.Contains("--") ||
            name.Any(c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')) ||
            string.IsNullOrWhiteSpace(description) || description.Length > 1024)
            throw new InvalidOperationException("Invalid Agent Skill name or description.");
        return node;
    }

    private static object? Normalize(object? value) => value switch
    {
        IDictionary<object, object> map => map.ToDictionary(k => k.Key.ToString()!, v => Normalize(v.Value)),
        System.Collections.IList list => list.Cast<object>().Select(Normalize).ToArray(),
        _ => value
    };
}
