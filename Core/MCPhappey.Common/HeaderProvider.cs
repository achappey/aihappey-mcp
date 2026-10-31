
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Common;

public class HeaderProvider
{
    private Dictionary<string, string>? headers;

    public Dictionary<string, string>? Headers
    {
        get => headers;
        set => headers = value is null ? null : new(value, StringComparer.OrdinalIgnoreCase);
    }

    public string? Authorization =>
        Headers?.TryGetValue(HeaderNames.Authorization, out var value) == true ? value : null;

    public string? Bearer =>
        Authorization?.Split(" ").LastOrDefault();

}
