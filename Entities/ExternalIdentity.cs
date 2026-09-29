using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NeverBeen.API.Entities;

/// <summary>
/// Links a user to an external OAuth account (Google / Facebook / Microsoft).
/// A user can sign in with any of the linked identities.
/// </summary>
[Table("ExternalIdentities")]
public class ExternalIdentity
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public UserProfile? User { get; set; }

    /// <summary>"Google", "Facebook" or "Microsoft".</summary>
    [MaxLength(20)]
    public string Provider { get; set; } = string.Empty;

    /// <summary>Stable provider-side user id (Google "sub", Facebook "id", Microsoft Graph "id").</summary>
    [MaxLength(256)]
    public string ProviderKey { get; set; } = string.Empty;

    /// <summary>Email address reported by the provider at login time.</summary>
    [MaxLength(256)]
    public string? Email { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
