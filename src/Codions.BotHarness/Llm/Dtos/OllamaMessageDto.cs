using System.Text.Json.Serialization;

namespace Codions.BotHarness.Llm.Dtos;

public sealed record OllamaMessageDto
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";
}