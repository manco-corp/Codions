namespace Codions.ChatAdapter;

/// <summary>
/// Builds Google Chat JSON reply messages (text or card) for success and failure.
/// </summary>
public static class ChatReplyBuilder
{
    public static ChatReplyMessage Success(string jobId, string? statusUrl = null)
    {
        var text = statusUrl is not null
            ? $"Job created: `{jobId}`. Status: {statusUrl}"
            : $"Job created: `{jobId}`. Check status via GET /api/jobs/{jobId}";
        return new ChatReplyMessage { Text = text };
    }

    public static ChatReplyMessage Error(string message)
    {
        var safe = message.Length > 500 ? message[..500] + "…" : message;
        return new ChatReplyMessage { Text = $"Could not create job: {safe}" };
    }

    public static ChatReplyMessage Help()
    {
        return new ChatReplyMessage { Text = JobRequestParser.GetUsageHelp() };
    }

    public static ChatReplyMessage Welcome()
    {
        return new ChatReplyMessage
        {
            Text = "Minions bot is here. " + JobRequestParser.GetUsageHelp()
        };
    }
}
