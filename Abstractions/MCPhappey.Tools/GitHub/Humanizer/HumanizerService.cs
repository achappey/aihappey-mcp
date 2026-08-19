using System.ComponentModel;
using Humanizer;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.GitHub.Humanizer;

public static class HumanizerService
{
    [Description("Humanizes a string")]
    [McpServerTool(Title = "Humanize string",
           Name = "github_humanizer_humanize",
           ReadOnly = true,
           OpenWorld = false)]
    public static async Task<string?> GitHubHumanizer_Humanize(
           [Description("Input text to humanize")] string input) =>
               await Task.FromResult(input.Humanize());

    [Description("Dehumanizes a string (e.g. 'the quick brown fox' -> 'TheQuickBrownFox')")]
    [McpServerTool(
           Title = "Dehumanize string",
           Name = "github_humanizer_dehumanize",
           ReadOnly = true,
           OpenWorld = false)]
    public static async Task<string?> GitHubHumanizer_Dehumanize(
           [Description("Input text to dehumanize")] string input) =>
           await Task.FromResult(input.Dehumanize());

    [Description("Pluralizes a word (e.g. 'dog' -> 'dogs')")]
    [McpServerTool(
        Title = "Pluralize word",
        Name = "github_humanizer_pluralize",
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<string?> GitHubHumanizer_Pluralize(
        [Description("Word to pluralize")] string word) =>
        await Task.FromResult(word.Pluralize());

    [Description("Singularizes a word (e.g. 'cars' -> 'car')")]
    [McpServerTool(
        Title = "Singularize word",
        Name = "github_humanizer_singularize",
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<string?> GitHubHumanizer_Singularize(
        [Description("Word to singularize")] string word) =>
        await Task.FromResult(word.Singularize());

    [Description("Converts a number into words (e.g. 123 -> 'one hundred and twenty-three')")]
    [McpServerTool(
        Title = "Number to words",
        Name = "github_humanizer_number_to_words",
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<string?> GitHubHumanizer_NumberToWords(
        [Description("Number to convert")] int number) =>
        await Task.FromResult(number.ToWords());

    [Description("Converts a DateTime to a human-readable relative time (e.g. '2 hours ago')")]
    [McpServerTool(
        Title = "Humanize date/time",
        Name = "github_humanizer_datetime_humanize",
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<string?> GitHubHumanizer_DateTimeHumanize(
        [Description("Date/time to humanize")] DateTime input) =>
        await Task.FromResult(input.Humanize());

    [Description("Converts a TimeSpan to a human-readable string (e.g. '3 days', '5 minutes')")]
    [McpServerTool(
        Title = "Humanize timespan",
        Name = "github_humanizer_timespan_humanize",
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<string?> GitHubHumanizer_TimeSpanHumanize(
        [Description("Duration to humanize (in seconds)")] int seconds) =>
        await Task.FromResult(TimeSpan.FromSeconds(seconds).Humanize());

    [Description("Converts a number to its ordinal form (e.g. 1 -> '1st', 22 -> '22nd')")]
    [McpServerTool(
Title = "Ordinalize number",
Name = "github_humanizer_ordinalize",
ReadOnly = true,
OpenWorld = false)]
    public static Task<string> GitHubHumanizer_Ordinalize(
[Description("Number to ordinalize")] int number) =>
Task.FromResult(number.Ordinalize());


    [Description("Truncates text to a maximum length while preserving a readable ending")]
    [McpServerTool(
        Title = "Truncate text",
        Name = "github_humanizer_truncate",
        ReadOnly = true,
        OpenWorld = false)]
    public static Task<string> GitHubHumanizer_Truncate(
        [Description("Text to truncate")] string input,
        [Description("Maximum number of characters")] int length) =>
        Task.FromResult(input.Truncate(length));


    [Description("Converts a quantity and word to a human-readable quantity (e.g. 2 and 'car' -> '2 cars')")]
    [McpServerTool(
        Title = "Humanize quantity",
        Name = "github_humanizer_quantity",
        ReadOnly = true,
        OpenWorld = false)]
    public static Task<string> GitHubHumanizer_Quantity(
        [Description("Word to quantify")] string word,
        [Description("Quantity")] int quantity) =>
        Task.FromResult(word.ToQuantity(quantity));

}

