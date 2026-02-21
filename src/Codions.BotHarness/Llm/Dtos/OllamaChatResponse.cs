using System.Text.Json.Serialization;

namespace Codions.BotHarness.Llm.Dtos;

public sealed record OllamaChatResponse
{
    [JsonPropertyName("message")] public OllamaMessageDto? Message { get; set; }

    [JsonPropertyName("prompt_eval_count")]
    [JsonConverter(typeof(NullableLongJsonConverter))]
    public long? PromptEvalCount { get; set; }

    [JsonPropertyName("eval_count")]
    [JsonConverter(typeof(NullableLongJsonConverter))]
    public long? EvalCount { get; set; }

    [JsonPropertyName("prompt_eval_duration")]
    [JsonConverter(typeof(NullableLongJsonConverter))]
    public long? PromptEvalDuration { get; set; }

    [JsonPropertyName("eval_duration")]
    [JsonConverter(typeof(NullableLongJsonConverter))]
    public long? EvalDuration { get; set; }
}