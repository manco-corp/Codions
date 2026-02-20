using System.Text.Json;
using System.Text.Json.Serialization;
using Codions.Contracts.Models;
using Codions.ChatAdapter;

var builder = WebApplication.CreateBuilder(args);

var codionsSection = builder.Configuration.GetSection("CodionsApi");
var baseUrl = codionsSection.GetValue<string>("BaseUrl")?.TrimEnd('/')
              ?? throw new InvalidOperationException("CodionsApi:BaseUrl is required.");
builder.Services.AddHttpClient<ICodionsApiClient, CodionsApiClient>(client =>
{
    client.BaseAddress = new Uri(baseUrl);
    client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
});

var app = builder.Build();

var verificationToken = builder.Configuration.GetValue<string>("GoogleChat:VerificationToken");
var codionsBaseUrlForLinks = codionsSection.GetValue<string>("BaseUrl")?.TrimEnd('/');

// JSON options for Google Chat: property names as-is for outgoing reply
var chatJsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/webhook", async (HttpRequest req, ICodionsApiClient codionsClient, CancellationToken ct) =>
{
    string body;
    try
    {
        body = await req.ReadRequestBodyAsStringAsync();
    }
    catch
    {
        return Results.Json(ChatReplyBuilder.Error("Could not read request body."), chatJsonOptions, statusCode: 200);
    }

    if (string.IsNullOrWhiteSpace(body))
        return Results.Json(ChatReplyBuilder.Error("Empty body."), chatJsonOptions, statusCode: 200);

    // Detect payload format: Add-on (slash command) has "chat" with "appCommandPayload"
    bool isAddOnFormat = false;
    try
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        isAddOnFormat = root.TryGetProperty("chat", out var chatEl) &&
                        chatEl.TryGetProperty("appCommandPayload", out _);
    }
    catch (JsonException)
    {
        return Results.Json(ChatReplyBuilder.Error("Invalid JSON body."), chatJsonOptions, statusCode: 200);
    }

    string? messageText = null;
    RequesterInfo? requester = null;

    if (isAddOnFormat)
    {
        var addOnPayload = JsonSerializer.Deserialize<GoogleChatAddOnWebhookPayload>(body, chatJsonOptions);
        if (addOnPayload?.Chat?.AppCommandPayload?.Message is null)
            return Results.Json(
                ChatReplyBuilder.Error("Invalid add-on payload: missing chat.appCommandPayload.message."),
                chatJsonOptions, statusCode: 200);

        var msg = addOnPayload.Chat.AppCommandPayload.Message;
        messageText = msg.ArgumentText?.Trim() ?? msg.Text?.Trim();
        var user = addOnPayload.Chat.User ?? msg.Sender;
        requester = new RequesterInfo
        {
            Id = user?.Name ?? "unknown",
            DisplayName = user?.DisplayName ?? "Unknown",
            Email = user?.Email
        };
    }
    else
    {
        var eventPayload = JsonSerializer.Deserialize<GoogleChatEvent>(body, chatJsonOptions);
        if (eventPayload is null)
            return Results.Json(ChatReplyBuilder.Error("Empty or unknown event payload."), chatJsonOptions,
                statusCode: 200);

        if (!string.IsNullOrEmpty(verificationToken) &&
            !string.Equals(eventPayload.Token, verificationToken, StringComparison.Ordinal))
            return Results.Unauthorized();

        var type = eventPayload.Type ?? "";
        if (string.Equals(type, "ADDED_TO_SPACE", StringComparison.OrdinalIgnoreCase))
            return Results.Json(ChatReplyBuilder.Welcome(), chatJsonOptions, statusCode: 200);
        if (!string.Equals(type, "MESSAGE", StringComparison.OrdinalIgnoreCase))
            return Results.Json(ChatReplyBuilder.Help(), chatJsonOptions, statusCode: 200);

        messageText = eventPayload.Message?.ArgumentText?.Trim() ?? eventPayload.Message?.Text?.Trim();
        var user = eventPayload.User;
        requester = new RequesterInfo
        {
            Id = user?.Name ?? "unknown",
            DisplayName = user?.DisplayName ?? "Unknown",
            Email = user?.Email
        };
    }

    if (string.IsNullOrWhiteSpace(messageText))
        return Results.Json(ChatReplyBuilder.Help(), chatJsonOptions, statusCode: 200);
    if (requester is null)
        return Results.Json(ChatReplyBuilder.Error("Could not determine requester."), chatJsonOptions, statusCode: 200);

    var (ok, jobRequest, parseError) = JobRequestParser.TryParse(messageText, requester);
    if (!ok || jobRequest is null)
        return Results.Json(ChatReplyBuilder.Error(parseError ?? "Parse failed."), chatJsonOptions, statusCode: 200);

    var (success, jobId, apiError) = await codionsClient.CreateJobAsync(jobRequest, ct);
    if (!success)
        return Results.Json(ChatReplyBuilder.Error(apiError ?? "Unknown error."), chatJsonOptions, statusCode: 200);

    var statusUrl = jobId is not null && !string.IsNullOrEmpty(codionsBaseUrlForLinks)
        ? $"{codionsBaseUrlForLinks}/api/jobs/{jobId}"
        : null;
    return Results.Json(ChatReplyBuilder.Success(jobId!, statusUrl), chatJsonOptions, statusCode: 200);
});

app.Run();