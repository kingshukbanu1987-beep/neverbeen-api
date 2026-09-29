using NeverBeen.API.Entities;

namespace NeverBeen.API.Dtos;

public class CircleDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public List<int> MemberIds { get; set; } = new();
    public List<int> AdminIds { get; set; } = new();
    public int OwnerId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ArchivedAtUtc { get; set; }
    public int UnreadCount { get; set; }
}

/// <summary>Body of POST /api/circles and PUT /api/circles/{id}.</summary>
public class SaveCircleRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public string? Color { get; set; }
    public string? PhotoUrl { get; set; }
    public List<int>? MemberIds { get; set; }
}

public class CircleMessageDto
{
    public long Id { get; set; }
    public int CircleId { get; set; }
    public int SenderId { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTime SentAtUtc { get; set; }
    public long? ReplyToMessageId { get; set; }
    public Dictionary<string, int>? Reactions { get; set; }
}

/// <summary>Body of POST /api/circles/{id}/messages.</summary>
public class SendCircleMessageRequest
{
    public string Text { get; set; } = string.Empty;
    public long? ReplyToMessageId { get; set; }
}
