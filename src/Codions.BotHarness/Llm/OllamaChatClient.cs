using System.Net.Http.Json;
using System.Text.Json;
using Codions.BotHarness.Llm.Dtos;

namespace Codions.BotHarness.Llm;

/// <summary>
/// Calls Ollama /api/chat with stream: false and deserializes the response using long? for
/// duration/count fields so we never hit int overflow or float-to-int conversion errors.
/// </summary>
public sealed class OllamaChatClient(HttpClient http) : ILlmChatClient
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        Converters = { new NullableLongJsonConverter() }
    };

    public static void ConfigureHttpClient(HttpClient client, string ollamaBaseUrl)
    {
        client.BaseAddress = new Uri(ollamaBaseUrl.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromMinutes(5);
    }

    /// <summary>Max tokens the model can generate per turn. File edits need substantial output; avoid low server defaults.</summary>
    private const int NumPredict = 8192;

    public async Task<LlmChatResult> SendChatAsync(string model, IReadOnlyList<LlmChatMessage> messages, CancellationToken cancellationToken = default)
    {
        var request = new OllamaChatRequest
        {
            Model = model,
            Stream = false,
            Messages = messages.Select(m => new OllamaMessageDto { Role = m.Role, Content = m.Content }).ToList(),
            Options = new OllamaOptionsDto { NumPredict = NumPredict }
        };

        using var response = await http.PostAsJsonAsync("api/chat", request, _jsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(_jsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Ollama returned empty response.");

        var content = body.Message?.Content ?? "";
        var promptEval = body.PromptEvalCount.HasValue ? (int)Math.Min(body.PromptEvalCount.Value, int.MaxValue) : (int?)null;
        var evalCount = body.EvalCount.HasValue ? (int)Math.Min(body.EvalCount.Value, int.MaxValue) : (int?)null;

        return new LlmChatResult(content, promptEval, evalCount);
    }
}
