using Codions.BotHarness;
using Codions.Contracts.Models;

namespace Codions.BotHarness.CommitPush;

/// <summary>
/// Immutable context for the commit-and-push chain of responsibility.
/// Carries repo path, job spec, token, secret scanner, and git runner.
/// </summary>
public sealed class CommitPushContext(
    string repoPath,
    JobSpec spec,
    string githubToken,
    Helpers.SecretScanner secretScanner,
    IGitCommandRunner gitRunner)
{
    public string RepoPath { get; } = repoPath;
    public JobSpec Spec { get; } = spec;
    public string GithubToken { get; } = githubToken;
    public Helpers.SecretScanner SecretScanner { get; } = secretScanner;
    public IGitCommandRunner GitRunner { get; } = gitRunner;
}
