using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NeverBeen.API.Common;
using NeverBeen.API.Data;
using NeverBeen.API.Dtos;
using NeverBeen.API.Entities;

namespace NeverBeen.API.Controllers;

/// <summary>
/// User profile endpoints - backing the profile page with its Side Panel
/// (About Me, Details, Gallery, Settings) and the main panel.
/// GET /api/profile/{id} is public (respecting the user's "public profile" setting);
/// all mutations require the user's JWT.
/// </summary>
[ApiController]
[Route("api/profile")]
public class ProfileController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<ProfileController> _logger;

    public ProfileController(AppDbContext db, ILogger<ProfileController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Full profile of the signed-in user (About Me + Details + Gallery + Settings).</summary>
    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<ProfileDto>> Me(CancellationToken cancellationToken)
    {
        var user = await LoadFullUserAsync(User.GetUserId(), cancellationToken);
        if (user == null)
            return NotFound();
        return Ok(await BuildDtoAsync(user, cancellationToken));
    }

    /// <summary>Public profile of any member (used to view another member's profile page).</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProfileDto>> Get(int id, CancellationToken cancellationToken)
    {
        var user = await LoadFullUserAsync(id, cancellationToken);
        if (user == null)
            return NotFound();

        var requesterId = User.Identity?.IsAuthenticated == true ? User.GetUserId() : null;
        if (requesterId != id && user.Settings?.PublicProfileEnabled == false)
            return Forbid();

        return Ok(await BuildDtoAsync(user, cancellationToken));
    }

    /// <summary>Updates editable details of the signed-in user. Omitted (null) fields keep their current value.</summary>
    [Authorize]
    [HttpPut]
    public async Task<ActionResult<ProfileDto>> Update([FromBody] UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var user = await _db.Users
            .Include(u => u.Settings)
            .FirstOrDefaultAsync(u => u.Id == User.GetUserId(), cancellationToken);
        if (user == null)
            return NotFound();

        if (request.Gender != null && !AppConstants.Genders.Contains(request.Gender, StringComparer.OrdinalIgnoreCase))
            return BadRequest(new { error = $"Gender must be one of: {string.Join(", ", AppConstants.Genders)}." });

        if (request.Profession != null && !AppConstants.Professions.Contains(request.Profession, StringComparer.Ordinal))
            return BadRequest(new { error = $"Profession must be one of: {string.Join(", ", AppConstants.Professions)}." });

        if (request.DateOfBirth.HasValue && request.DateOfBirth.Value.Date >= DateTime.Today)
            return BadRequest(new { error = "Date of birth must be in the past." });

        if (request.CountryId.HasValue || request.CityId.HasValue)
        {
            var countryId = request.CountryId ?? user.CountryId;
            var cityId = request.CityId ?? user.CityId;
            if (countryId == null || cityId == null)
                return BadRequest(new { error = "Both Country and City are required." });

            var city = await _db.Cities.FindAsync(new object[] { cityId }, cancellationToken);
            if (city == null || city.CountryId != countryId)
                return BadRequest(new { error = "The selected city does not belong to the selected country." });
        }

        if (!string.IsNullOrWhiteSpace(request.FullName))
            user.FullName = request.FullName.Trim();
        if (request.Gender != null)
            user.Gender = request.Gender;
        if (request.DateOfBirth.HasValue)
            user.DateOfBirth = request.DateOfBirth.Value.Date;
        if (request.CountryId.HasValue)
            user.CountryId = request.CountryId;
        if (request.CityId.HasValue)
            user.CityId = request.CityId;
        if (request.Pincode != null)
            user.Pincode = request.Pincode.Trim();
        if (request.ContactNumber != null)
            user.ContactNumber = request.ContactNumber.Trim();
        if (request.PostalAddress != null)
            user.PostalAddress = request.PostalAddress.Trim();
        if (request.AboutMe != null)
            user.AboutMe = request.AboutMe.Trim();
        if (request.Profession != null)
            user.Profession = request.Profession;

        user.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        var updated = await LoadFullUserAsync(user.Id, cancellationToken);
        return Ok(await BuildDtoAsync(updated!, cancellationToken));
    }

    /// <summary>Uploads a new profile photograph (multipart, field name "photo").</summary>
    [Authorize]
    [HttpPut("photo")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ProfileDto>> UploadPhoto([FromForm] IFormFile photo, CancellationToken cancellationToken)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Id == User.GetUserId(), cancellationToken);
        if (user == null)
            return NotFound();

        var error = ImageValidation.ValidateRequired(photo, AppConstants.MaxProfilePhotoBytes);
        if (error != null)
            return BadRequest(new { error });

        using var memory = new MemoryStream();
        await photo.CopyToAsync(memory, cancellationToken);
        user.ProfilePhotoData = memory.ToArray();
        user.ProfilePhotoMimeType = photo.ContentType ?? "image/jpeg";
        user.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        var updated = await LoadFullUserAsync(user.Id, cancellationToken);
        return Ok(await BuildDtoAsync(updated!, cancellationToken));
    }

    /// <summary>Removes the uploaded profile photograph (the OAuth provider picture becomes the fallback).</summary>
    [Authorize]
    [HttpDelete("photo")]
    public async Task<IActionResult> DeletePhoto(CancellationToken cancellationToken)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Id == User.GetUserId(), cancellationToken);
        if (user == null)
            return NotFound();

        user.ProfilePhotoData = null;
        user.ProfilePhotoMimeType = null;
        user.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Updates the signed-in user's Settings section.</summary>
    [Authorize]
    [HttpPut("settings")]
    public async Task<ActionResult<SettingsDto>> UpdateSettings([FromBody] SettingsDto request, CancellationToken cancellationToken)
    {
        var user = await _db.Users
            .Include(u => u.Settings)
            .FirstOrDefaultAsync(u => u.Id == User.GetUserId(), cancellationToken);
        if (user == null)
            return NotFound();

        if (request.Theme != null && !AppConstants.Themes.Contains(request.Theme, StringComparer.OrdinalIgnoreCase))
            return BadRequest(new { error = $"Theme must be one of: {string.Join(", ", AppConstants.Themes)}." });

        if (user.Settings == null)
            user.Settings = new UserSettings { UserId = user.Id };

        user.Settings.EmailNotificationsEnabled = request.EmailNotificationsEnabled;
        user.Settings.PhoneNotificationsEnabled = request.PhoneNotificationsEnabled;
        user.Settings.PublicProfileEnabled = request.PublicProfileEnabled;
        if (request.Theme != null)
            user.Settings.Theme = request.Theme;
        user.Settings.Timezone = string.IsNullOrWhiteSpace(request.Timezone) ? null : request.Timezone.Trim();
        user.Settings.IsProfileLocked = request.IsProfileLocked;
        user.Settings.WhoCanMessage = request.WhoCanMessage;
        user.Settings.SearchVisibility = request.SearchVisibility;
        user.Settings.JourneyVisibility = request.JourneyVisibility;
        user.Settings.SoundNotificationsEnabled = request.SoundNotificationsEnabled;
        user.Settings.TwoFactorEnabled = request.TwoFactorEnabled;
        user.Settings.TravelStyles = request.TravelStyles;
        user.Settings.PreferredSeason = request.PreferredSeason;
        user.Settings.WhoCanConnect = request.WhoCanConnect;
        user.Settings.WhoCanVisitProfile = request.WhoCanVisitProfile;
        user.Settings.ShowActiveStatusTo = request.ShowActiveStatusTo;
        user.Settings.WhoCanSeeCompanionsList = request.WhoCanSeeCompanionsList;
        user.Settings.AllowCompanionTagging = request.AllowCompanionTagging;
        user.Settings.ApproveTagsBeforePost = request.ApproveTagsBeforePost;
        user.Settings.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(SettingsDtoMapper.FromEntity(user.Settings));
    }

    /// <summary>Serves the profile photograph as an image (used by &lt;img&gt; tags across the app).</summary>
    [HttpGet("{id:int}/photo")]
    public async Task<IActionResult> GetPhoto(int id, CancellationToken cancellationToken)
    {
        var user = await _db.Users
            .AsNoTracking()
            .Include(u => u.Settings)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user == null || user.ProfilePhotoData == null)
            return NotFound();

        var requesterId = User.Identity?.IsAuthenticated == true ? User.GetUserId() : null;
        if (requesterId != id && user.Settings?.PublicProfileEnabled == false)
            return Forbid();

        return File(user.ProfilePhotoData, user.ProfilePhotoMimeType ?? "image/jpeg");
    }

    private async Task<UserProfile?> LoadFullUserAsync(int id, CancellationToken cancellationToken)
        => await _db.Users
            .AsNoTracking()
            .Include(u => u.Country)
            .Include(u => u.City)
            .Include(u => u.Settings)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    private async Task<ProfileDto> BuildDtoAsync(UserProfile user, CancellationToken cancellationToken)
    {
        var gallery = await _db.GalleryPhotos
            .AsNoTracking()
            .Where(g => g.UserId == user.Id)
            .OrderByDescending(g => g.CreatedAtUtc)
            .Select(g => new { g.Id, g.Caption, g.CreatedAtUtc })
            .ToListAsync(cancellationToken);

        var commentCount = await _db.CommunityComments
            .CountAsync(c => c.AuthorId == user.Id, cancellationToken);

        var galleryDtos = gallery
            .Select(g => new GalleryPhotoDto
            {
                Id = g.Id,
                Url = $"/api/gallery/{g.Id}",
                Caption = g.Caption,
                CreatedAtUtc = g.CreatedAtUtc
            })
            .ToList();

        return ProfileMapper.ToDto(user, galleryDtos, commentCount);
    }
}
