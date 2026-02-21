namespace Codions.BotHarness.CommitPush;

/// <summary>
/// Runs git commands in a fixed working directory (repo path).
/// Used by the commit-push pipeline to add, commit, and push.
/// </summary>
public interface IGitCommandRunner
{
    Task RunOrThrowAsync(string arguments, string failureContext, CancellationToken cancellationToken = default);

    Task<(int ExitCode, string Stdout, string Stderr)> RunAuthenticatedPushAsync(CancellationToken cancellationToken = default);
}
