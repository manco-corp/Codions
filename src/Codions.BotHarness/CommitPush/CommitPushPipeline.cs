namespace Codions.BotHarness.CommitPush;

/// <summary>
/// Builds and runs the commit-and-push chain of responsibility:
/// AddAll -> SanitizeSecrets -> Commit -> Push.
/// </summary>
public sealed class CommitPushPipeline
{
    private readonly ICommitPushHandler _addAll;
    private readonly Func<CommitPushContext, CancellationToken, Task> _addAllNext;

    public CommitPushPipeline()
    {
        var addAll = new AddAllHandler();
        var sanitize = new SanitizeSecretsHandler();
        var commit = new CommitHandler();
        var push = new PushHandler();
 
        Func<CommitPushContext, CancellationToken, Task> pushNext = (_, __) => Task.CompletedTask;
        var commitNext = (CommitPushContext ctx, CancellationToken ct) => push.HandleAsync(ctx, pushNext, ct);
        var sanitizeNext = (CommitPushContext ctx, CancellationToken ct) => commit.HandleAsync(ctx, commitNext, ct);
   
        _addAll = addAll;
        _addAllNext = (ctx, ct) => sanitize.HandleAsync(ctx, sanitizeNext, ct);
    }

    public async Task ExecuteAsync(CommitPushContext context, CancellationToken cancellationToken = default)
    {
        Console.WriteLine("[BotHarness] Step 4: Committing and pushing...");
        await _addAll.HandleAsync(context, _addAllNext, cancellationToken);
    }
}
