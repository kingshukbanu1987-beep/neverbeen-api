using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NeverBeen.API.Entities;

/// <summary>
/// Per-user settings, backing the "Settings" section of the user profile page.
/// One row per user (1:1 with <see cref="UserProfile"/>).
/// </summary>
[Table("UserSettings")]
public class UserSettings
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public UserProfile? User { get; set; }

    public bool EmailNotificationsEnabled { get; set; } = true;
    public bool PhoneNotificationsEnabled { get; set; }

    /// <summary>When false the profile (and photo) is only visible to the user themselves.</summary>
    public bool PublicProfileEnabled { get; set; } = true;

    /// <summary>"light", "dark" or "system".</summary>
    [MaxLength(20)]
    public string Theme { get; set; } = "light";

    [MaxLength(64)]
    public string? Timezone { get; set; }

    /// <summary>When true the profile shows a locked shield to non-connected travelers.</summary>
    public bool IsProfileLocked { get; set; }

    /// <summary>"everyone", "companions" or "none".</summary>
    [MaxLength(20)]
    public string WhoCanMessage { get; set; } = "everyone";

    /// <summary>When false the member is hidden from community search.</summary>
    public bool SearchVisibility { get; set; } = true;

    /// <summary>Default audience of Journey posts: "public" or "companions".</summary>
    [MaxLength(20)]
    public string JourneyVisibility { get; set; } = "public";

    public bool SoundNotificationsEnabled { get; set; } = true;
    public bool TwoFactorEnabled { get; set; }

    /// <summary>Travel styles chosen on the profile (e.g. "Solo Exploration").</summary>
    public string[]? TravelStyles { get; set; }

    [MaxLength(40)]
    public string? PreferredSeason { get; set; }

    /// <summary>Who may send a companionship request: "everyone", "companions-of-companions" or "none".</summary>
    [MaxLength(30)]
    public string WhoCanConnect { get; set; } = "everyone";

    /// <summary>Who may open the profile: "everyone", "companions" or "none".</summary>
    [MaxLength(20)]
    public string WhoCanVisitProfile { get; set; } = "everyone";

    /// <summary>Who sees the active / away / busy presence: "everyone", "companions" or "only-me".</summary>
    [MaxLength(20)]
    public string ShowActiveStatusTo { get; set; } = "everyone";

    /// <summary>Who sees the companions list: "everyone", "companions" or "only-me".</summary>
    [MaxLength(20)]
    public string WhoCanSeeCompanionsList { get; set; } = "everyone";

    /// <summary>Companions may tag the member in Journey posts.</summary>
    public bool AllowCompanionTagging { get; set; } = true;

    /// <summary>Tags that mention the member wait for approval.</summary>
    public bool ApproveTagsBeforePost { get; set; }

    /// <summary>Work / university verification (blue tick) mirrored from the user row.</summary>
    public bool IsVerified { get; set; }

    [MaxLength(256)]
    public string? VerificationEmail { get; set; }

    /// <summary>"work" or "university".</summary>
    [MaxLength(20)]
    public string? VerificationType { get; set; }

    public DateTime? VerifiedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
