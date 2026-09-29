namespace NeverBeen.API.Dtos;

/// <summary>A messenger thread as shown in the Messenger panel.</summary>
public class ConversationDto
{
    public long Id { get; set; }
    public bool IsGroup { get; set; }
    public int? CircleId { get; set; }
    public int? OwnerId { get; set; }
    public List<AuthorDto> Participants { get; set; } = new();
    public ChatMessageDto? LastMessage { get; set; }
    public int UnreadCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class ChatMessageDto
{
    public long Id { get; set; }
    public long ConversationId { get; set; }
    public int SenderId { get; set; }
    public string SenderName { get; set; } = string.Empty;

    /// <summary>Receiver of a 1:1 message (null in group chats).</summary>
    public int? ReceiverId { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime SentAtUtc { get; set; }
    public long? ReplyToMessageId { get; set; }
    public ChatMessageReplyDto? ReplyTo { get; set; }
    public Dictionary<string, int>? Reactions { get; set; }
    public string? MyReaction { get; set; }
}

/// <summary>Snippet of the message a chat message replies to.</summary>
public class ChatMessageReplyDto
{
    public long Id { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

/// <summary>Body of POST /api/messages/conversations — start (or reopen) a chat.</summary>
public class StartConversationRequest
{
    /// <summary>The companion to chat with (1:1) — ignored for group chats.</summary>
    public int? CompanionId { get; set; }

    /// <summary>Group chat members (including the creator).</summary>
    public List<int>? ParticipantIds { get; set; }
    public bool IsGroup { get; set; }
    public int? CircleId { get; set; }
}

/// <summary>Body of POST /api/messages/conversations/{id}/messages.</summary>
public class SendMessageRequest
{
    public string Text { get; set; } = string.Empty;
    public long? ReplyToMessageId { get; set; }
}

/// <summary>Body of POST /api/messages/{id}/reactions.</summary>
public class MessageReactionRequest
{
    /// <summary>Emoji key, e.g. "👍". Each submit increments that emoji's counter (multi-user tally).</summary>
    public string Emoji { get; set; } = string.Empty;
}
