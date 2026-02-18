namespace Codions.Contracts.Interfaces;

public interface IContainerRunner
{
    Task<ContainerRunResult> RunJobContainerAsync(
        string jobId,
        string workspacePath,
        Dictionary<string, string> environmentVariables,
        CancellationToken ct = default);
}

public sealed record ContainerRunResult
{
    public required string ContainerId { get; init; }
    public required int ExitCode { get; init; }
    public string? LogOutput { get; init; }
    public double ElapsedSeconds { get; init; }
}
