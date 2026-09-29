using NeverBeen.API.Entities;

namespace NeverBeen.API.Dtos;

/// <summary>
/// A traveler as shown in companions lists. <see cref="Status"/> is relative to the
/// requesting member: "connected", "pending_outgoing", "pending_incoming" or "none".
/// </summary>
public class CompanionDto
{
    public int Id { get; set; }
    public string? UniqueId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string ProfilePhotoUrl { get; set; } = string.Empty;
    public string? CoverPhotoUrl { get; set; }
    public string Country { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Profession { get; set; } = string.Empty;
    public bool IsOnline { get; set; }
    public int MutualCompanionsCount { get; set; }
    public string Status { get; set; } = "none";
    public string? Bio { get; set; }
    public string? AboutMe { get; set; }
    public string? AboutMeDetailsJson { get; set; }
    public bool IsProfileLocked { get; set; }
    public string? ActiveStatus { get; set; }
    public string? CustomStatusText { get; set; }
    public bool IsVerified { get; set; }
    public string? RelationshipStatus { get; set; }
    public List<int>? ConnectedCompanionIds { get; set; }
}

/// <summary>A follower / following entry.</summary>
public class FollowDto
{
    public int Id { get; set; }
    public string? UniqueId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string ProfilePhotoUrl { get; set; } = string.Empty;
    public string? Profession { get; set; }
    public DateTime FollowedAtUtc { get; set; }
}

/// <summary>Follow counters for a profile.</summary>
public class FollowCountsDto
{
    public int Followers { get; set; }
    public int Following { get; set; }
    public bool IsFollowing { get; set; }
}

/// <summary>Result of a companionship action.</summary>
public class CompanionshipResultDto
{
    /// <summary>"connected", "pending_outgoing", "pending_incoming" or "none".</summary>
    public string Status { get; set; } = "none";
    public string Message { get; set; } = string.Empty;
}
