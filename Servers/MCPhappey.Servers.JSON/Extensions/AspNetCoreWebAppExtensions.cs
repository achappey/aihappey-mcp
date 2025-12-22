
using MCPhappey.Common.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace MCPhappey.Servers.JSON.Extensions;

public static class AspNetCoreWebAppExtensions
{
    public static WebApplication UseWidgets(
            this WebApplication webApp,
            string basePath)
    {
        var webComponents = Directory.GetFiles(Path.Combine(basePath, "web-components"));

        foreach (var webComponent in webComponents)
        {
            var filename = Path.GetFileName(webComponent);
            webApp.MapGet($"/{filename}", async (HttpContext context) =>
                {
                  //  var file = Path.Combine(basePath, filename);
                    var text = await File.ReadAllTextAsync(webComponent);
                    return Results.Text(text, "text/javascript; charset=utf-8");
                });
        }

        var otherAssets = Directory.GetFiles(basePath);

        foreach (var otherAsset in otherAssets)
        {
            var filename = Path.GetFileName(otherAsset);
            webApp.MapGet($"/{filename}", async (HttpContext context) =>
                {
                  //  var file = Path.Combine(basePath, filename);
                    var text = await File.ReadAllTextAsync(otherAsset);
                    var extension = Path.GetExtension(otherAsset);
                    var mimeType = extension.EndsWith(".js")
                        ? "text/javascript; charset=utf-8"
                        : extension.EndsWith(".css")
                        ? "text/css; charset=utf-8" : null;

                    return Results.Text(text, mimeType);
                });
        }

        return webApp;
    }
}