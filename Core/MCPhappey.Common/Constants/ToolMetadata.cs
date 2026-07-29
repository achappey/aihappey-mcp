namespace MCPhappey.Common.Constants;

public static class ToolMetadata
{
    public static Dictionary<string, object>? GetMCPAppUI(string resourceUri, IEnumerable<string>? visibility = null)
    {
        return new Dictionary<string, object>
        {
           {"ui", new
           {
            resourceUri,
            visibility
            }}
        };
    }
}