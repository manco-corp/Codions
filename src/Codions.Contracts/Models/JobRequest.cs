using Codions.Contracts.Enums;

namespace Codions.Contracts.Models;

public sealed record JobRequest
{
    public required string Source { get; init; }
    public required RequesterInfo Requester { get; init; }
    public required RepoInfo Repo { get; init; }
    public required TaskInfo Task { get; init; }
    public Preferences? Preferences { get; init; }
}

public sealed record RequesterInfo
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string? Email { get; init; }
}

public sealed record RepoInfo
{
    public required RepoProvider Provider { get; init; }
    public required string Owner { get; init; }
    public required string Name { get; init; }
    public required string CloneUrl { get; init; }
    public string DefaultBranch { get; init; } = "main";
}

public sealed record TaskInfo
{
    public required string Title { get; init; }
    public required string Description { get; init; }
    public List<string> AcceptanceCriteria { get; init; } = [];
    public List<string> Links { get; init; } = [];
    public List<string> ScopeHints { get; init; } = [];
}

public sealed record Preferences
{
    public TaskPriority Priority { get; init; } = TaskPriority.Normal;
    public string? ModelHint { get; init; }
    public int MaxMinutes { get; init; } = 25;
}
