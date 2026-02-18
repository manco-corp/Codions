using Codions.Contracts.Enums;

namespace Codions.Contracts.Interfaces;

public interface IArtifactStore
{
    Task<string> SaveArtifactAsync(string jobId, ArtifactType type, string content, CancellationToken ct = default);
    Task<string?> LoadArtifactAsync(string jobId, ArtifactType type, CancellationToken ct = default);
    string GetWorkspacePath(string jobId);
    Task EnsureWorkspaceAsync(string jobId, CancellationToken ct = default);
}
