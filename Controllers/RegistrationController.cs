using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NeverBeen.API.Common;
using NeverBeen.API.Data;
using NeverBeen.API.Dtos;
using NeverBeen.API.Entities;

namespace NeverBeen.API.Controllers;

/// <summary>
/// Completes the registration of a user who signed in via OAuth with a "Pending" status
/// (the Registration Page shown to new members). Accepts multipart/form-data:
/// the form fields plus an optional "photo" file. On success the profile becomes
/// "Active" and the frontend is redirected to the user profile page.
/// </summary>
[ApiController]
[Route("api/registration")]
[Authorize]
public class RegistrationController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<RegistrationController> _logger;

    public RegistrationController(AppDbContext db, ILogger<RegistrationController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProfileDto>> Complete([FromForm] RegistrationRequest request, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        var user = await _db.Users
            .Include(u => u.Settings)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user == null)
            return NotFound();

        if (user.Status == UserProfileStatus.Active)
            return Conflict(new { error = "This account already has a complete profile. Use PUT /api/profile to update it." });

        // ---- business rules beyond attribute validation -------------------------
        if (!AppConstants.Genders.Contains(request.Gender, StringComparer.OrdinalIgnoreCase))
            return BadRequest(new { error = $"Gender must be one of: {string.Join(", ", AppConstants.Genders)}." });

        if (!AppConstants.Professions.Contains(request.Profession, StringComparer.Ordinal))
            return BadRequest(new { error = $"Profession must be one of: {string.Join(", ", AppConstants.Professions)}." });

        if (request.DateOfBirth.Date >= DateTime.Today)
            return BadRequest(new { error = "Date of birth must be in the past." });

        var age = DateTime.Today.Year - request.DateOfBirth.Year;
        if (request.DateOfBirth.Date > DateTime.Today.AddYears(-age))
            age--;
        if (age < 13 || age > 120)
            return BadRequest(new { error = "Date of birth must be between 13 and 120 years ago." });

        if (request.CountryId <= 0 || request.CityId <= 0)
            return BadRequest(new { error = "Country and City are required." });

        var city = await _db.Cities.FindAsync(new object[] { request.CityId }, cancellationToken);
        if (city == null || city.CountryId != request.CountryId)
            return BadRequest(new { error = "The selected city does not belong to the selected country." });

        // ---- email ----------------------------------------------------------------
        var newEmail = request.Email.Trim();
        if (!newEmail.Equals(user.Email, StringComparison.OrdinalIgnoreCase))
        {
            var emailTaken = await _db.Users
                .AnyAsync(u => u.Email.ToLower() == newEmail.ToLower() && u.Id != userId, cancellationToken);
            if (emailTaken)
                return Conflict(new { error = "The email address is already registered to another community member." });
        }

        // ---- optional photograph ----------------------------------------------------
        var photoError = ImageValidation.ValidateOptional(request.Photo, AppConstants.MaxProfilePhotoBytes);
        if (photoError != null)
            return BadRequest(new { error = photoError });

        if (request.Photo != null && request.Photo.Length > 0)
        {
            using var memory = new MemoryStream();
            await request.Photo.CopyToAsync(memory, cancellationToken);
            user.ProfilePhotoData = memory.ToArray();
            user.ProfilePhotoMimeType = request.Photo.ContentType ?? "image/jpeg";
        }

        // ---- write the profile --------------------------------------------------------
        user.FullName = request.FullName.Trim();
        user.Email = newEmail;
        user.Gender = request.Gender;
        user.DateOfBirth = request.DateOfBirth.Date;
        user.CountryId = request.CountryId;
        user.CityId = request.CityId;
        user.Pincode = string.IsNullOrWhiteSpace(request.Pincode) ? null : request.Pincode.Trim();
        user.ContactNumber = string.IsNullOrWhiteSpace(request.ContactNumber) ? null : request.ContactNumber.Trim();
        user.PostalAddress = string.IsNullOrWhiteSpace(request.PostalAddress) ? null : request.PostalAddress.Trim();
        user.AboutMe = string.IsNullOrWhiteSpace(request.AboutMe) ? null : request.AboutMe.Trim();
        user.Profession = request.Profession;
        user.Status = UserProfileStatus.Active;
        user.UpdatedAtUtc = DateTime.UtcNow;

        if (user.Settings == null)
            user.Settings = new UserSettings { UserId = userId };

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("User {UserId} completed registration as {Email}", userId, newEmail);

        var fresh = await LoadFullUserAsync(userId, cancellationToken);
        return Ok(await BuildDtoAsync(fresh!, cancellationToken));
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
        var commentCount = await _db.CommunityComments
            .CountAsync(c => c.AuthorId == user.Id, cancellationToken);
        return ProfileMapper.ToDto(user, new List<GalleryPhotoDto>(), commentCount);
    }
}
