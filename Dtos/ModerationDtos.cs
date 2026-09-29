namespace NeverBeen.API.Dtos;

/// <summary>An abuse report filed against a post, comment or message.</summary>
public class AbuseReportDto
{
    public long Id { get; set; }

    /// <summary>"post", "comment" or "message".</summary>
    public string TargetType { get; set; } = "post";
    public long TargetId { get; set; }
    public AuthorDto ReportedAuthor { get; set; } = new();
    public int ReportedByUserId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string? ReporterEmail { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>"pending" or "reviewed".</summary>
    public string Status { get; set; } = "pending";
}

/// <summary>Body of POST /api/moderation/reports.</summary>
public class CreateAbuseReportRequest
{
    public string TargetType { get; set; } = "post";
    public long TargetId { get; set; }
    public int ReportedAuthorId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string? ReporterEmail { get; set; }
}

/// <summary>A traveler the member has blocked.</summary>
public class BlockedUserDto
{
    public int Id { get; set; }
    public string? UniqueId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string ProfilePhotoUrl { get; set; } = string.Empty;
    public DateTime BlockedAtUtc { get; set; }
}
