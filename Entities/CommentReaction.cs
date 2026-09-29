using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NeverBeen.API.Entities;

/// <summary>
/// A like or dislike of a message-book comment. One reaction per user per comment
/// (unique on CommentId + UserId); re-submitting the same reaction removes it (toggle).
/// </summary>
[Table("CommentReactions")]
public class CommentReaction
{
    public long Id { get; set; }

    public int CommentId { get; set; }
    public CommunityComment? Comment { get; set; }

    public int UserId { get; set; }
    public UserProfile? User { get; set; }

    /// <summary><see cref="ReactionTypes.Like"/> (1) or <see cref="ReactionTypes.Dislike"/> (-1).</summary>
    public int ReactionType { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public static class ReactionTypes
{
    public const int Like = 1;
    public const int Dislike = -1;
    public const int Love = 2;
    public const int Smile = 3;
    public const int Laugh = 4;
    public const int Cry = 5;
    public const int Heart = 6;
    public const int Clapping = 7;
    public const int Confused = 8;
    public const int Shocked = 9;
    public const int Angry = 10;
    public const int Fire = 11;

    public static readonly Dictionary<string, int> NameToType = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Like", Like },
        { "Dislike", Dislike },
        { "Love", Love },
        { "Smile", Smile },
        { "Laugh", Laugh },
        { "Cry", Cry },
        { "Heart", Heart },
        { "Clapping", Clapping },
        { "Confused", Confused },
        { "Shocked", Shocked },
        { "Angry", Angry },
        { "Fire", Fire },
    };

    public static readonly Dictionary<int, string> TypeToName = new()
    {
        { Like, "Like" },
        { Dislike, "Dislike" },
        { Love, "Love" },
        { Smile, "Smile" },
        { Laugh, "Laugh" },
        { Cry, "Cry" },
        { Heart, "Heart" },
        { Clapping, "Clapping" },
        { Confused, "Confused" },
        { Shocked, "Shocked" },
        { Angry, "Angry" },
        { Fire, "Fire" },
    };
}
