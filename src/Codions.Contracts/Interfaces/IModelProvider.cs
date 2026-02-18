using Codions.Contracts.Enums;

namespace Codions.Contracts.Interfaces;

public interface IModelProvider
{
    Task<ModelResponse> SendMessageAsync(
        ModelTier tier,
        string systemPrompt,
        List<ChatMessage> messages,
        CancellationToken ct = default);
}

public sealed record ChatMessage
{
    public required string Role { get; init; }
    public required string Content { get; init; }
}

public sealed record ModelResponse
{
    public required string Content { get; init; }
    public string? StopReason { get; init; }
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
}
