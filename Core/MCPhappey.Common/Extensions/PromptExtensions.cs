
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MCPhappey.Core.Extensions;

public static class PromptArguments
{
    public static Dictionary<string, JsonElement> Create(params (string Key, object? Value)[] items)
    {
        var dict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in items)
        {
            dict[key] = JsonSerializer.SerializeToElement(value);
        }

        return dict;
    }
}


public static partial class PromptExtensions
{

    public static void ValidatePrompt(
        this ModelContextProtocol.Protocol.Prompt template,
        IReadOnlyDictionary<string, JsonElement>? argumentsDict = null)
    {
        var requiredArgs = template.Arguments?.Where(a => a.Required == true).ToList() ?? [];

        if (requiredArgs.Count == 0)
            return; // No required args, nothing to validate

        if (argumentsDict == null)
            throw new Exception($"Missing required argument(s): {string.Join(", ", requiredArgs.Select(a => a.Name))}");

        // Find missing required arguments
        var missing = requiredArgs
            .Where(a => !argumentsDict.ContainsKey(a.Name))
            .Select(a => a.Name)
            .ToList();

        if (missing.Count > 0)
            throw new ArgumentException($"Missing required argument(s): {string.Join(", ", missing)}");
    }

    public static int CountPromptArguments(this string template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var matches = PromptArgumentRegex().Matches(template);

        return matches
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    public static string FormatPrompt(
       this string prompt,
       ModelContextProtocol.Protocol.Prompt promptTemplate,
       IReadOnlyDictionary<string, JsonElement>? argumentsDict = null)
    {
        promptTemplate.ValidatePrompt(argumentsDict);

        var result = PromptArgumentRegex().Replace(prompt, match =>
        {
            var argName = match.Groups[1].Value;
            var argDef = promptTemplate.Arguments?.FirstOrDefault(a => a.Name == argName);

            if (argDef == null)
                return string.Empty; // Remove unknown placeholders

            if (argumentsDict != null && argumentsDict.TryGetValue(argName, out var value))
            {
                return value.ToString();
            }

            return string.Empty;
        });

        return result;
    }

    public static List<string> ExtractPromptArguments(this string template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var matches = PromptArgumentRegex().Matches(template);

        return [.. matches
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }


    //[GeneratedRegex("{(.*?)}")]
    [GeneratedRegex(@"\{([a-zA-Z0-9_-]+)\}")]
    private static partial Regex PromptArgumentRegex();
}