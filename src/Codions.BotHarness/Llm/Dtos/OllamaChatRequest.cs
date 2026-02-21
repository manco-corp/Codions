namespace Codions.BotHarness.Llm.Dtos;

public sealed record OllamaChatRequest
{
    public string Model { get; set; } = "";
    public bool Stream { get; set; }
    public List<OllamaMessageDto> Messages { get; set; } = [];
    public OllamaOptionsDto? Options { get; set; }
}