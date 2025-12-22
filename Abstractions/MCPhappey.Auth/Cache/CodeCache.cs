
namespace MCPhappey.Auth.Cache;

public static class CodeCache
{
    private static readonly Dictionary<string, string> _map = new();

    public static void Store(string code, string redirectUri)
    {
        _map[code] = redirectUri;
    }

    public static string? Retrieve(string code)
    {
        return _map.TryGetValue(code, out var uri) ? uri : null;
    }
}
