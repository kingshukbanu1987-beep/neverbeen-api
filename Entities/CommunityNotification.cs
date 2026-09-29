using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NeverBeen.API.Entities;

/// <summary>A notification shown on the member's Notifications page.</summary>
[Table("Notifications")]
public class CommunityNotification
{
    public long Id { get; set; }

    /// <summary>The member who receives the notification.</summary>
    public int UserId { get; set; }

    /// <summary>One of <see cref="NotificationTypes"/>.</summary>
    [MaxLength(40)]
    public string Type { get; set; } = NotificationTypes.JourneyLike;

    /// <summary>The member who caused the notification.</summary>
    public int FromUserId { get; set; }

    [MaxLength(500)]
    public string Message { get; set; } = string.Empty;

    public bool IsRead { get; set; }

    /// <summary>Companionship id / Journey post id the notification refers to.</summary>
    public long? RequestId { get; set; }

    /// <summary>"pending", "approved" or "rejected" (companionship requests).</summary>
    [MaxLength(20)]
    public string? Status { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public static class NotificationTypes
{
    public const string CompanionshipRequest = "companionship_request";
    public const string CompanionshipAccepted = "companionship_accepted";
    public const string JourneyLike = "journey_like";
    public const string JourneyComment = "journey_comment";
    public const string Followed = "followed";
    public const string Tagged = "tagged";
    public const string MessageBookReply = "messagebook_reply";
}
