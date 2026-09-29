using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NeverBeen.API.Entities;

/// <summary>
/// A comment on a Journey post. <see cref="ParentId"/> == null makes it a top-level
/// comment; otherwise it is a reply to that comment (unlimited nesting).
/// </summary>
[Table("JourneyComments")]
public class JourneyComment
{
    public long Id { get; set; }

    public long PostId { get; set; }

    public int AuthorId { get; set; }

    /// <summary>Id of the comment this entry replies to (null for top-level comments).</summary>
    public long? ParentId { get; set; }

    [MaxLength(2000)]
    public string Text { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string? ImageUrl { get; set; }

    public int LikeCount { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>One reaction per member per Journey comment.</summary>
[Table("JourneyCommentReactions")]
public class JourneyCommentReaction
{
    public long Id { get; set; }
    public long CommentId { get; set; }
    public int UserId { get; set; }

    /// <summary>One of <see cref="JourneyReactionTypes"/>.</summary>
    [MaxLength(20)]
    public string ReactionType { get; set; } = JourneyReactionTypes.Like;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
