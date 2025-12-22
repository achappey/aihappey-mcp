
using MCPhappey.Auth.Cache;

namespace MCPhappey.Auth.Controllers;

public static class CallbackController
{
    public static async Task<IResult> Handle(HttpContext ctx)
    {
        var req = ctx.Request;
        var code = req.Query["code"].ToString();
        var state = req.Query["state"].ToString();

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
        {
            return Results.BadRequest("Missing 'code' or 'state'");
        }

        var redirectUri = PkceCache.Retrieve(state);
        if (redirectUri == null)
        {
            return Results.BadRequest("Invalid or expired state.");
        }

        CodeCache.Store(code, redirectUri!);

        var fullRedirect = $"{redirectUri}?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state)}";
        return await Task.FromResult(Results.Redirect(fullRedirect));
    }
}
