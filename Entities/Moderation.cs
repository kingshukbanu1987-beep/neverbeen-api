using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NeverBeen.API.Entities;

/// <summary>A member's report of abusive content (a post, comment or message).</summary>
[Table("AbuseReports")]
public class AbuseReport
{
    public long Id { get; set; }

    /// <summary>"post", "comment" or "message".</summary>
    [MaxLength(20)]
    public string TargetType { get; set; } = "post";

    public long TargetId { get; set; }

    /// <summary>Author of the reported content.</summary>
    public int ReportedAuthorId { get; set; }

    public int ReportedByUserId { get; set; }

    [MaxLength(200)]
    public string Reason { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Details { get; set; }

    [MaxLength(256)]
    public string? ReporterEmail { get; set; }

    /// <summary>"pending" or "reviewed".</summary>
    [MaxLength(20)]
    public string Status { get; set; } = "pending";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>A member blocking another traveler (mutual invisibility).</summary>
[Table("BlockedUsers")]
public class BlockedUser
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public int BlockedUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>A Journey post a member hid from their own feed.</summary>
[Table("HiddenPosts")]
public class HiddenPost
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public long PostId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
