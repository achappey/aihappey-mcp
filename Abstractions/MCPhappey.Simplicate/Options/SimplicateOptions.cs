namespace MCPhappey.Simplicate.Options;

//
// Summary:
//     Configuration settings for the Simplicate integration.
//
public class SimplicateOptions
{
    //
    // Summary:
    //     Uri of the Azure Key Vault that contains Simplicate client-id / secret
    //     secrets where the secret name equals the user’s oid claim.
    //
    // Example:
    //     KeyVaultUri = "https://my-kv.vault.azure.net/"
    //
    public required string KeyVaultUri { get; init; }

    public string Organization { get; init; } = null!;
}
