using System.Net.Http.Headers;
using System.Text;

namespace MCPhappey.Tools.StabilityAI;

public static class StabilityAIExtensions
{

    public static StringContent NamedField(this string name, string value)
    {
        var c = new StringContent(value ?? string.Empty, Encoding.UTF8);
        c.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            // quoting avoids odd parsers; .NET will keep the quotes
            Name = $"\"{name}\""
        };
        return c;
    }

    public static ByteArrayContent NamedFile(this string name, byte[] bytes, string fileName, string contentType)
    {
        var c = new ByteArrayContent(bytes);
        c.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        c.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = $"\"{name}\"",
            FileName = $"\"{fileName}\""
        };
        return c;
    }

}
