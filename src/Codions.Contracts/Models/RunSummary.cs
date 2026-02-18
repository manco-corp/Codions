namespace Codions.Contracts.Models;

public sealed record RunSummary
{
    public required string JobId { get; init; }
    public required bool Success { get; init; }
    public string? PrUrl { get; init; }
    public string? ErrorMessage { get; init; }
    public List<GateResult> GateResults { get; init; } = [];
    public int AgentStepsUsed { get; init; }
    public int AttemptNumber { get; init; }
    public string? ModelUsed { get; init; }
    public double ElapsedMinutes { get; init; }
    public List<string> FilesChanged { get; init; } = [];
    public string? CommitSha { get; init; }
}

public sealed record GateResult
{
    public required string GateName { get; init; }
    public required string Command { get; init; }
    public required bool Passed { get; init; }
    public int ExitCode { get; init; }
    public string? Output { get; init; }
    public double DurationSeconds { get; init; }
}
