namespace MCPhappey.Common.Extensions;

public static partial class StringExtensions
{
    public static string Slugify(this string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        // Replace spaces with hyphens
        var replaced = input.Replace(' ', '-');
        // Remove all chars except a-z, A-Z, 0-9, and -
        return SlugRegex().Replace(replaced, "");
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"[^a-zA-Z0-9\-]")]
    private static partial System.Text.RegularExpressions.Regex SlugRegex();
}
