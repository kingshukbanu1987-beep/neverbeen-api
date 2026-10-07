namespace NeverBeen.API.Dtos;

/// <summary>
/// Full user profile - backs the profile page with its sections
/// (About Me, Details, Gallery, Settings).
/// </summary>
public class ProfileDto
{
    public int Id { get; set; }
    public string? FullName { get; set; }

    /// <summary>First name (own column on the member row); split out of FullName when empty.</summary>
    public string? FirstName { get; set; }

    /// <summary>Last name / surname (own column on the member row).</summary>
    public string? LastName { get; set; }

    public string Email { get; set; } = string.Empty;
    public string? Gender { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public int? Age { get; set; }

    // Details section
    public int? CountryId { get; set; }
    public string? CountryName { get; set; }
    public int? CityId { get; set; }
    public string? CityName { get; set; }

    /// <summary>State / province as typed on the registration page (Users.State).</summary>
    public string? State { get; set; }

    public string? Pincode { get; set; }
    public string? ContactNumber { get; set; }
    public string? PostalAddress { get; set; }

    // About Me section
    public string? AboutMe { get; set; }
    public string? Profession { get; set; }

    public string Status { get; set; } = string.Empty;

    /// <summary>Relative API URL of the uploaded profile picture, or the OAuth provider picture URL as fallback.</summary>
    public string? ProfilePhotoUrl { get; set; }
    public string? ExternalProfilePictureUrl { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    // Settings section
    public SettingsDto Settings { get; set; } = new();

    // Gallery section
    public List<GalleryPhotoDto> Gallery { get; set; } = new();

    public int CommentCount { get; set; }
}

public class SettingsDto
{
    public bool EmailNotificationsEnabled { get; set; } = true;
    public bool PhoneNotificationsEnabled { get; set; }
    public bool PublicProfileEnabled { get; set; } = true;

    /// <summary>"light", "dark" or "system".</summary>
    public string Theme { get; set; } = "light";
    public string? Timezone { get; set; }

    public bool IsProfileLocked { get; set; }

    /// <summary>"everyone", "companions" or "none".</summary>
    public string WhoCanMessage { get; set; } = "everyone";
    public bool SearchVisibility { get; set; } = true;

    /// <summary>"public" or "companions".</summary>
    public string JourneyVisibility { get; set; } = "public";
    public bool SoundNotificationsEnabled { get; set; } = true;
    public bool TwoFactorEnabled { get; set; }
    public string[]? TravelStyles { get; set; }
    public string? PreferredSeason { get; set; }

    /// <summary>"everyone", "companions-of-companions" or "none".</summary>
    public string WhoCanConnect { get; set; } = "everyone";

    /// <summary>"everyone", "companions" or "none".</summary>
    public string WhoCanVisitProfile { get; set; } = "everyone";

    /// <summary>"everyone", "companions" or "only-me".</summary>
    public string ShowActiveStatusTo { get; set; } = "everyone";

    /// <summary>"everyone", "companions" or "only-me".</summary>
    public string WhoCanSeeCompanionsList { get; set; } = "everyone";
    public bool AllowCompanionTagging { get; set; } = true;
    public bool ApproveTagsBeforePost { get; set; }
    public bool IsVerified { get; set; }
    public string? VerificationEmail { get; set; }

    /// <summary>"work" or "university".</summary>
    public string? VerificationType { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }
}

/// <summary>
/// Body of PUT /api/profile. All fields are optional; a field that is omitted (null)
/// keeps its current value. The email address is not changeable here - it is the
/// OAuth account email.
/// </summary>
public class UpdateProfileRequest
{
    [System.ComponentModel.DataAnnotations.MaxLength(200)]
    public string? FullName { get; set; }

    [System.ComponentModel.DataAnnotations.MaxLength(100)]
    public string? FirstName { get; set; }

    [System.ComponentModel.DataAnnotations.MaxLength(100)]
    public string? LastName { get; set; }

    [System.ComponentModel.DataAnnotations.MaxLength(20)]
    public string? Gender { get; set; }

    public DateTime? DateOfBirth { get; set; }

    public int? CountryId { get; set; }
    public int? CityId { get; set; }

    [System.ComponentModel.DataAnnotations.MaxLength(100)]
    public string? State { get; set; }

    [System.ComponentModel.DataAnnotations.MaxLength(20)]
    public string? Pincode { get; set; }

    [System.ComponentModel.DataAnnotations.MaxLength(30)]
    public string? ContactNumber { get; set; }

    [System.ComponentModel.DataAnnotations.MaxLength(500)]
    public string? PostalAddress { get; set; }

    [System.ComponentModel.DataAnnotations.MaxLength(2000)]
    public string? AboutMe { get; set; }

    [System.ComponentModel.DataAnnotations.MaxLength(60)]
    public string? Profession { get; set; }
}
