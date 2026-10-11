using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NeverBeen.API.Entities;

/// <summary>
/// A single view of a photograph or clip inside an AI model portfolio, counted once per visitor.
/// </summary>
[Table("ModelMediaViews")]
public class ModelMediaView
{
    public long Id { get; set; }

    [MaxLength(120)]
    public string ModelSlug { get; set; } = string.Empty;

    [MaxLength(16)]
    public string MediaType { get; set; } = string.Empty;

    [MaxLength(512)]
    public string MediaKey { get; set; } = string.Empty;

    public int? ViewerId { get; set; }
    public UserProfile? Viewer { get; set; }

    [MaxLength(64)]
    public string? VisitorToken { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A share event of a photograph or clip, counted once per visitor.
/// </summary>
[Table("ModelMediaShares")]
public class ModelMediaShare
{
    public long Id { get; set; }

    [MaxLength(120)]
    public string ModelSlug { get; set; } = string.Empty;

    [MaxLength(16)]
    public string MediaType { get; set; } = string.Empty;

    [MaxLength(512)]
    public string MediaKey { get; set; } = string.Empty;

    public int? ViewerId { get; set; }
    public UserProfile? Viewer { get; set; }

    [MaxLength(64)]
    public string? VisitorToken { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A member's like on a photograph or clip in an AI model portfolio.
/// </summary>
[Table("ModelMediaLikes")]
public class ModelMediaLike
{
    public long Id { get; set; }

    [MaxLength(120)]
    public string ModelSlug { get; set; } = string.Empty;

    [MaxLength(16)]
    public string MediaType { get; set; } = string.Empty;

    [MaxLength(512)]
    public string MediaKey { get; set; } = string.Empty;

    public int UserId { get; set; }
    public UserProfile? User { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A comment posted by a signed-in Community member on a portfolio photograph or clip.
/// </summary>
[Table("ModelMediaComments")]
public class ModelMediaComment
{
    public long Id { get; set; }

    [MaxLength(120)]
    public string ModelSlug { get; set; } = string.Empty;

    [MaxLength(16)]
    public string MediaType { get; set; } = string.Empty;

    [MaxLength(512)]
    public string MediaKey { get; set; } = string.Empty;

    public int AuthorId { get; set; }
    public UserProfile? Author { get; set; }

    [MaxLength(1000)]
    public string Text { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
