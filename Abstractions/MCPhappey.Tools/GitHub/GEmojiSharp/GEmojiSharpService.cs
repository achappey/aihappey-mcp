using System.ComponentModel;
using GEmojiSharp;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.GitHub.GEmojiSharp;

public static class GEmojiSharpService
{
    [Description("Convert emoji alias (e.g. ':tada:') into raw emoji character (🎉).")]
    [McpServerTool(
    Title = "Alias to emoji",
    Name = "github_gemojisharp_alias_to_emoji",
    ReadOnly = true,
    OpenWorld = false,
    UseStructuredContent = true)]
    public static async Task<string?> GEmojiSharp_AliasToEmoji(
    [Description("Emoji alias (e.g. ':tada:')")] string alias) =>
    await Task.FromResult(Emoji.Get(alias)?.Raw);

    [Description("Convert raw emoji character (e.g. '🎉') into its GitHub alias (:tada:).")]
    [McpServerTool(
        Title = "Emoji to alias",
        Name = "github_gemojisharp_emoji_to_alias",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    public static async Task<string?> GEmojiSharp_EmojiToAlias(
        [Description("Raw emoji character (e.g. '🎉')")] string emoji) =>
        await Task.FromResult(Emoji.Get(emoji)?.Alias());

    [Description("Replace all emoji aliases in text with raw emoji characters (emojify).")]
    [McpServerTool(
        Title = "Emojify text",
        Name = "github_gemojisharp_emojify",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    public static async Task<string?> GEmojiSharp_Emojify(
        [Description("Text containing emoji aliases (e.g. 'Great job :tada:')")] string input) =>
        await Task.FromResult(Emoji.Emojify(input));

    [Description("Replace all raw emoji characters in text with their GitHub aliases (demojify).")]
    [McpServerTool(
        Title = "Demojify text",
        Name = "github_gemojisharp_demojify",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    public static async Task<string?> GEmojiSharp_Demojify(
        [Description("Text containing raw emojis (e.g. 'Great job 🎉')")] string input) =>
        await Task.FromResult(Emoji.Demojify(input));

    [Description("Find the first matching emoji by description, alias or tag (e.g. 'party popper').")]
    [McpServerTool(
        Title = "Find emoji",
        Name = "github_gemojisharp_find_emoji",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    public static async Task<string?> GEmojiSharp_FindEmoji(
        [Description("Search term (e.g. 'party popper')")] string query) =>
        await Task.FromResult(Emoji.Find(query).FirstOrDefault()?.Raw);

    [Description("Return all raw skin tone variants for an emoji (if supported).")]
    [McpServerTool(
        Title = "Emoji skin tone variants",
        Name = "github_gemojisharp_skin_tones",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    public static async Task<IEnumerable<string>?> GEmojiSharp_SkinToneVariants(
        [Description("Raw emoji character (e.g. '✌️')")] string emoji) =>
        await Task.FromResult(Emoji.Get(emoji)?.RawSkinToneVariants());

}

