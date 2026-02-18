using Microsoft.Extensions.Logging;
using Codions.Contracts.Interfaces;
using Codions.Contracts.Models;
using Octokit;

namespace Codions.Infrastructure.GitHub;

public class GitHubRepoProvider(string token, ILogger<GitHubRepoProvider> logger) : IRepoProvider
{
    private readonly GitHubClient _client = new(new ProductHeaderValue("CodionsBot"))
    {
        Credentials = new Credentials(token)
    };

    public async Task<string> CreatePullRequestAsync(
        RepoInfo repo,
        string branchName,
        string baseBranch,
        string title,
        string body,
        CancellationToken ct = default)
    {
        logger.LogInformation("Creating PR in {Owner}/{Repo}: {Branch} -> {Base}",
            repo.Owner, repo.Name, branchName, baseBranch);

        var pr = await _client.PullRequest.Create(
            repo.Owner,
            repo.Name,
            new NewPullRequest(title, branchName, baseBranch)
            {
                Body = body
            });

        logger.LogInformation("PR created: {Url}", pr.HtmlUrl);
        return pr.HtmlUrl;
    }
}
