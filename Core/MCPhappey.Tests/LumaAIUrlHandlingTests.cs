using System.ComponentModel.DataAnnotations;
using MCPhappey.Tools.LumaAI;

namespace MCPhappey.Tests;

public sealed class LumaAIUrlHandlingTests
{
    [Theory]
    [InlineData("https://images.example.test/image.png?token=opaque")]
    [InlineData("http://images.example.test/image.png")]
    public void Input_url_is_passed_through_unchanged(string url)
    {
        Assert.Equal(url, LumaAIImages.GetLumaAccessibleUrlFromFileUrl(url));
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("/relative/image.png")]
    public void Rejects_non_http_input_urls(string url)
    {
        Assert.Throws<ValidationException>(() => LumaAIImages.GetLumaAccessibleUrlFromFileUrl(url));
    }
}
