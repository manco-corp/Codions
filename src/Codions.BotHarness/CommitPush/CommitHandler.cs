namespace Codions.BotHarness.CommitPush;

/// <summary>
/// Third handler: creates the commit with the branch's commit message.
/// </summary>
internal sealed class CommitHandler : ICommitPushHandler
{
    public async Task HandleAsync(CommitPushContext context, Func<CommitPushContext, CancellationToken, Task> next, CancellationToken cancellationToken = default)
    {
        var message = context.Spec.Branch.CommitMessage
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"");
        await context.GitRunner.RunOrThrowAsync($"commit -m \"{message}\"", "Git commit failed.", cancellationToken);
        await next(context, cancellationToken);
    }
}
