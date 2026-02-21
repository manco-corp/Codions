using Codions.BotHarness;
using Codions.BotHarness.Helpers;

namespace Codions.BotHarness.CommitPush;

/// <summary>
/// Fourth handler: runs authenticated push; on push protection (GH013), retries once with sanitization.
/// </summary>
internal sealed class PushHandler : ICommitPushHandler
{
    public async Task HandleAsync(CommitPushContext context, Func<CommitPushContext, CancellationToken, Task> next, CancellationToken cancellationToken = default)
    {
        var (exitCode, _, stderr) = await context.GitRunner.RunAuthenticatedPushAsync(cancellationToken);
        if (exitCode == 0)
            return;

        var message = PushFailureClassifier.GetMessage(stderr);
        if (!PushFailureClassifier.IsPushProtection(stderr))
            throw new InvalidOperationException(message);

        Console.WriteLine("[BotHarness] GitHub push protection detected. Attempting one sanitization retry...");
        var lastCommitFiles = await GitHelper.GetLastCommitChangedFilesAsync(context.RepoPath);
        var findings = await context.SecretScanner.RedactFilesAsync(context.RepoPath, lastCommitFiles);
        if (findings.Count == 0)
            throw new InvalidOperationException(message);

        await context.GitRunner.RunOrThrowAsync("add -A", "Git add failed after push-protection sanitization.", cancellationToken);
        await context.GitRunner.RunOrThrowAsync("commit -m \"chore: sanitize potential secrets before push\"",
            "Git commit failed after push-protection sanitization.", cancellationToken);

        var (retryExitCode, _, retryStderr) = await context.GitRunner.RunAuthenticatedPushAsync(cancellationToken);
        if (retryExitCode == 0)
            return;

        throw new InvalidOperationException(PushFailureClassifier.GetMessage(retryStderr));
    }
}
