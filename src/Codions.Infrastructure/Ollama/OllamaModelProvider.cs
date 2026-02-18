using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Codions.Contracts.Enums;
using Codions.Contracts.Interfaces;

namespace Codions.Infrastructure.Ollama;

public class OllamaModelProvider(OllamaSettings settings, ILogger<OllamaModelProvider> logger) : IModelProvider
{
    private readonly HttpClient _httpClient = new()
    {
        BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/')),
        Timeout = TimeSpan.FromMinutes(10)
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<ModelResponse> SendMessageAsync(
        ModelTier tier,
        string systemPrompt,
        List<ChatMessage> messages,
        CancellationToken ct = default)
    {
        var modelName = GetModelName(tier);
        logger.LogInformation("Sending message to {Model} (tier: {Tier})", modelName, tier);

        List<OllamaMessage> chatMessages = [new() { Role = "system", Content = systemPrompt }, ..messages.Select(m => new OllamaMessage { Role = m.Role, Content = m.Content })];

        var request = new OllamaRequest
        {
            Model = modelName,
            Messages = chatMessages,
            Stream = false
        };

        var json = JsonSerializer.Serialize(request, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("/v1/chat/completions", content, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Ollama API error ({Status}): {Body}", response.StatusCode, responseBody);
            throw new HttpRequestException($"Ollama API error: {response.StatusCode} - {responseBody}");
        }

        var result = JsonSerializer.Deserialize<OllamaResponse>(responseBody, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize Ollama response");

        var textContent = result.Choices?.FirstOrDefault()?.Message?.Content ?? "";

        logger.LogInformation(
            "Ollama response: {PromptTokens} prompt, {CompletionTokens} completion tokens, finish: {FinishReason}",
            result.Usage?.PromptTokens, result.Usage?.CompletionTokens, result.Choices?.FirstOrDefault()?.FinishReason);

        return new ModelResponse
        {
            Content = textContent,
            StopReason = result.Choices?.FirstOrDefault()?.FinishReason,
            InputTokens = result.Usage?.PromptTokens ?? 0,
            OutputTokens = result.Usage?.CompletionTokens ?? 0
        };
    }

    private string GetModelName(ModelTier tier) => tier switch
    {
        ModelTier.Cheap => settings.Models.Cheap,
        ModelTier.Balanced => settings.Models.Balanced,
        ModelTier.Strong => settings.Models.Strong,
        _ => settings.Models.Balanced
    };
}

public sealed record OllamaSettings
{
    public string BaseUrl { get; init; } = "http://localhost:11434";
    public OllamaModelNames Models { get; init; } = new();
}

public sealed record OllamaModelNames
{
    public string Cheap { get; init; } = "qwen2.5-coder:7b";
    public string Balanced { get; init; } = "qwen2.5-coder:14b";
    public string Strong { get; init; } = "qwen2.5-coder:32b";
}

internal sealed class OllamaRequest
{
    public required string Model { get; set; }
    public required List<OllamaMessage> Messages { get; set; }
    public bool Stream { get; set; } = false;
}

internal sealed class OllamaMessage
{
    public required string Role { get; set; }
    public required string Content { get; set; }
}

internal sealed class OllamaResponse
{
    public List<OllamaChoice>? Choices { get; set; }
    public OllamaUsage? Usage { get; set; }
}

internal sealed class OllamaChoice
{
    public OllamaMessage? Message { get; set; }
    public string? FinishReason { get; set; }
}

internal sealed class OllamaUsage
{
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
}
