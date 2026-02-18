using Codions.Contracts.Models;

namespace Codions.Contracts.Interfaces;

public interface IRepoProvider
{
    Task<string> CreatePullRequestAsync(
        RepoInfo repo,
        string branchName,
        string baseBranch,
        string title,
        string body,
        CancellationToken ct = default);
}
