using NeverBeen.API.Entities;

namespace NeverBeen.API.Dtos;

/// <summary>Compact author card shown on posts, comments and feeds.</summary>
public class AuthorDto
{
    public int Id { get; set; }
    public string? UniqueId { get; set; }
    public string? FullName { get; set; }
    public string? ProfilePhotoUrl { get; set; }
    public string? Profession { get; set; }
    public string? Country { get; set; }
    public string? City { get; set; }
    public bool IsVerified { get; set; }
}

/// <summary>Who may see a Journey post.</summary>
public class PostAudienceDto
{
    /// <summary>"public", "companions", "custom" or "only-me".</summary>
    public string Mode { get; set; } = JourneyPostAudience.Public;
    public List<int>? AllowIds { get; set; }
    public List<int>? DenyIds { get; set; }
}

/// <summary>A member's reaction to a post or comment.</summary>
public class ReactionDto
{
    public AuthorDto User { get; set; } = new();
    public string Type { get; set; } = JourneyReactionTypes.Like;
    public DateTime? ReactedAtUtc { get; set; }
}

public class JourneyPostDto
{
    public long Id { get; set; }
    public AuthorDto Author { get; set; } = new();
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public List<string> ImageUrls { get; set; } = new();
    public string? ImageUrl { get; set; }
    public int LikeCount { get; set; }
    public bool IsLiked { get; set; }
    public string? MyReaction { get; set; }
    public List<ReactionDto> Reactions { get; set; } = new();
    public List<AuthorDto> TaggedCompanions { get; set; } = new();
    public List<JourneyCommentDto> Comments { get; set; } = new();
    public int CommentCount { get; set; }
    public string? Location { get; set; }
    public string? Mood { get; set; }
    public string? PlaceId { get; set; }
    public List<string> Hashtags { get; set; } = new();
    public int ShareCount { get; set; }
    public int SharesCount { get; set; }
    public bool IsShared { get; set; }
    public string? SharedText { get; set; }
    public JourneyPostDto? OriginalPost { get; set; }
    public PostAudienceDto? Audience { get; set; }
    public int? WallOwnerId { get; set; }
    public string? WallOwnerName { get; set; }
    public DateTime? EditedAtUtc { get; set; }
}

public class JourneyCommentDto
{
    public long Id { get; set; }
    public long PostId { get; set; }
    public AuthorDto Author { get; set; } = new();
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public string? ImageUrl { get; set; }
    public long? ParentId { get; set; }
    public int LikeCount { get; set; }
    public bool IsLiked { get; set; }
    public string? MyReaction { get; set; }
    public List<ReactionDto> Reactions { get; set; } = new();
    public List<JourneyCommentDto> Replies { get; set; } = new();
}

/// <summary>Body of POST /api/journey (and PUT /api/journey/{id}).</summary>
public class SaveJourneyPostRequest
{
    public string Text { get; set; } = string.Empty;
    public List<string>? ImageUrls { get; set; }
    public string? Location { get; set; }
    public string? Mood { get; set; }
    public string? PlaceId { get; set; }
    public List<string>? Hashtags { get; set; }
    public PostAudienceDto? Audience { get; set; }

    /// <summary>When set, the new post shares this post on the author's feed.</summary>
    public long? OriginalPostId { get; set; }
    public string? SharedText { get; set; }

    /// <summary>When set, the post is written on that member's Journey wall.</summary>
    public int? WallOwnerId { get; set; }
}

/// <summary>Body of POST /api/journey/{id}/comments (and replies).</summary>
public class CreateJourneyCommentRequest
{
    public string Text { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }

    /// <summary>Set to reply to another comment (unlimited nesting).</summary>
    public long? ParentId { get; set; }
}

/// <summary>Body of the reaction endpoints: the chosen reaction type.</summary>
public class ReactRequest
{
    public string ReactionType { get; set; } = JourneyReactionTypes.Like;
}

// PagedResult<T> lives in MessageBookDtos.cs (shared by all list endpoints).
