using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NeverBeen.API.Common;
using NeverBeen.API.Data;
using NeverBeen.API.Dtos;
using NeverBeen.API.Entities;

namespace NeverBeen.API.Controllers;

/// <summary>Gallery albums: named photo collections with privacy and a cover photo.</summary>
[Route("api/gallery/albums")]
[ApiController]
[Authorize]
public class GalleryAlbumsController : ControllerBase
{
    private readonly AppDbContext _db;

    public GalleryAlbumsController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>Albums of the signed-in member with their photos.</summary>
    [HttpGet]
    public async Task<ActionResult<List<GalleryAlbumDto>>> GetAlbums(CancellationToken cancellationToken = default)
    {
        var myId = User.GetUserId();
        var albums = await _db.GalleryAlbums.AsNoTracking()
            .Where(a => a.UserId == myId)
            .OrderBy(a => a.IsDefault ? 0 : 1)
            .ThenByDescending(a => a.UpdatedAtUtc)
            .ToListAsync(cancellationToken);

        var photos = await _db.GalleryPhotos.AsNoTracking()
            .Where(p => p.UserId == myId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        var byAlbum = photos.GroupBy(p => p.AlbumId).ToDictionary(g => g.Key, g => g.ToList());

        return Ok(albums.Select(a =>
        {
            byAlbum.TryGetValue(a.Id, out var rows);
            rows ??= new List<GalleryPhoto>();
            return new GalleryAlbumDto
            {
                Id = a.Id,
                Name = a.Name,
                CoverPhotoId = a.CoverPhotoId,
                Privacy = a.Privacy,
                IsDefault = a.IsDefault,
                UpdatedAtUtc = a.UpdatedAtUtc,
                Photos = rows.Select(ToPhotoDto).ToList()
            };
        }).ToList());
    }

    /// <summary>Creates an album.</summary>
    [HttpPost]
    public async Task<ActionResult<GalleryAlbumDto>> Create([FromBody] SaveGalleryAlbumRequest request, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Album name is required." });

        var album = new GalleryAlbum
        {
            UserId = myId,
            Name = request.Name.Trim(),
            CoverPhotoId = request.CoverPhotoId,
            Privacy = request.Privacy ?? "public"
        };
        _db.GalleryAlbums.Add(album);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new GalleryAlbumDto
        {
            Id = album.Id,
            Name = album.Name,
            CoverPhotoId = album.CoverPhotoId,
            Privacy = album.Privacy,
            IsDefault = album.IsDefault,
            UpdatedAtUtc = album.UpdatedAtUtc
        });
    }

    /// <summary>Renames an album / changes its privacy or cover.</summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult<GalleryAlbumDto>> Update(int id, [FromBody] SaveGalleryAlbumRequest request, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        var album = await _db.GalleryAlbums
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == myId, cancellationToken);
        if (album == null)
            return NotFound();

        if (!string.IsNullOrWhiteSpace(request.Name)) album.Name = request.Name.Trim();
        if (request.CoverPhotoId.HasValue) album.CoverPhotoId = request.CoverPhotoId;
        if (!string.IsNullOrWhiteSpace(request.Privacy)) album.Privacy = request.Privacy;
        album.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new GalleryAlbumDto
        {
            Id = album.Id,
            Name = album.Name,
            CoverPhotoId = album.CoverPhotoId,
            Privacy = album.Privacy,
            IsDefault = album.IsDefault,
            UpdatedAtUtc = album.UpdatedAtUtc
        });
    }

    /// <summary>Deletes an album (photos move back to the default album).</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        var album = await _db.GalleryAlbums
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == myId, cancellationToken);
        if (album == null)
            return NotFound();

        var photos = await _db.GalleryPhotos
            .Where(p => p.AlbumId == id)
            .ToListAsync(cancellationToken);
        foreach (var photo in photos)
            photo.AlbumId = null;

        _db.GalleryAlbums.Remove(album);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Moves a photo into an album (albumId omitted = back to default).</summary>
    [HttpPut("{id:int}/photos/{photoId:int}")]
    public async Task<IActionResult> MovePhoto(int id, int photoId, [FromQuery] int? albumId = null, CancellationToken cancellationToken = default)
    {
        var myId = User.GetUserId();
        var photo = await _db.GalleryPhotos
            .FirstOrDefaultAsync(p => p.Id == photoId && p.UserId == myId, cancellationToken);
        if (photo == null)
            return NotFound();

        photo.AlbumId = id == 0 ? albumId : id;
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static GalleryPhotoDto ToPhotoDto(GalleryPhoto p) => new()
    {
        Id = p.Id,
        Url = !string.IsNullOrWhiteSpace(p.Url) ? p.Url! : $"/api/gallery/{p.Id}",
        Caption = p.Caption,
        CreatedAtUtc = p.CreatedAtUtc
    };
}
