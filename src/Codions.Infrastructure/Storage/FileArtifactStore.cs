using Codions.Contracts.Enums;
using Codions.Contracts.Interfaces;

namespace Codions.Infrastructure.Storage;

public class FileArtifactStore(string basePath) : IArtifactStore
{
    public string GetWorkspacePath(string jobId)
    {
        return Path.Combine(basePath, jobId);
    }

    public Task EnsureWorkspaceAsync(string jobId, CancellationToken ct = default)
    {
        var path = GetWorkspacePath(jobId);
        Directory.CreateDirectory(path);
        return Task.CompletedTask;
    }

    public async Task<string> SaveArtifactAsync(string jobId, ArtifactType type, string content, CancellationToken ct = default)
    {
        var workspace = GetWorkspacePath(jobId);
        Directory.CreateDirectory(workspace);

        var fileName = GetFileName(type);
        var filePath = Path.Combine(workspace, fileName);

        await File.WriteAllTextAsync(filePath, content, ct);
        return filePath;
    }

    public async Task<string?> LoadArtifactAsync(string jobId, ArtifactType type, CancellationToken ct = default)
    {
        var workspace = GetWorkspacePath(jobId);
        var fileName = GetFileName(type);
        var filePath = Path.Combine(workspace, fileName);

        if (!File.Exists(filePath))
            return null;

        return await File.ReadAllTextAsync(filePath, ct);
    }

    private static string GetFileName(ArtifactType type) => type switch
    {
        ArtifactType.JobSpec => "job-spec.json",
        ArtifactType.ContextPack => "context-pack.json",
        ArtifactType.RunSummary => "run-summary.json",
        ArtifactType.Log => "output.log",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
}
