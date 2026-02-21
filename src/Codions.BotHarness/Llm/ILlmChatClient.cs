namespace Codions.BotHarness.Llm;

/// <summary>
/// Abstraction for sending chat requests to an LLM (e.g. Ollama) and receiving content + optional token counts.
/// </summary>
public interface ILlmChatClient
{
    /// <summary>
    /// Sends a chat request and returns the assistant message content and optional usage.
    /// </summary>
    Task<LlmChatResult> SendChatAsync(string model, IReadOnlyList<LlmChatMessage> messages, CancellationToken cancellationToken = default);
}

/// <summary>
/// A single message in a chat (role + content).
/// </summary>
/// <param name="Role">e.g. "system", "user", "assistant"</param>
/// <param name="Content">Message text</param>
public sealed record LlmChatMessage(string Role, string Content);

/// <summary>
/// Result of a chat call: assistant content and optional token counts.
/// </summary>
/// <param name="Content">Assistant reply text</param>
/// <param name="PromptEvalCount">Input tokens (if provided by API)</param>
/// <param name="EvalCount">Output tokens (if provided by API)</param>
public sealed record LlmChatResult(string Content, int? PromptEvalCount, int? EvalCount);
