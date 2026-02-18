namespace Codions.Contracts.Models;

public sealed record ContextPack
{
    public required string JobId { get; init; }
    public RepoInsights RepoInsights { get; init; } = new();
    public List<SearchResultEntry> SearchResults { get; init; } = [];
    public List<string> RelevantFilesShortlist { get; init; } = [];
    public List<LinkedText> LinkedTexts { get; init; } = [];
    public List<string> Rules { get; init; } = [];
}

public sealed record RepoInsights
{
    public List<string> Solutions { get; init; } = [];
    public List<string> Projects { get; init; } = [];
    public SuggestedCommands SuggestedCommands { get; init; } = new();
}

public sealed record SuggestedCommands
{
    public string? Format { get; init; }
    public string? Build { get; init; }
    public string? Test { get; init; }
}

public sealed record SearchResultEntry
{
    public required string Query { get; init; }
    public List<SearchMatch> Matches { get; init; } = [];
}

public sealed record SearchMatch
{
    public required string Path { get; init; }
    public int? Line { get; init; }
    public string? Snippet { get; init; }
}

public sealed record LinkedText
{
    public required string Kind { get; init; }
    public required string Content { get; init; }
}
