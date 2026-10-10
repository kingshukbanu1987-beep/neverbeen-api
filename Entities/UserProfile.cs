using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NeverBeen.API.Entities;

/// <summary>
/// A NeverBeen community member. Created automatically on the first OAuth (SSO) login;
/// the profile is completed through the registration flow, which flips <see cref="Status"/>
/// from "Pending" to "Active".
/// </summary>
[Table("Users")]
public class UserProfile
{
    public int Id { get; set; }

    /// <summary>20-digit public profile id ('8920153401' + zero-padded Id), used in profile URLs.</summary>
    [MaxLength(20)]
    public string? UniqueId { get; set; }

    [MaxLength(100)]
    public string? FirstName { get; set; }

    [MaxLength(100)]
    public string? LastName { get; set; }

    /// <summary>Full name as typed on the registration page (or from the OAuth account before registration).</summary>
    [MaxLength(200)]
    public string? FullName { get; set; }

    /// <summary>Login email - by default the OAuth (Google / Facebook / Outlook) email address.</summary>
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    /// <summary>One of: Male, Female, Other.</summary>
    [MaxLength(20)]
    public string? Gender { get; set; }

    /// <summary>Stored as a plain timestamp (no time zone) — a calendar date only.</summary>
    [Column(TypeName = "timestamp without time zone")]
    public DateTime? DateOfBirth { get; set; }

    public int? CountryId { get; set; }
    public Country? Country { get; set; }

    public int? CityId { get; set; }
    public City? City { get; set; }

    /// <summary>State / province as typed on the registration page.</summary>
    [MaxLength(100)]
    public string? State { get; set; }

    [MaxLength(20)]
    public string? Pincode { get; set; }

    /// <summary>Optional contact number.</summary>
    [MaxLength(30)]
    public string? ContactNumber { get; set; }

    /// <summary>Optional full postal address.</summary>
    [MaxLength(500)]
    public string? PostalAddress { get; set; }

    /// <summary>Optional "About Me" text.</summary>
    [MaxLength(2000)]
    public string? AboutMe { get; set; }

    /// <summary>Selected from the fixed profession drop-down list.</summary>
    [MaxLength(60)]
    public string? Profession { get; set; }

    /// <summary>"Pending" = signed in via SSO but registration not finished; "Active" = registered profile.</summary>
    [MaxLength(20)]
    public string Status { get; set; } = UserProfileStatus.Pending;

    /// <summary>Profile picture URL supplied by the OAuth provider (e.g. Google photo). Used as a fallback before the user uploads a photo.</summary>
    [MaxLength(1024)]
    public string? ExternalProfilePictureUrl { get; set; }

    /// <summary>Profile photo URL (e.g. Supabase Storage) — alternative to the stored bytes.</summary>
    [MaxLength(1024)]
    public string? ProfilePhotoUrl { get; set; }

    /// <summary>Cover photo URL (e.g. Supabase Storage) — alternative to the stored bytes.</summary>
    [MaxLength(1024)]
    public string? CoverPhotoUrl { get; set; }

    /// <summary>Profile picture uploaded by the user (stored in the database).</summary>
    public byte[]? ProfilePhotoData { get; set; }

    [MaxLength(64)]
    public string? ProfilePhotoMimeType { get; set; }

    /// <summary>Cover photo uploaded by the user (stored in the database, max 100 KB).</summary>
    public byte[]? CoverPhotoData { get; set; }

    [MaxLength(64)]
    public string? CoverPhotoMimeType { get; set; }

    /// <summary>Structured JSON representation of the 8 detailed About Me sub-sections.</summary>
    [MaxLength(8000)]
    public string? AboutMeDetailsJson { get; set; }

    /// <summary>Presence shown to other travelers: Active, Busy, Don't Disturb, Away, Inactive or Custom.</summary>
    [MaxLength(20)]
    public string ActiveStatus { get; set; } = "Active";

    /// <summary>Custom presence text (max 15 letters) used when <see cref="ActiveStatus"/> is "Custom".</summary>
    [MaxLength(30)]
    public string? CustomStatusText { get; set; }

    /// <summary>
    /// When the member was last using the community (presence heartbeat, status change, sign-in
    /// or sign-out), in UTC. Null until it is first recorded. Away is shown after 15 minutes without use.
    /// </summary>
    public DateTime? LastSeenUtc { get; set; }

    /// <summary>When true, non-connected travelers see a locked profile.</summary>
    public bool IsProfileLocked { get; set; }

    /// <summary>Work / university verification (blue tick).</summary>
    public bool IsVerified { get; set; }

    [MaxLength(256)]
    public string? VerifiedEmail { get; set; }

    /// <summary>"work" or "university".</summary>
    [MaxLength(20)]
    public string? VerificationType { get; set; }

    public DateTime? VerifiedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<ExternalIdentity> Identities { get; set; } = new List<ExternalIdentity>();
    public UserSettings? Settings { get; set; }
    public ICollection<GalleryPhoto> GalleryPhotos { get; set; } = new List<GalleryPhoto>();
    public ICollection<CommunityComment> Comments { get; set; } = new List<CommunityComment>();
}

public static class UserProfileStatus
{
    public const string Pending = "Pending";
    public const string Active = "Active";

    /// <summary>The 20-digit public profile id derived from the numeric user id.</summary>
    public static string GenerateUniqueId(int userId)
        => "8920153401" + userId.ToString().PadLeft(10, '0');
}
