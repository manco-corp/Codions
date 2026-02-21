using System.Text.Json.Serialization;

namespace Codions.BotHarness.Llm.Dtos;

public sealed record OllamaOptionsDto
{
    [JsonPropertyName("num_predict")]
    public int NumPredict { get; set; }
} 