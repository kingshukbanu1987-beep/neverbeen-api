using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NeverBeen.API.Entities;

/// <summary>
/// A companionship between two travelers. One row per pair: <see cref="UserId"/> is
/// always the smaller id and <see cref="CompanionId"/> the larger one; the direction
/// of the request comes from <see cref="RequesterId"/>.
/// </summary>
[Table("Companionships")]
public class Companionship
{
    public long Id { get; set; }

    public int UserId { get; set; }
    public int CompanionId { get; set; }

    /// <summary>The member who sent the companionship request.</summary>
    public int RequesterId { get; set; }

    /// <summary>"pending" or "connected".</summary>
    [MaxLength(20)]
    public string Status { get; set; } = CompanionshipStatus.Pending;

    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ConnectedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public static class CompanionshipStatus
{
    public const string Pending = "pending";
    public const string Connected = "connected";
}

/// <summary>One traveler follows another (followers / following).</summary>
[Table("Follows")]
public class Follow
{
    public long Id { get; set; }
    public int FollowerId { get; set; }
    public int FolloweeId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
