namespace NeverBeen.API.Dtos;

/// <summary>Body of POST /api/messagebook (new post) or a reply when ParentId is set.</summary>
public class CommentCreateRequest
{
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.MaxLength(2000)]
    public string Text { get; set; } = string.Empty;

    /// <summary>Id of the top-level comment this is a reply to (omit for a new top-level post).</summary>
    public int? ParentId { get; set; }
}

/// <summary>Body of POST /api/messagebook/{id}/reactions.</summary>
public class ReactionRequest
{
    /// <summary>One of: Dislike, Love, Smile, Laugh, Cry, Heart, Clapping, Confused, Shocked, Angry, Fire (or Like).</summary>
    [System.ComponentModel.DataAnnotations.Required]
    public string Type { get; set; } = string.Empty;
}

public class ReactionResultDto
{
    public int LikeCount { get; set; }
    public int DislikeCount { get; set; }

    /// <summary>The user's reaction name (e.g. "Fire", "Love") or null when toggled off.</summary>
    public string? MyReaction { get; set; }
}

public class CommentDto
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public int LikeCount { get; set; }
    public int DislikeCount { get; set; }

    /// <summary>Poster details shown next to the comment.</summary>
    public AuthorInfoDto Author { get; set; } = new();

    /// <summary>The current visitor's reaction to this comment ("Like"/"Dislike") or null.</summary>
    public string? MyReaction { get; set; }

    public int ReplyCount { get; set; }

    /// <summary>Replies (only populated for top-level comments when includeReplies is requested).</summary>
    public List<CommentDto> Replies { get; set; } = new();
}

public class AuthorInfoDto
{
    public int Id { get; set; }
    public string? FullName { get; set; }

    /// <summary>Relative API URL of the profile picture, or the OAuth provider picture URL as fallback.</summary>
    public string? ProfilePhotoUrl { get; set; }
    public string? Profession { get; set; }
}

public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}
