using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NeverBeen.API.Entities;

/// <summary>
/// A post in a member's Journey feed. Supports photos, location / mood / place tags,
/// hashtags, an audience (public, companions, custom allow/deny list, only-me),
/// sharing of another post and posting on another member's wall.
/// </summary>
[Table("JourneyPosts")]
public class JourneyPost
{
    public long Id { get; set; }

    public int AuthorId { get; set; }

    /// <summary>Set when the post was written on another member's Journey wall.</summary>
    public int? WallOwnerId { get; set; }

    [MaxLength(4000)]
    public string Text { get; set; } = string.Empty;

    /// <summary>Photo URLs attached to the post (0..n).</summary>
    public string[]? ImageUrls { get; set; }

    [MaxLength(200)]
    public string? Location { get; set; }

    /// <summary>Travel mood chosen in the Journey composer.</summary>
    [MaxLength(60)]
    public string? Mood { get; set; }

    /// <summary>Google Maps place id of the destination tag.</summary>
    [MaxLength(120)]
    public string? PlaceId { get; set; }

    public string[]? Hashtags { get; set; }

    /// <summary>"public", "companions", "custom" or "only-me".</summary>
    [MaxLength(20)]
    public string AudienceMode { get; set; } = JourneyPostAudience.Public;

    /// <summary>Custom caption written when sharing another post.</summary>
    [MaxLength(2000)]
    public string? SharedText { get; set; }

    /// <summary>The post this one shares (null for original posts).</summary>
    public long? OriginalPostId { get; set; }

    public int LikeCount { get; set; }
    public int ShareCount { get; set; }
    public int CommentCount { get; set; }
    public bool IsEdited { get; set; }
    public DateTime? EditedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public static class JourneyPostAudience
{
    public const string Public = "public";
    public const string Companions = "companions";
    public const string Custom = "custom";
    public const string OnlyMe = "only-me";
}

public static class JourneyReactionTypes
{
    public const string Like = "Like";
    public const string Dislike = "Dislike";
    public const string Love = "Love";
    public const string Smile = "Smile";
    public const string Laugh = "Laugh";
    public const string Cry = "Cry";
    public const string Heart = "Heart";
    public const string Clapping = "Clapping";
    public const string Confused = "Confused";
    public const string Shocked = "Shocked";
    public const string Angry = "Angry";
    public const string Fire = "Fire";

    public static readonly string[] All =
    {
        Like, Dislike, Love, Smile, Laugh, Cry, Heart, Clapping, Confused, Shocked, Angry, Fire
    };
}

/// <summary>One row of a custom post audience: an allowed or denied companion.</summary>
[Table("JourneyPostAudienceEntries")]
public class JourneyPostAudienceEntry
{
    public long Id { get; set; }
    public long PostId { get; set; }
    public int UserId { get; set; }

    /// <summary>"allow" or "deny".</summary>
    [MaxLength(10)]
    public string Kind { get; set; } = "allow";
}

/// <summary>A companion tagged in a Journey post.</summary>
[Table("JourneyPostTags")]
public class JourneyPostTag
{
    public long Id { get; set; }
    public long PostId { get; set; }
    public int UserId { get; set; }
}

/// <summary>One reaction per member per Journey post (re-submitting changes the type).</summary>
[Table("JourneyPostReactions")]
public class JourneyPostReaction
{
    public long Id { get; set; }
    public long PostId { get; set; }
    public int UserId { get; set; }

    /// <summary>One of <see cref="JourneyReactionTypes"/>.</summary>
    [MaxLength(20)]
    public string ReactionType { get; set; } = JourneyReactionTypes.Like;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
