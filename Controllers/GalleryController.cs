using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NeverBeen.API.Common;
using NeverBeen.API.Data;
using NeverBeen.API.Dtos;
using NeverBeen.API.Entities;

namespace NeverBeen.API.Controllers;

/// <summary>
/// Photo gallery endpoints (the "Gallery" section of the profile page).
/// Photos are stored in the database; GET /api/gallery/{id} serves the image bytes.
/// </summary>
[ApiController]
[Route("api/gallery")]
[Authorize]
public class GalleryController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<GalleryController> _logger;

    public GalleryController(AppDbContext db, ILogger<GalleryController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>The signed-in user's gallery photos.</summary>
    [HttpGet]
    public async Task<ActionResult<List<GalleryPhotoDto>>> Mine(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        return Ok(await QueryGalleryAsync(userId, cancellationToken));
    }

    /// <summary>Adds a photo to the signed-in user's gallery (multipart: "photo" file + optional "caption").</summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(GalleryPhotoDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<GalleryPhotoDto>> Add([FromForm] IFormFile photo, [FromForm] string? caption, CancellationToken cancellationToken)
    {
        var error = ImageValidation.ValidateRequired(photo, AppConstants.MaxGalleryPhotoBytes);
        if (error != null)
            return BadRequest(new { error });

        using var memory = new MemoryStream();
        await photo.CopyToAsync(memory, cancellationToken);

        var entity = new GalleryPhoto
        {
            UserId = User.GetUserId(),
            PhotoData = memory.ToArray(),
            MimeType = photo.ContentType ?? "image/jpeg",
            Caption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.GalleryPhotos.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetPhoto), new { id = entity.Id }, Map(entity.Id, entity.Caption, entity.CreatedAtUtc));
    }

    /// <summary>Deletes one of the signed-in user's photos.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var photo = await _db.GalleryPhotos
            .FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
        if (photo == null)
            return NotFound();
        if (photo.UserId != User.GetUserId())
            return Forbid();

        _db.GalleryPhotos.Remove(photo);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Serves a gallery photo as an image (public when the owner allows a public profile).</summary>
    [AllowAnonymous]
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetPhoto(int id, CancellationToken cancellationToken)
    {
        var photo = await _db.GalleryPhotos
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
        if (photo == null)
            return NotFound();

        var requesterId = User.Identity?.IsAuthenticated == true ? User.GetUserId() : null;
        if (requesterId != photo.UserId)
        {
            var isPublic = await _db.UserSettings
                .AsNoTracking()
                .Where(s => s.UserId == photo.UserId)
                .Select(s => (bool?)s.PublicProfileEnabled)
                .FirstOrDefaultAsync(cancellationToken) ?? true;
            if (!isPublic)
                return Forbid();
        }

        return File(photo.PhotoData, photo.MimeType, $"photo-{id}{GetExtension(photo.MimeType)}");
    }

    /// <summary>List of another member's gallery photos (public when the owner allows it).</summary>
    [AllowAnonymous]
    [HttpGet("users/{userId:int}")]
    public async Task<ActionResult<List<GalleryPhotoDto>>> ForUser(int userId, CancellationToken cancellationToken)
    {
        var user = await _db.Users
            .AsNoTracking()
            .Include(u => u.Settings)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user == null)
            return NotFound();

        var requesterId = User.Identity?.IsAuthenticated == true ? User.GetUserId() : null;
        if (requesterId != userId && user.Settings?.PublicProfileEnabled == false)
            return Forbid();

        return Ok(await QueryGalleryAsync(userId, cancellationToken));
    }

    private async Task<List<GalleryPhotoDto>> QueryGalleryAsync(int userId, CancellationToken cancellationToken)
    {
        var rows = await _db.GalleryPhotos
            .AsNoTracking()
            .Where(g => g.UserId == userId)
            .OrderByDescending(g => g.CreatedAtUtc)
            .Select(g => new { g.Id, g.Caption, g.CreatedAtUtc })
            .ToListAsync(cancellationToken);

        return rows
            .Select(g => Map(g.Id, g.Caption, g.CreatedAtUtc))
            .ToList();
    }

    private static GalleryPhotoDto Map(int id, string? caption, DateTime createdAtUtc) => new()
    {
        Id = id,
        Url = $"/api/gallery/{id}",
        Caption = caption,
        CreatedAtUtc = createdAtUtc
    };

    private static string GetExtension(string? mimeType) => (mimeType ?? string.Empty).ToLowerInvariant() switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        _ => ".bin"
    };
}
