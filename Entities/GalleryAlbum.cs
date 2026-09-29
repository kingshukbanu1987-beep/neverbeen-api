using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NeverBeen.API.Entities;

/// <summary>A gallery album ("Gallery" section of the profile page).</summary>
[Table("GalleryAlbums")]
public class GalleryAlbum
{
    public int Id { get; set; }

    public int UserId { get; set; }

    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gallery photo shown as the album cover (soft reference).</summary>
    public long? CoverPhotoId { get; set; }

    /// <summary>"public", "companions" or "only-me".</summary>
    [MaxLength(20)]
    public string Privacy { get; set; } = "public";

    public bool IsDefault { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>A companion tagged in a Message Book post.</summary>
[Table("CommunityCommentTags")]
public class CommunityCommentTag
{
    public int Id { get; set; }
    public int CommentId { get; set; }
    public int UserId { get; set; }
}
