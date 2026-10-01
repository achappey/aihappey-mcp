using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using MCPhappey.Common;
using MCPhappey.Common.Models;
using ModelContextProtocol.Server;

namespace MCPhappey.Core.Services;

public sealed class SkillSourceResolver(DownloadService downloader, IEnumerable<IContentScraper> scrapers,
    IHttpClientFactory clients)
{
    private const int MaxBytes = 16 * 1024 * 1024;
    private const int MaxFiles = 512;

    public async Task<IReadOnlyDictionary<string, byte[]>> ResolveAsync(IServiceProvider services, McpServer server,
        ServerConfig config, string source, CancellationToken ct)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Skill source must be an HTTPS URL.");

        var directory = scrapers.OfType<IContentDirectoryScraper>().FirstOrDefault(d => d.SupportsDirectory(config, source));
        if (directory != null)
            return await directory.GetDirectoryAsync(server, services, source, ct);

        if (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) && uri.AbsolutePath.Contains("/tree/", StringComparison.Ordinal))
            return await ResolveGitHubAsync(source, uri, ct);

        var downloaded = (await downloader.DownloadContentAsync(services, server, source, ct)).ToList();
        if (downloaded.Count != 1 || downloaded[0].Contents.Length > MaxBytes * 4L)
            throw new InvalidOperationException("Skill source must resolve to one bounded ZIP file.");
        return ReadZip(downloaded[0].Contents.ToArray());
    }

    private async Task<IReadOnlyDictionary<string, byte[]>> ResolveGitHubAsync(string source, Uri uri, CancellationToken ct)
    {
        var segments = uri.AbsolutePath.Trim('/').Split('/');
        if (segments.Length < 5 || segments[2] != "tree" || segments.Any(s => s.Length == 0 || s is "." or ".."))
            throw new InvalidOperationException("Invalid GitHub skill folder URL.");
        var owner = segments[0];
        var repo = segments[1];
        // GitHub refs may contain slashes. Try the longest ref first, accepting only a directory response.
        using var client = clients.CreateClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MCPhappey", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        for (var refEnd = segments.Length - 2; refEnd >= 3; refEnd--)
        {
            var reference = string.Join('/', segments[3..(refEnd + 1)]);
            var path = string.Join('/', segments[(refEnd + 1)..]);
            if (string.IsNullOrEmpty(path)) continue;
            var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var api = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/contents/";
            async Task<bool> Visit(string remote, string relative)
            {
                var address = api + string.Join('/', remote.Split('/').Select(Uri.EscapeDataString)) + "?ref=" + Uri.EscapeDataString(reference);
                using var response = await client.GetAsync(address, ct);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
                response.EnsureSuccessStatusCode();
                using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
                if (document.RootElement.ValueKind != JsonValueKind.Array) return false;
                foreach (var item in document.RootElement.EnumerateArray())
                {
                    var name = item.GetProperty("name").GetString()!;
                    if (!ValidPath(name) || name.Contains('/')) throw new InvalidOperationException("Invalid GitHub skill path.");
                    var filePath = relative + name;
                    var type = item.GetProperty("type").GetString();
                    if (type == "dir")
                        await Visit(remote + "/" + name, filePath + "/");
                    else if (type == "file")
                    {
                        if (result.Count >= MaxFiles || item.GetProperty("size").GetInt64() > MaxBytes)
                            throw new InvalidOperationException("Skill exceeds the supported limits.");
                        var raw = $"https://raw.githubusercontent.com/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/{Uri.EscapeDataString(reference)}/" +
                            string.Join('/', (remote + "/" + name).Split('/').Select(Uri.EscapeDataString));
                        using var fileResponse = await client.GetAsync(raw, ct);
                        fileResponse.EnsureSuccessStatusCode();
                        var bytes = await fileResponse.Content.ReadAsByteArrayAsync(ct);
                        if (bytes.Length > MaxBytes || result.Values.Sum(b => (long)b.Length) + bytes.Length > MaxBytes || !result.TryAdd(filePath, bytes))
                            throw new InvalidOperationException("Invalid or oversized GitHub skill folder.");
                    }
                    else throw new InvalidOperationException("Unsupported GitHub skill entry.");
                }
                return true;
            }
            if (await Visit(path, "") && result.ContainsKey("SKILL.md")) return result;
        }
        throw new InvalidOperationException($"GitHub source is not a skill folder: {source}");
    }

    internal static IReadOnlyDictionary<string, byte[]> ReadZip(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entries = archive.Entries.Where(e => !e.FullName.EndsWith('/')).ToArray();
        if (entries.Length > MaxFiles || entries.Any(e => !ValidPath(e.FullName) || e.Length > MaxBytes ||
            // Reject Unix symlinks and other non-regular entries.
            (e.ExternalAttributes >> 16 & 0xF000) is 0xA000 or 0x4000))
            throw new InvalidOperationException("Invalid or oversized skill ZIP.");
        var roots = entries.Where(e => e.Name == "SKILL.md").Select(e => e.FullName[..^"SKILL.md".Length]).ToArray();
        var prefix = roots.FirstOrDefault(r => r.Length == 0) ?? (roots.Length == 1 ? roots[0] : null);
        if (prefix == null || entries.Any(e => !e.FullName.StartsWith(prefix, StringComparison.Ordinal)))
            throw new InvalidOperationException("ZIP must contain a single skill root with SKILL.md.");
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long total = 0;
        foreach (var entry in entries)
        {
            var path = entry.FullName[prefix.Length..];
            total += entry.Length;
            if (total > MaxBytes || !ValidPath(path) || !result.TryAdd(path, []))
                throw new InvalidOperationException("Invalid, duplicate, or oversized skill ZIP entry.");
            using var input = entry.Open();
            using var buffer = new MemoryStream();
            input.CopyTo(buffer);
            if (buffer.Length != entry.Length) throw new InvalidOperationException("Invalid ZIP entry length.");
            result[path] = buffer.ToArray();
        }
        return result;
    }

    internal static bool ValidPath(string path) => path.Length > 0 && !path.StartsWith('/') &&
        !path.Contains('\\') && !path.Contains(':') && !path.Contains('%') &&
        path.Split('/').All(segment => segment.Length > 0 && segment is not "." and not "..");
}
