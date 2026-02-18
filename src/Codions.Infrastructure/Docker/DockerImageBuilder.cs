using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Codions.Infrastructure.Docker;

/// <summary>
/// Ensures the bot Docker image exists, building it from docker/bot/Dockerfile when missing.
/// </summary>
public static class DockerImageBuilder
{
    private const string DockerfileRelativePath = "docker/bot/Dockerfile";

    public static async Task EnsureBotImageExistsAsync(DockerSettings settings, ILogger logger, CancellationToken ct = default)
    {
        var imageName = settings.BotImage;
        if (await ImageExistsAsync(imageName, logger, ct).ConfigureAwait(false))
        {
            logger.LogInformation("Docker image {Image} already exists, skipping build", imageName);
            return;
        }

        var contextPath = ResolveBuildContextPath(settings.BuildContextPath, logger);
        if (string.IsNullOrEmpty(contextPath))
        {
            logger.LogWarning(
                "Cannot build image {Image}: build context path not set and docker/bot/Dockerfile not found. Set Docker:BuildContextPath in appsettings to the repo root.",
                imageName);
            return;
        }

        logger.LogInformation("Building Docker image {Image} from {ContextPath}", imageName, contextPath);
        await BuildImageAsync(imageName, contextPath, logger, ct).ConfigureAwait(false);
    }

    private static async Task<bool> ImageExistsAsync(string imageName, ILogger logger, CancellationToken ct)
    {
        try
        {
            var endpoint = GetDockerEndpoint();
            using var client = new DockerClientConfiguration(new Uri(endpoint)).CreateClient();
            var list = await client.Images.ListImagesAsync(new ImagesListParameters(), ct).ConfigureAwait(false);
            foreach (var img in list)
            {
                if (img.RepoTags != null && img.RepoTags.Any(t => t.Equals(imageName, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not list Docker images; will attempt build");
            return false;
        }
    }

    private static string? ResolveBuildContextPath(string? configuredPath, ILogger logger)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var full = Path.GetFullPath(configuredPath);
            if (File.Exists(Path.Combine(full, DockerfileRelativePath)))
                return full;
            logger.LogWarning("Configured Docker:BuildContextPath does not contain {Dockerfile}: {Path}", DockerfileRelativePath, full);
            return null;
        }

        var start = AppContext.BaseDirectory ?? Environment.CurrentDirectory;
        var dir = new DirectoryInfo(Path.GetFullPath(start));
        while (dir != null)
        {
            var dockerfilePath = Path.Combine(dir.FullName, DockerfileRelativePath);
            if (File.Exists(dockerfilePath))
            {
                logger.LogDebug("Resolved build context to {Path}", dir.FullName);
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        return null;
    }

    private static async Task BuildImageAsync(string imageTag, string contextPath, ILogger logger, CancellationToken ct)
    {
        var dockerfilePath = "docker/bot/Dockerfile";
        var args = $"build -t \"{imageTag}\" -f \"{dockerfilePath}\" .";

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = args,
                WorkingDirectory = contextPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        var output = new List<string>();
        var error = new List<string>();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) { output.Add(e.Data); logger.LogDebug("[docker] {Line}", e.Data); } };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) { error.Add(e.Data); logger.LogDebug("[docker] {Line}", e.Data); } };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var err = string.Join(Environment.NewLine, error);
            var @out = string.Join(Environment.NewLine, output);
            logger.LogError("Docker build failed with exit code {ExitCode}. StdErr: {Stderr} StdOut: {Stdout}",
                process.ExitCode, err, @out);
            throw new InvalidOperationException($"Docker build failed with exit code {process.ExitCode}. {err}");
        }

        logger.LogInformation("Docker image {Image} built successfully", imageTag);
    }

    private static string GetDockerEndpoint()
    {
        if (OperatingSystem.IsWindows())
            return "npipe://./pipe/docker_engine";
        return "unix:///var/run/docker.sock";
    }
}
