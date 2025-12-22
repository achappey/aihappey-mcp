using System.Collections.Concurrent;
using Microsoft.IdentityModel.Tokens;

namespace MCPhappey.Auth.Cache;

public static class JwksCache
{
    private class CacheEntry(IReadOnlyList<JsonWebKey> keys, DateTimeOffset expiresAt)
    {
        public IReadOnlyList<JsonWebKey> Keys { get; } = keys;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;

        public bool IsValid() => DateTimeOffset.UtcNow < ExpiresAt;
    }

    private static readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private static readonly TimeSpan DefaultCacheDuration = TimeSpan.FromHours(12);

    public static async Task<IReadOnlyList<JsonWebKey>?> GetAsync(
        string url,
        IHttpClientFactory httpClientFactory,
        TimeSpan? cacheDuration = null)
    {
        // 1) Fast path if cache entry is valid
        if (_cache.TryGetValue(url, out var entry) && entry.IsValid())
        {
            return entry.Keys;
        }

        // 2) Acquire lock to prevent multiple downloads for same url
        var semaphore = _locks.GetOrAdd(url, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync();

        try
        {
            // Double-check cache after waiting
            if (_cache.TryGetValue(url, out entry) && entry.IsValid())
            {
                return entry.Keys;
            }

            using var client = httpClientFactory.CreateClient();

            HttpResponseMessage response;
            try
            {
                response = await client.GetAsync(url);
            }
            catch
            {
                // If failed to fetch and we do have a valid (older) entry, use that
                if (_cache.TryGetValue(url, out entry) && entry.IsValid())
                {
                    return entry.Keys;
                }
                return null;
            }

            // If not success, fallback to old entry
            if (!response.IsSuccessStatusCode)
            {
                if (_cache.TryGetValue(url, out entry) && entry.IsValid())
                {
                    return entry.Keys;
                }
                return null;
            }

            var jwks = await response.Content.ReadFromJsonAsync<JsonWebKeySet>();
            if (jwks?.Keys == null || !jwks.Keys.Any())
            {
                if (_cache.TryGetValue(url, out entry) && entry.IsValid())
                {
                    return entry.Keys;
                }
                return null;
            }

            // Determine cache duration from response if possible
            var effectiveCacheDuration = cacheDuration ?? DefaultCacheDuration;
            if (response.Headers.CacheControl?.MaxAge != null)
            {
                effectiveCacheDuration = response.Headers.CacheControl.MaxAge.Value;
            }

            // Store new entry
            var newEntry = new CacheEntry([.. jwks.Keys], DateTimeOffset.UtcNow.Add(effectiveCacheDuration));
            _cache[url] = newEntry;
            return newEntry.Keys;
        }
        finally
        {
            semaphore.Release();
        }
    }

    public static void Invalidate(string url)
    {
        _cache.TryRemove(url, out _);
    }

    public static void Clear()
    {
        _cache.Clear();
    }
}
