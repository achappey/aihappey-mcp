using System.Text.Json;

namespace MCPhappey.Auth.Controllers;

public static class RegisterController
{
    public static async Task<IResult> Handle(HttpContext ctx)
    {
        if (!ctx.Request.HasJsonContentType())
            return Results.BadRequest(new { error = "Expected application/json" });

        var registration = await JsonSerializer.DeserializeAsync<JsonElement>(ctx.Request.Body);

        if (!registration.TryGetProperty("client_name", out var clientNameProp) ||
            !registration.TryGetProperty("redirect_uris", out var redirectUrisProp) ||
            !registration.TryGetProperty("grant_types", out var grantTypesProp) ||
            !registration.TryGetProperty("response_types", out var responseTypesProp))
        {
            return Results.BadRequest(new { error = "Missing required fields for dynamic client registration" });
        }

        var clientName = clientNameProp.GetString() ?? "unknown";

        var redirectUris = redirectUrisProp
            .EnumerateArray()
            .Select(x => x.GetString())
            .Where(x => x is not null)
            .ToList()!;

        var grantTypes = grantTypesProp
            .EnumerateArray()
            .Select(x => x.GetString())
            .Where(x => x is not null)
            .ToList()!;

        var responseTypes = responseTypesProp
            .EnumerateArray()
            .Select(x => x.GetString())
            .Where(x => x is not null)
            .ToList()!;

        var clientUri = registration.TryGetProperty("client_uri", out var clientUriProp)
            ? clientUriProp.GetString()
            : null;

        if (redirectUris.Count == 0)
            return Results.BadRequest(new { error = "redirect_uris cannot be empty" });

        var clientId = Guid.NewGuid().ToString("N");

        // Optionally: store the registration
        // ClientStore.Register(clientId, redirectUris[0]);

        var response = new
        {
            client_id = clientId,
            client_name = clientName,
            redirect_uris = redirectUris,
            grant_types = grantTypes,
            response_types = responseTypes,
            token_endpoint_auth_method = "none",
            client_uri = clientUri
        };

        return Results.Json(response);
    }
}
