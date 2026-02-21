namespace Codions.BotHarness.CommitPush;

/// <summary>
/// First handler in the commit-push chain: stages all changes with git add -A.
/// </summary>
internal sealed class AddAllHandler : ICommitPushHandler
{
    public async Task HandleAsync(CommitPushContext context, Func<CommitPushContext, CancellationToken, Task> next, CancellationToken cancellationToken = default)
    {
        await context.GitRunner.RunOrThrowAsync("add -A", "Git add failed.", cancellationToken);
        await next(context, cancellationToken);
    }
}
