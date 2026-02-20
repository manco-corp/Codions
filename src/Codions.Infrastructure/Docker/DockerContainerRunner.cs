using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;
using Codions.Contracts.Interfaces;
using System.Diagnostics;
using System.Text;

namespace Codions.Infrastructure.Docker;

public class DockerContainerRunner(DockerSettings settings, ILogger<DockerContainerRunner> logger) : IContainerRunner
{
    private readonly DockerClient _client = new DockerClientConfiguration(
        new Uri(GetDockerEndpoint())).CreateClient();

    public async Task<ContainerRunResult> RunJobContainerAsync(
        string jobId,
        string workspacePath,
        Dictionary<string, string> environmentVariables,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var absoluteWorkspace = Path.GetFullPath(workspacePath);

        var bindMountSource = absoluteWorkspace;
        if (!string.IsNullOrEmpty(settings.HostWorkspacesPath))
        {
            bindMountSource = Path.Combine(settings.HostWorkspacesPath, jobId);
        }

        logger.LogInformation("Starting container for job {JobId} with workspace {Workspace} (bind: {Bind})",
            jobId, absoluteWorkspace, bindMountSource);

        var env = environmentVariables
            .Select(kv => $"{kv.Key}={kv.Value}")
            .ToList();

        var createResponse = await _client.Containers.CreateContainerAsync(
            new CreateContainerParameters
            {
                Image = settings.BotImage,
                Name = $"codions-bot-{jobId}",
                Env = env,
                HostConfig = new HostConfig
                {
                    Binds = [$"{bindMountSource}:/workspace"],
                    Memory = settings.MemoryLimitMb * 1024 * 1024,
                    NanoCPUs = (long)(settings.CpuLimit * 1_000_000_000),
                    NetworkMode = settings.NetworkMode,
                    AutoRemove = false
                },
                WorkingDir = "/workspace"
            }, ct);

        var containerId = createResponse.ID;
        logger.LogInformation("Created container {ContainerId} for job {JobId}", containerId[..12], jobId);

        var started = await _client.Containers.StartContainerAsync(containerId, new ContainerStartParameters(), ct);
        if (!started)
        {
            logger.LogError("Failed to start container {ContainerId}", containerId[..12]);
            return new ContainerRunResult
            {
                ContainerId = containerId,
                ExitCode = -1,
                LogOutput = "Failed to start container",
                ElapsedSeconds = sw.Elapsed.TotalSeconds
            };
        }

        var waitResponse = await _client.Containers.WaitContainerAsync(containerId, ct);

        var logStream = await _client.Containers.GetContainerLogsAsync(containerId, false,
            new ContainerLogsParameters { ShowStdout = true, ShowStderr = true, Follow = false },
            ct);

        var logBuilder = new StringBuilder();
        var buffer = new byte[4096];
        var readResult = await logStream.ReadOutputAsync(buffer, 0, buffer.Length, ct);
        while (readResult.Count > 0)
        {
            logBuilder.Append(Encoding.UTF8.GetString(buffer, 0, readResult.Count));
            readResult = await logStream.ReadOutputAsync(buffer, 0, buffer.Length, ct);
        }
        sw.Stop();

        var logOutput = logBuilder.ToString();
        var logPath = Path.Combine(absoluteWorkspace, "output.log");
        await File.WriteAllTextAsync(logPath, logOutput, ct);

        logger.LogInformation(
            "Container {ContainerId} for job {JobId} exited with code {ExitCode} in {Elapsed:F1}s",
            containerId[..12], jobId, waitResponse.StatusCode, sw.Elapsed.TotalSeconds);

        try
        {
            await _client.Containers.RemoveContainerAsync(containerId,
                new ContainerRemoveParameters { Force = true }, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to remove container {ContainerId}", containerId[..12]);
        }

        return new ContainerRunResult
        {
            ContainerId = containerId,
            ExitCode = (int)waitResponse.StatusCode,
            LogOutput = logOutput,
            ElapsedSeconds = sw.Elapsed.TotalSeconds
        };
    }

    private static string GetDockerEndpoint()
    {
        if (OperatingSystem.IsWindows())
            return "npipe://./pipe/docker_engine";
        return "unix:///var/run/docker.sock";
    }
}

public sealed record DockerSettings
{
    public string BotImage { get; init; } = "codions-bot:latest";
    /// <summary>
    /// Optional. Root directory for 'docker build' (must contain docker/bot/Dockerfile). If null, discovered from current directory.
    /// </summary>
    public string? BuildContextPath { get; init; }
    public string WorkspacesPath { get; init; } = "data/workspaces";
    /// <summary>
    /// Host-side workspaces path for Docker bind mounts (DinD). When the Worker runs inside a container,
    /// the internal WorkspacesPath differs from the host path that the Docker daemon needs for bind mounts.
    /// </summary>
    public string? HostWorkspacesPath { get; init; }
    public string NetworkMode { get; init; } = "bridge";
    public long MemoryLimitMb { get; init; } = 2048;
    public double CpuLimit { get; init; } = 2.0;
}
