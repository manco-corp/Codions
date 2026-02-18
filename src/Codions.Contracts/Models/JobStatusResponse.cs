using Codions.Contracts.Enums;

namespace Codions.Contracts.Models;

public sealed record JobStatusResponse
{
    public required string JobId { get; init; }
    public required JobStatus Status { get; init; }
    public required DateTime CreatedUtc { get; init; }
    public DateTime? UpdatedUtc { get; init; }
    public string? BranchName { get; init; }
    public string? PrUrl { get; init; }
    public string? ErrorMessage { get; init; }
    public int AttemptCount { get; init; }
}

public sealed record CreateJobResponse
{
    public required string JobId { get; init; }
    public required JobStatus Status { get; init; }
}
