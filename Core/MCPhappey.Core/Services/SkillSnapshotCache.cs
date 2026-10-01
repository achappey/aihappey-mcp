using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MCPhappey.Common;
using MCPhappey.Common.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;

namespace MCPhappey.Core.Services;

// Shared across requests, but never across different servers, source configurations or callers.
public sealed class SkillSnapshotCache : IDisposable
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 256 * 1024 * 1024 });
    private readonly SemaphoreSlim[] gates = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public async Task<IReadOnlyList<SkillService.SkillSnapshot>> GetAsync(ServerConfig config,
        IServiceProvider services,
        Func<CancellationToken, Task<IReadOnlyList<SkillService.SkillSnapshot>>> load,
        CancellationToken ct)
    {
        var key = CacheKey(config, services);
        if (cache.TryGetValue(key, out IReadOnlyList<SkillService.SkillSnapshot>? snapshot) && snapshot != null)
            return snapshot;

        var gate = gates[(int)((uint)key.GetHashCode() % (uint)gates.Length)];
        await gate.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue(key, out snapshot) && snapshot != null)
                return snapshot;

            // Failed or cancelled loads are never cached. A waiting request can retry independently.
            snapshot = await load(ct);
            var bytes = snapshot.SelectMany(skill => skill.Files.Values)
                .DistinctBy(file => file.Uri)
                .Sum(file => (long)file.Bytes.Length);
            var size = bytes + 1024L * snapshot.Count + 1;
            if (size <= 256L * 1024 * 1024)
                cache.Set(key, snapshot, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = Lifetime,
                    Size = size
                });
            return snapshot;
        }
        finally
        {
            gate.Release();
        }
    }

    private static string CacheKey(ServerConfig config, IServiceProvider services)
    {
        var headers = services.GetService(typeof(HeaderProvider)) as HeaderProvider;
        var context = (services.GetService(typeof(IHttpContextAccessor)) as IHttpContextAccessor)?.HttpContext;
        var caller = context?.User?.Claims
            .Select(c => new { c.Type, c.Value, c.Issuer })
            .OrderBy(c => c.Type, StringComparer.Ordinal).ThenBy(c => c.Value, StringComparer.Ordinal).ToArray();
        var descriptor = JsonSerializer.Serialize(new
        {
            server = config.Server.ServerInfo.Name,
            sources = config.SkillSources?.Skills.Where(s => s.MimeType == SkillSource.MediaType)
                .Select(s => s.Uri).OrderBy(s => s, StringComparer.Ordinal).ToArray(),
            headers = headers?.Headers?.OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase).ToArray(),
            caller
        });
        // Never retain raw bearer tokens (or other header values) as cache keys.
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(descriptor)));
    }

    public void Dispose()
    {
        cache.Dispose();
        foreach (var gate in gates) gate.Dispose();
    }
}
