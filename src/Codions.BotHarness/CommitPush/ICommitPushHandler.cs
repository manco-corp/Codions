namespace Codions.BotHarness.CommitPush;

/// <summary>
/// One step in the commit-and-push chain of responsibility.
/// Each handler performs its work then calls next(context).
/// </summary>
public interface ICommitPushHandler
{
    Task HandleAsync(CommitPushContext context, Func<CommitPushContext, CancellationToken, Task> next, CancellationToken cancellationToken = default);
}
