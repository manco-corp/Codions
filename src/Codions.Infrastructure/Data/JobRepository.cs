using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Codions.Contracts.Enums;
using Codions.Contracts.Interfaces;
using Codions.Contracts.Models;

namespace Codions.Infrastructure.Data;

public class JobRepository : IJobRepository
{
    private readonly AppDbContext _db;

    public JobRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<JobEntity> CreateAsync(JobRequest request, JobSpec spec, CancellationToken ct = default)
    {
        var entity = new JobEntity
        {
            Id = spec.JobId,
            Status = JobStatus.Created,
            CreatedUtc = spec.CreatedUtc,
            UpdatedUtc = spec.CreatedUtc,
            RequesterJson = JsonSerializer.Serialize(request.Requester),
            RepoJson = JsonSerializer.Serialize(spec.Repo),
            TaskJson = JsonSerializer.Serialize(spec.Task),
            RunProfileJson = JsonSerializer.Serialize(spec.RunProfile),
            BranchName = spec.Branch.Name,
            AttemptCount = 0
        };

        _db.Jobs.Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<JobEntity?> GetByIdAsync(string jobId, CancellationToken ct = default)
    {
        return await _db.Jobs.FindAsync([jobId], ct);
    }

    public async Task<List<JobEntity>> ListRecentAsync(int limit = 20, CancellationToken ct = default)
    {
        return await _db.Jobs
            .OrderByDescending(j => j.CreatedUtc)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<JobEntity?> DequeueNextAsync(CancellationToken ct = default)
    {
        var job = await _db.Jobs
            .Where(j => j.Status == JobStatus.Queued)
            .OrderBy(j => j.CreatedUtc)
            .FirstOrDefaultAsync(ct);

        if (job is not null)
        {
            job.Status = JobStatus.Running;
            job.UpdatedUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return job;
    }

    public async Task UpdateStatusAsync(string jobId, JobStatus status, string? errorMessage = null, CancellationToken ct = default)
    {
        var job = await _db.Jobs.FindAsync([jobId], ct)
            ?? throw new InvalidOperationException($"Job {jobId} not found");

        job.Status = status;
        job.UpdatedUtc = DateTime.UtcNow;
        if (errorMessage is not null)
            job.ErrorMessage = errorMessage;

        await _db.SaveChangesAsync(ct);
    }

    public async Task SetPrUrlAsync(string jobId, string prUrl, CancellationToken ct = default)
    {
        var job = await _db.Jobs.FindAsync([jobId], ct)
            ?? throw new InvalidOperationException($"Job {jobId} not found");

        job.PrUrl = prUrl;
        job.UpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task IncrementAttemptAsync(string jobId, CancellationToken ct = default)
    {
        var job = await _db.Jobs.FindAsync([jobId], ct)
            ?? throw new InvalidOperationException($"Job {jobId} not found");

        job.AttemptCount++;
        job.UpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }
}
