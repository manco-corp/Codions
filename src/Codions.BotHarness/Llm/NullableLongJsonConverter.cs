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
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.Number)
        {
            if (reader.TryGetInt64(out var i64))
                return i64;
            if (reader.TryGetDouble(out var d))
                return (long)Math.Round(d);
        }

        throw new JsonException($"Unexpected token {reader.TokenType} for long?.");
    }

    public override void Write(Utf8JsonWriter writer, long? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteNumberValue(value.Value);
        else
            writer.WriteNullValue();
    }
}
