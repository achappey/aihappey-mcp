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
        Assert.Equal(url, LumaAIVideo.GetLumaAccessibleUrlFromFileUrl(url));
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("/relative/image.png")]
    public void Rejects_non_http_input_urls(string url)
    {
        Assert.Throws<ValidationException>(() => LumaAIImages.GetLumaAccessibleUrlFromFileUrl(url));
        Assert.Throws<ValidationException>(() => LumaAIVideo.GetLumaAccessibleUrlFromFileUrl(url));
    }

    [Fact]
    public void Video_keyframe_payload_uses_input_urls_without_downloading_them()
    {
        const string firstUrl = "https://images.example.test/frame0.png?token=opaque";
        const string secondUrl = "https://images.example.test/frame1.png";
        var request = new LumaAIVideoBaseRequest
        {
            Prompt = "A moving scene",
            FileUrl = $"{firstUrl}, {secondUrl}"
        };

        var body = LumaAIVideo.BuildCreateVideoBody(request);

        Assert.Equal(firstUrl, body["keyframes"]?["frame0"]?["url"]?.GetValue<string>());
        Assert.Equal(secondUrl, body["keyframes"]?["frame1"]?["url"]?.GetValue<string>());
    }
}
