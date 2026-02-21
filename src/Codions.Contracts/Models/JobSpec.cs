using Codions.Contracts.Enums;

namespace Codions.Contracts.Models;

public sealed record JobSpec
{
    public required string JobId { get; init; }
    public required DateTime CreatedUtc { get; init; }
    public required RepoInfo Repo { get; init; }
    public required BranchSpec Branch { get; init; }
    public required TaskInfo Task { get; init; }
    public required RunProfile RunProfile { get; init; }
}

public sealed record BranchSpec
{
    public required string Name { get; init; }
    public required string CommitMessage { get; init; }
    public required string PrTitle { get; init; }
    public string? PrBodyTemplate { get; init; }
}

public sealed record RunProfile
{
    public ModelTier ModelTier { get; init; } = ModelTier.Balanced;
    public string ModelName { get; init; } = "qwen2.5-coder:14b";
    public int MaxAgentSteps { get; init; } = 16;
    public int MaxWallClockMinutes { get; init; } = 25;
    public int MaxFixAttempts { get; init; } = 2;
    public LocalGates LocalGates { get; init; } = new();
    public TestStrategy TestStrategy { get; init; } = new();
    public Policies Policies { get; init; } = new();
}

public sealed record LocalGates
{
    public bool Format { get; init; } = true;
    public bool Build { get; init; } = true;
    public bool Tests { get; init; } = true;
}

public sealed record TestStrategy
{
    public string Mode { get; init; } = "targeted-first";
    public List<string> TargetedCommands { get; init; } = [];
    public string FallbackCommand { get; init; } = "";
    public int MaxTestMinutes { get; init; } = 15;
}

public sealed record Policies
{
    public bool NoNetworkEgress { get; init; } = true;
    public List<string> DisallowedPaths { get; init; } = [];
    public bool AllowFileWritesOutsideScope { get; init; } = false;
}
