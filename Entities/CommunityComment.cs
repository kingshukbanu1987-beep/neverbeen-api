using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NeverBeen.API.Entities;

/// <summary>
/// An entry in the NeverBeen Community Message Book.
/// A comment with <see cref="ParentId"/> == null is a top-level post;
/// a comment with a <see cref="ParentId"/> is a reply to that post (one level of nesting).
/// </summary>
[Table("CommunityComments")]
public class CommunityComment
{
    public int Id { get; set; }

    /// <summary>The comment text.</summary>
    [MaxLength(2000)]
    public string Text { get; set; } = string.Empty;

    public int AuthorId { get; set; }
    public UserProfile? Author { get; set; }

    /// <summary>Optional photo attached to the post.</summary>
    [MaxLength(1024)]
    public string? ImageUrl { get; set; }

    /// <summary>Id of the top-level comment this entry replies to (null for top-level posts).</summary>
    public int? ParentId { get; set; }
    public CommunityComment? Parent { get; set; }
    public ICollection<CommunityComment> Replies { get; set; } = new List<CommunityComment>();

    /// <summary>Denormalized counters maintained with each reaction (like/dislike).</summary>
    public int LikeCount { get; set; }
    public int DislikeCount { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
