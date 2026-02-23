using System.Text.Json;
using Codions.Contracts.Enums;
using Codions.Contracts.Interfaces;
using Codions.Contracts.Models;

namespace Codions.Core;

public class OrchestratorService(
    IJobRepository jobRepo,
    IArtifactStore artifactStore,
    RunProfileDefaults defaults)
{
    public async Task<(JobSpec spec, CreateJobResponse response)> CreateJobAsync(
        JobRequest request, CancellationToken ct = default)
    {
        var jobId = Guid.NewGuid().ToString("N")[..12];
        var branchName = $"agent/job-{jobId[..8]}";

        var runProfile = ModelTierRouter.Route(request.Task, request.Preferences, defaults);

        var spec = new JobSpec
        {
            JobId = jobId,
            CreatedUtc = DateTime.UtcNow,
            Repo = request.Repo,
            Branch = new BranchSpec
            {
                Name = branchName,
                CommitMessage = request.Task.Title,
                PrTitle = request.Task.Title,
                PrBodyTemplate = null
            },
            Task = request.Task,
            RunProfile = runProfile
        };

        var created = false;
        try
        {
            await jobRepo.CreateAsync(request, spec, ct);
            created = true;

            await jobRepo.UpdateStatusAsync(jobId, JobStatus.HydratingContext, ct: ct);

            var contextPack = ContextPackBuilder.Build(jobId, request.Task);

            var specJson = JsonSerializer.Serialize(spec, JsonOptions.Default);
            var contextJson = JsonSerializer.Serialize(contextPack, JsonOptions.Default);

            await artifactStore.SaveArtifactAsync(jobId, ArtifactType.JobSpec, specJson, ct);
            await artifactStore.SaveArtifactAsync(jobId, ArtifactType.ContextPack, contextJson, ct);

            await jobRepo.UpdateStatusAsync(jobId, JobStatus.Queued, ct: ct);

            var response = new CreateJobResponse
            {
                JobId = jobId,
                Status = JobStatus.Queued
            };

            return (spec, response);
        }
        catch (Exception ex)
        {
            if (created)
            {
                var error = $"Job hydration failed during creation: {Truncate(ex.Message, 500)}";
                try
                {
                    await jobRepo.UpdateStatusAsync(
                        jobId,
                        JobStatus.CompletedFailed,
                        error,
                        CancellationToken.None);
                }
                catch
                {
                    // Best-effort compensating action. Preserve original failure.
                }
            }

            throw;
        }
    }

    public async Task<JobStatusResponse?> GetJobStatusAsync(string jobId, CancellationToken ct = default)
    {
        var entity = await jobRepo.GetByIdAsync(jobId, ct);
        if (entity is null) return null;

        return new JobStatusResponse
        {
            JobId = entity.Id,
            Status = entity.Status,
            CreatedUtc = entity.CreatedUtc,
            UpdatedUtc = entity.UpdatedUtc,
            BranchName = entity.BranchName,
            PrUrl = entity.PrUrl,
            ErrorMessage = entity.ErrorMessage,
            AttemptCount = entity.AttemptCount
        };
    }

    public async Task<List<JobStatusResponse>> ListJobsAsync(int limit = 20, CancellationToken ct = default)
    {
        var entities = await jobRepo.ListRecentAsync(limit, ct);
        return entities.Select(e => new JobStatusResponse
        {
            JobId = e.Id,
            Status = e.Status,
            CreatedUtc = e.CreatedUtc,
            UpdatedUtc = e.UpdatedUtc,
            BranchName = e.BranchName,
            PrUrl = e.PrUrl,
            ErrorMessage = e.ErrorMessage,
            AttemptCount = e.AttemptCount
        }).ToList();
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;

        return value[..maxLength];
    }
}

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}
