namespace MCPhappey.Auth.Cache;

static class PkceCache
{
    private static readonly Dictionary<string, string> StateToRedirect = [];
    private static readonly object Lock = new();

    public static void Store(string state, string redirectUri)
    {
        lock (Lock)
        {
            StateToRedirect[state] = redirectUri;
        }
    }

    public static string? Retrieve(string state)
    {
        lock (Lock)
        {
            if (StateToRedirect.TryGetValue(state, out var uri))
            {
                StateToRedirect.Remove(state); // one-time use
                return uri;
            }
            return null;
        }
    }
}
