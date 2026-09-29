namespace NeverBeen.API.Dtos;

public class GalleryPhotoDto
{
    public int Id { get; set; }

    /// <summary>Relative API URL to download the image, e.g. "/api/gallery/12".</summary>
    public string Url { get; set; } = string.Empty;

    public string? Caption { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
