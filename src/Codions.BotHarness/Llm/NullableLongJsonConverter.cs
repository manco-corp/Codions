using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codions.BotHarness.Llm;

/// <summary>
/// Deserializes JSON number (int, long, or float) to long? so Ollama's prompt_eval_duration (nanoseconds)
/// and similar fields never overflow or fail when the server sends a float.
/// </summary>
internal sealed class NullableLongJsonConverter : JsonConverter<long?>
{
    public override long? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.Number when reader.TryGetInt64(out var i64) => i64,
            JsonTokenType.Number when reader.TryGetDouble(out var d) => (long)Math.Round(d),
            _ => throw new JsonException($"Unexpected token {reader.TokenType} for long?.")
        };
    }

    public override void Write(Utf8JsonWriter writer, long? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteNumberValue(value.Value);
        else
            writer.WriteNullValue();
    }
}
