using System.Text.Json.Serialization;

namespace Codions.ChatAdapter;

/// <summary>
/// Incoming Google Chat interaction event.
/// See https://developers.google.com/workspace/chat/api/reference/rest/v1/Event
/// </summary>
public sealed class GoogleChatEvent
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("eventTime")]
    public string? EventTime { get; set; }

    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("message")]
    public ChatMessagePayload? Message { get; set; }

    [JsonPropertyName("user")]
    public ChatUser? User { get; set; }

    [JsonPropertyName("space")]
    public ChatSpace? Space { get; set; }

    [JsonPropertyName("common")]
    public CommonEventObject? Common { get; set; }
}

public sealed class ChatMessagePayload
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("argumentText")]
    public string? ArgumentText { get; set; }
}

public sealed class ChatUser
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }
}

public sealed class ChatSpace
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }
}

/// <summary>
/// For CARD_CLICKED / form submit; contains formInputs with widget values.
/// </summary>
public sealed class CommonEventObject
{
    [JsonPropertyName("formInputs")]
    public Dictionary<string, FormInputValue>? FormInputs { get; set; }
}

// --- Google Chat Add-on / Slash-command webhook payload ---
// Root body when Chat sends events to app webhook (e.g. /sbot command).
// See: https://developers.google.com/workspace/chat/add-ons/reference/rest/v1/Event

/// <summary>
/// Root payload for Google Chat Add-on webhook (slash commands, etc.).
/// Contains commonEventObject, authorizationEventObject, and chat with appCommandPayload.
/// </summary>
public sealed class GoogleChatAddOnWebhookPayload
{
    [JsonPropertyName("commonEventObject")]
    public AddOnCommonEventObject? CommonEventObject { get; set; }

    [JsonPropertyName("authorizationEventObject")]
    public AuthorizationEventObject? AuthorizationEventObject { get; set; }

    [JsonPropertyName("chat")]
    public AddOnChat? Chat { get; set; }
}

public sealed class AddOnCommonEventObject
{
    [JsonPropertyName("userLocale")]
    public string? UserLocale { get; set; }

    [JsonPropertyName("hostApp")]
    public string? HostApp { get; set; }

    [JsonPropertyName("platform")]
    public string? Platform { get; set; }

    [JsonPropertyName("timeZone")]
    public AddOnTimeZone? TimeZone { get; set; }
}

public sealed class AddOnTimeZone
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("offset")]
    public double? Offset { get; set; }
}

public sealed class AuthorizationEventObject
{
    [JsonPropertyName("systemIdToken")]
    public string? SystemIdToken { get; set; }
}

public sealed class AddOnChat
{
    [JsonPropertyName("user")]
    public ChatUser? User { get; set; }

    [JsonPropertyName("eventTime")]
    public string? EventTime { get; set; }

    [JsonPropertyName("appCommandPayload")]
    public AppCommandPayload? AppCommandPayload { get; set; }
}

public sealed class AppCommandPayload
{
    [JsonPropertyName("appCommandMetadata")]
    public AppCommandMetadata? AppCommandMetadata { get; set; }

    [JsonPropertyName("space")]
    public ChatSpace? Space { get; set; }

    [JsonPropertyName("message")]
    public AddOnMessage? Message { get; set; }

    [JsonPropertyName("configCompleteRedirectUri")]
    public string? ConfigCompleteRedirectUri { get; set; }
}

public sealed class AppCommandMetadata
{
    [JsonPropertyName("appCommandId")]
    public double? AppCommandId { get; set; }

    [JsonPropertyName("appCommandType")]
    public string? AppCommandType { get; set; }
}

public sealed class AddOnMessage
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("sender")]
    public ChatUser? Sender { get; set; }

    [JsonPropertyName("createTime")]
    public string? CreateTime { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("argumentText")]
    public string? ArgumentText { get; set; }

    [JsonPropertyName("thread")]
    public MessageThread? Thread { get; set; }

    [JsonPropertyName("space")]
    public ChatSpace? Space { get; set; }

    [JsonPropertyName("formattedText")]
    public string? FormattedText { get; set; }
}

public sealed class MessageThread
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public sealed class FormInputValue
{
    [JsonPropertyName("stringInputs")]
    public StringInputs? StringInputs { get; set; }
}

public sealed class StringInputs
{
    [JsonPropertyName("value")]
    public List<string>? Value { get; set; }
}

// --- Outgoing (reply) models ---

/// <summary>
/// Response body for webhook: a Chat message (text or card).
/// See https://developers.google.com/workspace/chat/format-messages
/// </summary>
public sealed class ChatReplyMessage
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("cardsV2")]
    public List<CardWithId>? CardsV2 { get; set; }
}

public sealed class CardWithId
{
    [JsonPropertyName("cardId")]
    public string? CardId { get; set; }

    [JsonPropertyName("card")]
    public CardContent? Card { get; set; }
}

public sealed class CardContent
{
    [JsonPropertyName("sections")]
    public List<Section>? Sections { get; set; }
}

public sealed class Section
{
    [JsonPropertyName("header")]
    public string? Header { get; set; }

    [JsonPropertyName("widgets")]
    public List<Widget>? Widgets { get; set; }
}

public sealed class Widget
{
    [JsonPropertyName("textParagraph")]
    public TextParagraph? TextParagraph { get; set; }

    [JsonPropertyName("decoratedText")]
    public DecoratedText? DecoratedText { get; set; }
}

public sealed class TextParagraph
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

public sealed class DecoratedText
{
    [JsonPropertyName("topLabel")]
    public string? TopLabel { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}
