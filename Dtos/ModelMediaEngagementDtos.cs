using System.ComponentModel.DataAnnotations;

namespace NeverBeen.API.Dtos;

/// <summary>Per-media counters returned by GET /api/models/{modelSlug}/media/stats.</summary>
public class ModelMediaStatDto
{
    public string MediaKey { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public int SeenCount { get; set; }
    public int LikeCount { get; set; }
    public int CommentCount { get; set; }
    public int ShareCount { get; set; }
    public bool LikedByMe { get; set; }
}

/// <summary>Request body for POST /api/models/{modelSlug}/media/view and /share.</summary>
public class RecordModelMediaEventRequest
{
    [Required]
    [StringLength(512, MinimumLength = 1)]
    public string MediaKey { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^(photo|video)$")]
    public string MediaType { get; set; } = "photo";

    [StringLength(64)]
    public string? ViewerToken { get; set; }
}

/// <summary>Response from POST /api/models/{modelSlug}/media/view.</summary>
public class ModelMediaViewResponse
{
    public int SeenCount { get; set; }
}

/// <summary>Response from POST /api/models/{modelSlug}/media/share.</summary>
public class ModelMediaShareResponse
{
    public int ShareCount { get; set; }
}

/// <summary>Request body for POST /api/models/{modelSlug}/media/like.</summary>
public class ToggleModelMediaLikeRequest
{
    [Required]
    [StringLength(512, MinimumLength = 1)]
    public string MediaKey { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^(photo|video)$")]
    public string MediaType { get; set; } = "photo";
}

/// <summary>Response from POST /api/models/{modelSlug}/media/like.</summary>
public class ModelMediaLikeResponse
{
    public bool LikedByMe { get; set; }
    public int LikeCount { get; set; }
}

/// <summary>Request body for POST /api/models/{modelSlug}/media/comments.</summary>
public class CreateModelMediaCommentRequest
{
    [Required]
    [StringLength(512, MinimumLength = 1)]
    public string MediaKey { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^(photo|video)$")]
    public string MediaType { get; set; } = "photo";

    [Required]
    [StringLength(1000, MinimumLength = 1)]
    public string Text { get; set; } = string.Empty;
}

/// <summary>A single comment on a photograph or clip.</summary>
public class ModelMediaCommentDto
{
    public long Id { get; set; }
    public string MediaKey { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public AuthorDto Author { get; set; } = new();
}
