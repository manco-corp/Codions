using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codions.BotHarness;

/// <summary>
/// Lightweight Ollama client using the OpenAI-compatible chat completions endpoint.
/// Runs inside the bot container -- fully self-contained, no Infrastructure dependency.
/// </summary>
public class OllamaClient
{
    private readonly HttpClient _http;
    private readonly string _model;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OllamaClient(string baseUrl, string model)
    {
        _model = model;
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/')),
            Timeout = TimeSpan.FromMinutes(10)
        };
    }

    public async Task<string> SendAsync(string systemPrompt, List<ConversationMessage> messages)
    {
        List<object> chatMessages = [new { role = "system", content = systemPrompt }, ..messages.Select(m => new { role = m.Role, content = m.Content })];

        var request = new
        {
            model = _model,
            messages = chatMessages,
            stream = false
        };

        var json = JsonSerializer.Serialize(request, JsonOpts);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync("/v1/chat/completions", content);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"[OllamaClient] API error {response.StatusCode}: {body}");
            throw new HttpRequestException($"Ollama API error: {response.StatusCode}");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (root.TryGetProperty("choices", out var choices))
        {
            foreach (var choice in choices.EnumerateArray())
            {
                if (choice.TryGetProperty("message", out var msg) &&
                    msg.TryGetProperty("content", out var text))
                {
                    var promptTokens = 0;
                    var completionTokens = 0;
                    if (root.TryGetProperty("usage", out var usage))
                    {
                        if (usage.TryGetProperty("prompt_tokens", out var pt))
                            promptTokens = pt.GetInt32();
                        if (usage.TryGetProperty("completion_tokens", out var ct))
                            completionTokens = ct.GetInt32();
                    }

                    Console.WriteLine($"[OllamaClient] Tokens: {promptTokens} in / {completionTokens} out");
                    return text.GetString() ?? "";
                }
            }
        }

        throw new InvalidOperationException("No content in Ollama response");
    }
}
