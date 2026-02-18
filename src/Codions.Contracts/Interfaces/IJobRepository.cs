using Codions.Contracts.Enums;
using Codions.Contracts.Models;

namespace Codions.Contracts.Interfaces;

public interface IJobRepository
{
    Task<JobEntity> CreateAsync(JobRequest request, JobSpec spec, CancellationToken ct = default);
    Task<JobEntity?> GetByIdAsync(string jobId, CancellationToken ct = default);
    Task<List<JobEntity>> ListRecentAsync(int limit = 20, CancellationToken ct = default);
    Task<JobEntity?> DequeueNextAsync(CancellationToken ct = default);
    Task UpdateStatusAsync(string jobId, JobStatus status, string? errorMessage = null, CancellationToken ct = default);
    Task SetPrUrlAsync(string jobId, string prUrl, CancellationToken ct = default);
    Task IncrementAttemptAsync(string jobId, CancellationToken ct = default);
}

public sealed class JobEntity
{
    public required string Id { get; set; }
    public required JobStatus Status { get; set; }
    public required DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public required string RequesterJson { get; set; }
    public required string RepoJson { get; set; }
    public required string TaskJson { get; set; }
    public required string RunProfileJson { get; set; }
    public required string BranchName { get; set; }
    public string? PrUrl { get; set; }
    public string? ErrorMessage { get; set; }
    public int AttemptCount { get; set; }
}
