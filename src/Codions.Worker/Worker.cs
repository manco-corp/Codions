using System.Text.Json;
using Codions.Contracts.Enums;
using Codions.Contracts.Interfaces;
using Codions.Contracts.Models;
using Codions.Infrastructure.Data;
using Codions.Infrastructure.Security;

namespace Codions.Worker;

public class JobProcessorService(
    IServiceProvider services,
    IContainerRunner containerRunner,
    IArtifactStore artifactStore,
    AuditLogger auditLogger,
    ILogger<JobProcessorService> logger,
    IConfiguration config) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Job processor service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessNextJobAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in job processing loop");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }

        logger.LogInformation("Job processor service stopped");
    }

    private async Task ProcessNextJobAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var jobRepo = scope.ServiceProvider.GetRequiredService<IJobRepository>();

        var job = await jobRepo.DequeueNextAsync(ct);
        if (job is null)
            return;

        logger.LogInformation("Processing job {JobId}", job.Id);

        await auditLogger.LogJobEventAsync(new AuditEntry
        {
            JobId = job.Id,
            Event = "JobStarted",
            TimestampUtc = DateTime.UtcNow,
            Requester = job.RequesterJson,
            Repo = job.RepoJson,
            Branch = job.BranchName
        });

        try
        {
            var workspacePath = artifactStore.GetWorkspacePath(job.Id);
            await artifactStore.EnsureWorkspaceAsync(job.Id, ct);

            var spec = await artifactStore.LoadArtifactAsync(job.Id, ArtifactType.JobSpec, ct);
            var context = await artifactStore.LoadArtifactAsync(job.Id, ArtifactType.ContextPack, ct);

            if (spec is null || context is null)
            {
                logger.LogError("Missing artifacts for job {JobId}", job.Id);
                await jobRepo.UpdateStatusAsync(job.Id, JobStatus.CompletedFailed,
                    "Missing job-spec or context-pack artifacts", ct);
                return;
            }

            var envVars = BuildEnvironmentVariables(job);
            logger.LogInformation("Job {JobId}: GITHUB_TOKEN passed to container: {HasToken}", job.Id, envVars.ContainsKey("GITHUB_TOKEN") ? "Yes" : "No");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var jobSpec = JsonSerializer.Deserialize<JobSpec>(spec, JsonOptions);
            var wallClockMinutes = jobSpec?.RunProfile.MaxWallClockMinutes ?? 25;
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(wallClockMinutes + 2));

            var result = await containerRunner.RunJobContainerAsync(
                job.Id, workspacePath, envVars, timeoutCts.Token);

            var redactedLog = result.LogOutput is not null
                ? TokenRedactor.Redact(result.LogOutput)
                : null;

            if (redactedLog is not null)
            {
                await artifactStore.SaveArtifactAsync(job.Id, ArtifactType.Log, redactedLog, ct);
            }

            string? prUrl = null;
            if (result.ExitCode == 0)
            {
                var summaryJson = await artifactStore.LoadArtifactAsync(job.Id, ArtifactType.RunSummary, ct);
                if (summaryJson is not null)
                {
                    var summary = JsonSerializer.Deserialize<RunSummary>(summaryJson, JsonOptions);
                    if (summary?.PrUrl is not null)
                    {
                        prUrl = summary.PrUrl;
                        await jobRepo.SetPrUrlAsync(job.Id, summary.PrUrl, ct);
                    }
                }

                await jobRepo.UpdateStatusAsync(job.Id, JobStatus.CompletedSuccess, ct: ct);
                logger.LogInformation("Job {JobId} completed successfully", job.Id);
            }
            else
            {
                await jobRepo.UpdateStatusAsync(job.Id, JobStatus.CompletedFailed,
                    $"Container exited with code {result.ExitCode}", ct);
                logger.LogWarning("Job {JobId} failed with exit code {ExitCode}", job.Id, result.ExitCode);
            }

            await auditLogger.LogJobEventAsync(new AuditEntry
            {
                JobId = job.Id,
                Event = result.ExitCode == 0 ? "JobSucceeded" : "JobFailed",
                TimestampUtc = DateTime.UtcNow,
                Branch = job.BranchName,
                PrUrl = prUrl,
                Details = $"ExitCode={result.ExitCode}, Elapsed={result.ElapsedSeconds:F1}s"
            });
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Job {JobId} timed out", job.Id);
            await jobRepo.UpdateStatusAsync(job.Id, JobStatus.CompletedFailed, "Job timed out", ct);

            await auditLogger.LogJobEventAsync(new AuditEntry
            {
                JobId = job.Id,
                Event = "JobTimedOut",
                TimestampUtc = DateTime.UtcNow,
                Branch = job.BranchName
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Job {JobId} failed with exception", job.Id);
            await jobRepo.UpdateStatusAsync(job.Id, JobStatus.CompletedFailed, ex.Message, ct);

            await auditLogger.LogJobEventAsync(new AuditEntry
            {
                JobId = job.Id,
                Event = "JobException",
                TimestampUtc = DateTime.UtcNow,
                Branch = job.BranchName,
                Details = ex.Message
            });
        }
    }

    private Dictionary<string, string> BuildEnvironmentVariables(JobEntity job)
    {
        var envVars = new Dictionary<string, string>
        {
            ["JOB_ID"] = job.Id,
            ["WORKSPACE_PATH"] = "/workspace"
        };

        var githubToken = config["GitHub:Token"]?.Trim() ?? "";
        if (!string.IsNullOrEmpty(githubToken))
            envVars["GITHUB_TOKEN"] = githubToken;

        var ollamaBaseUrl = config["Ollama:BaseUrl"];
        envVars["OLLAMA_BASE_URL"] = !string.IsNullOrEmpty(ollamaBaseUrl)
            ? ollamaBaseUrl.Replace("localhost", "host.docker.internal")
            : "http://host.docker.internal:11434";

        var modelsSection = config.GetSection("Ollama:Models");
        if (modelsSection.Exists())
        {
            envVars["MODEL_CHEAP"] = modelsSection["Cheap"] ?? "qwen2.5-coder:7b";
            envVars["MODEL_BALANCED"] = modelsSection["Balanced"] ?? "qwen2.5-coder:14b";
            envVars["MODEL_STRONG"] = modelsSection["Strong"] ?? "qwen2.5-coder:32b";
        }

        return envVars;
    }
}
