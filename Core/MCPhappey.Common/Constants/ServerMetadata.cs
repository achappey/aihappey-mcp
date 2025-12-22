namespace MCPhappey.Common.Constants;

public static class ServerMetadata
{
    public const string McpUrl = "McpUrl";
    public const string McpSource = "McpSource";
    public const string Owners = "Owners";
    public const string Groups = "Groups";
    public const string Plugins = "Plugins";

    public enum McpSources
    {
        Config,
        Database
    }
}