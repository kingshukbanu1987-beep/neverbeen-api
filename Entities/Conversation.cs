using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NeverBeen.API.Entities;

/// <summary>
/// A messenger thread: a 1:1 conversation between two members or a group chat
/// (optionally backed by a Circle).
/// </summary>
[Table("Conversations")]
public class Conversation
{
    public long Id { get; set; }
    public bool IsGroup { get; set; }

    /// <summary>Set when the conversation is a Circle's group chat.</summary>
    public int? CircleId { get; set; }

    /// <summary>Member who started the chat / owns the group.</summary>
    public int? OwnerId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>A member taking part in a conversation (drives unread counts).</summary>
[Table("ConversationParticipants")]
public class ConversationParticipant
{
    public long Id { get; set; }
    public long ConversationId { get; set; }
    public int UserId { get; set; }

    /// <summary>Newest message the member has read; messages after it are unread.</summary>
    public long? LastReadMessageId { get; set; }

    public DateTime JoinedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>A chat message in a conversation.</summary>
[Table("ChatMessages")]
public class ChatMessage
{
    public long Id { get; set; }
    public long ConversationId { get; set; }
    public int SenderId { get; set; }

    [MaxLength(2000)]
    public string Text { get; set; } = string.Empty;

    public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Message this one replies to.</summary>
    public long? ReplyToMessageId { get; set; }

    /// <summary>JSON map of emoji to count, e.g. {"👍": 2}.</summary>
    public string? Reactions { get; set; }
}
