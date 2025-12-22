namespace MCPhappey.Auth.Models;

public class OAuthSettings
{
    public string TenantId { get; set; } = default!;
    public string ClientId { get; set; } = default!;
    public string ClientSecret { get; set; } = default!;
    public string Audience { get; set; } = default!;
    public string Scopes { get; set; } = default!;
    public IDictionary<string, ConfidentialClientSettings>? ConfidentialClients { get; set; }
}

public class ConfidentialClientSettings
{
    public List<string> ClientSecrets { get; set; } = [];
    public List<string> RedirectUris { get; set; } = [];
}