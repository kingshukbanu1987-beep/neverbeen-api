namespace NeverBeen.API.Dtos;

/// <summary>A gallery album with its photos.</summary>
public class GalleryAlbumDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public long? CoverPhotoId { get; set; }

    /// <summary>"public", "companions" or "only-me".</summary>
    public string Privacy { get; set; } = "public";
    public bool IsDefault { get; set; }
    public List<GalleryPhotoDto> Photos { get; set; } = new();
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>Body of POST /api/gallery/albums and PUT /api/gallery/albums/{id}.</summary>
public class SaveGalleryAlbumRequest
{
    public string Name { get; set; } = string.Empty;
    public long? CoverPhotoId { get; set; }
    public string? Privacy { get; set; }
}
