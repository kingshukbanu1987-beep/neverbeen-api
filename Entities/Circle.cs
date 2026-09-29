using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NeverBeen.API.Entities;

/// <summary>A travel circle: a named group of members with its own group chat.</summary>
[Table("Circles")]
public class Circle
{
    public int Id { get; set; }

    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(60)]
    public string? Icon { get; set; }

    [MaxLength(20)]
    public string? Color { get; set; }

    /// <summary>Cover photo (upload or stock travel image).</summary>
    [MaxLength(1024)]
    public string? PhotoUrl { get; set; }

    /// <summary>Member who created the Circle (always an admin).</summary>
    public int OwnerId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Set when an admin deletes the Circle; archived circles stay recoverable.</summary>
    public DateTime? ArchivedAtUtc { get; set; }
}

/// <summary>Membership of a member in a Circle.</summary>
[Table("CircleMembers")]
public class CircleMember
{
    public long Id { get; set; }
    public int CircleId { get; set; }
    public int UserId { get; set; }
    public bool IsAdmin { get; set; }
    public DateTime JoinedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>A message in a Circle's group chat.</summary>
[Table("CircleMessages")]
public class CircleMessage
{
    public long Id { get; set; }
    public int CircleId { get; set; }
    public int SenderId { get; set; }

    [MaxLength(2000)]
    public string Text { get; set; } = string.Empty;

    public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Message this one replies to (message replies in the group chat).</summary>
    public long? ReplyToMessageId { get; set; }

    /// <summary>JSON map of emoji to count, e.g. {"👍": 2, "🔥": 1}.</summary>
    public string? Reactions { get; set; }
}
