namespace MCPhappey.Tools.Memory.GitHubCopilot;

internal sealed record GitHubCopilotMemoryEntry(
    string Id,
    string Subject,
    string Fact,
    string[] Citations,
    string Reason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt);
