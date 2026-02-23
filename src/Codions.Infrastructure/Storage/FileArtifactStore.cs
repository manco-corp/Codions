using Codions.Contracts.Enums;
using Codions.Contracts.Interfaces;
using System.Text.RegularExpressions;

namespace Codions.Infrastructure.Storage;

public class FileArtifactStore(string basePath) : IArtifactStore
{
    private static readonly Regex JobIdRegex = new("^[a-f0-9]{12}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly string _basePath = Path.GetFullPath(basePath);
    private readonly string _basePathWithSeparator = EnsureTrailingSeparator(Path.GetFullPath(basePath));

    public string GetWorkspacePath(string jobId)
    {
        return ResolveWorkspacePath(jobId);
    }

    public Task EnsureWorkspaceAsync(string jobId, CancellationToken ct = default)
    {
        var path = ResolveWorkspacePath(jobId);
        Directory.CreateDirectory(path);
        return Task.CompletedTask;
    }

    public async Task<string> SaveArtifactAsync(string jobId, ArtifactType type, string content, CancellationToken ct = default)
    {
        var workspace = ResolveWorkspacePath(jobId);
        Directory.CreateDirectory(workspace);

        var fileName = GetFileName(type);
        var filePath = Path.Combine(workspace, fileName);

        await File.WriteAllTextAsync(filePath, content, ct);
        return filePath;
    }

    public async Task<string?> LoadArtifactAsync(string jobId, ArtifactType type, CancellationToken ct = default)
    {
        var workspace = ResolveWorkspacePath(jobId);
        var fileName = GetFileName(type);
        var filePath = Path.Combine(workspace, fileName);

        if (!File.Exists(filePath))
            return null;

        return await File.ReadAllTextAsync(filePath, ct);
    }

    private string ResolveWorkspacePath(string jobId)
    {
        if (!JobIdRegex.IsMatch(jobId))
            throw new ArgumentException("Invalid jobId format. Expected 12 lowercase hex characters.", nameof(jobId));

        var workspacePath = Path.GetFullPath(Path.Combine(_basePath, jobId));
        if (!workspacePath.StartsWith(_basePathWithSeparator, PathComparison))
            throw new InvalidOperationException("Resolved workspace path is outside the artifact store base path.");

        return workspacePath;
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
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
