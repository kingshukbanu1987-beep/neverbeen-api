using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NeverBeen.API.Entities;

/// <summary>A photo in a user's gallery (the "Gallery" section of the profile page).</summary>
[Table("GalleryPhotos")]
public class GalleryPhoto
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public UserProfile? User { get; set; }

    /// <summary>Album the photo belongs to (null = default album).</summary>
    public int? AlbumId { get; set; }

    /// <summary>Photo URL (e.g. Supabase Storage) — alternative to the stored bytes.</summary>
    [MaxLength(1024)]
    public string? Url { get; set; }

    /// <summary>Image bytes (JPEG / PNG / WebP / GIF).</summary>
    public byte[] PhotoData { get; set; } = Array.Empty<byte>();

    [MaxLength(64)]
    public string MimeType { get; set; } = "image/jpeg";

    [MaxLength(300)]
    public string? Caption { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
