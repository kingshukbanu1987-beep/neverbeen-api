using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NeverBeen.API.Common;
using NeverBeen.API.Data;
using NeverBeen.API.Dtos;
using NeverBeen.API.Entities;

namespace NeverBeen.API.Controllers;

/// <summary>
/// Engagement (seen, likes, comments, share) on each photograph and clip inside AI model portfolios.
///
/// Seen and share counts never require authentication — each visitor (signed-in member or guest
/// token) is counted once per media.
///
/// Likes and comments require an active Community session. Reading comments is public so every
/// visitor exploring a portfolio can see the conversation.
/// </summary>
[ApiController]
[Route("api/models/{modelSlug}/media")]
public class ModelMediaEngagementController : ControllerBase
{
    private const int MaximumPageSize = 100;
    private static readonly Regex ModelSlugPattern = new(
        "^[a-z0-9]+(?:-[a-z0-9]+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly AppDbContext _db;

    public ModelMediaEngagementController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>Returns every media's seen, like, comment, and share count for this portfolio.</summary>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(List<ModelMediaStatDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ModelMediaStatDto>>> GetStats(
        [FromRoute] string modelSlug,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidModelSlug(modelSlug))
            return BadRequest(new { error = "The model slug is invalid." });

        var viewerId = await ResolveViewerIdAsync(cancellationToken);

        var views = await _db.ModelMediaViews
            .AsNoTracking()
            .Where(v => v.ModelSlug == modelSlug)
            .GroupBy(v => new { v.MediaKey, v.MediaType })
            .Select(g => new { g.Key.MediaKey, g.Key.MediaType, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var likes = await _db.ModelMediaLikes
            .AsNoTracking()
            .Where(l => l.ModelSlug == modelSlug)
            .GroupBy(l => new { l.MediaKey, l.MediaType })
            .Select(g => new { g.Key.MediaKey, g.Key.MediaType, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var comments = await _db.ModelMediaComments
            .AsNoTracking()
            .Where(c => c.ModelSlug == modelSlug)
            .GroupBy(c => new { c.MediaKey, c.MediaType })
            .Select(g => new { g.Key.MediaKey, g.Key.MediaType, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var shares = await _db.ModelMediaShares
            .AsNoTracking()
            .Where(s => s.ModelSlug == modelSlug)
            .GroupBy(s => new { s.MediaKey, s.MediaType })
            .Select(g => new { g.Key.MediaKey, g.Key.MediaType, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var myLikes = viewerId.HasValue
            ? await _db.ModelMediaLikes
                .AsNoTracking()
                .Where(l => l.ModelSlug == modelSlug && l.UserId == viewerId.Value)
                .Select(l => l.MediaKey)
                .ToListAsync(cancellationToken)
            : new List<string>();

        var keys = views.Select(v => new { v.MediaKey, v.MediaType })
            .Union(likes.Select(l => new { l.MediaKey, l.MediaType }))
            .Union(comments.Select(c => new { c.MediaKey, c.MediaType }))
            .Union(shares.Select(s => new { s.MediaKey, s.MediaType }))
            .Distinct()
            .ToList();

        var viewMap = views.ToDictionary(v => (v.MediaKey, v.MediaType), v => v.Count);
        var likeMap = likes.ToDictionary(l => (l.MediaKey, l.MediaType), l => l.Count);
        var commentMap = comments.ToDictionary(c => (c.MediaKey, c.MediaType), c => c.Count);
        var shareMap = shares.ToDictionary(s => (s.MediaKey, s.MediaType), s => s.Count);
        var myLikeSet = new HashSet<string>(myLikes);

        var result = keys.Select(k => new ModelMediaStatDto
        {
            MediaKey = k.MediaKey,
            MediaType = k.MediaType,
            SeenCount = viewMap.TryGetValue((k.MediaKey, k.MediaType), out var vc) ? vc : 0,
            LikeCount = likeMap.TryGetValue((k.MediaKey, k.MediaType), out var lc) ? lc : 0,
            CommentCount = commentMap.TryGetValue((k.MediaKey, k.MediaType), out var cc) ? cc : 0,
            ShareCount = shareMap.TryGetValue((k.MediaKey, k.MediaType), out var sc) ? sc : 0,
            LikedByMe = myLikeSet.Contains(k.MediaKey)
        }).ToList();

        return Ok(result);
    }

    /// <summary>Records a view event, deduped per visitor (member or guest token).</summary>
    [HttpPost("view")]
    [ProducesResponseType(typeof(ModelMediaViewResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ModelMediaViewResponse>> RecordView(
        [FromRoute] string modelSlug,
        [FromBody] RecordModelMediaEventRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidModelSlug(modelSlug))
            return BadRequest(new { error = "The model slug is invalid." });

        var mediaKey = request.MediaKey?.Trim() ?? string.Empty;
        var mediaType = request.MediaType?.Trim().ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrEmpty(mediaKey) || (mediaType != "photo" && mediaType != "video"))
            return BadRequest(new { error = "A valid mediaKey and mediaType (photo|video) are required." });

        var viewerId = await ResolveViewerIdAsync(cancellationToken);
        var visitorToken = string.IsNullOrWhiteSpace(request.ViewerToken) ? null : request.ViewerToken.Trim();

        bool alreadySeen;
        if (viewerId.HasValue)
        {
            alreadySeen = await _db.ModelMediaViews.AnyAsync(
                v => v.ModelSlug == modelSlug && v.MediaKey == mediaKey && v.ViewerId == viewerId.Value,
                cancellationToken);
        }
        else if (!string.IsNullOrEmpty(visitorToken))
        {
            alreadySeen = await _db.ModelMediaViews.AnyAsync(
                v => v.ModelSlug == modelSlug && v.MediaKey == mediaKey && v.VisitorToken == visitorToken,
                cancellationToken);
        }
        else
        {
            alreadySeen = false;
        }

        if (!alreadySeen)
        {
            _db.ModelMediaViews.Add(new ModelMediaView
            {
                ModelSlug = modelSlug,
                MediaType = mediaType,
                MediaKey = mediaKey,
                ViewerId = viewerId,
                VisitorToken = visitorToken,
                CreatedAtUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(cancellationToken);
        }

        var total = await _db.ModelMediaViews.CountAsync(
            v => v.ModelSlug == modelSlug && v.MediaKey == mediaKey,
            cancellationToken);

        return Ok(new ModelMediaViewResponse { SeenCount = total });
    }

    /// <summary>Records a share event, deduped per visitor.</summary>
    [HttpPost("share")]
    [ProducesResponseType(typeof(ModelMediaShareResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ModelMediaShareResponse>> RecordShare(
        [FromRoute] string modelSlug,
        [FromBody] RecordModelMediaEventRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidModelSlug(modelSlug))
            return BadRequest(new { error = "The model slug is invalid." });

        var mediaKey = request.MediaKey?.Trim() ?? string.Empty;
        var mediaType = request.MediaType?.Trim().ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrEmpty(mediaKey) || (mediaType != "photo" && mediaType != "video"))
            return BadRequest(new { error = "A valid mediaKey and mediaType (photo|video) are required." });

        var viewerId = await ResolveViewerIdAsync(cancellationToken);
        var visitorToken = string.IsNullOrWhiteSpace(request.ViewerToken) ? null : request.ViewerToken.Trim();

        bool alreadyShared;
        if (viewerId.HasValue)
        {
            alreadyShared = await _db.ModelMediaShares.AnyAsync(
                s => s.ModelSlug == modelSlug && s.MediaKey == mediaKey && s.ViewerId == viewerId.Value,
                cancellationToken);
        }
        else if (!string.IsNullOrEmpty(visitorToken))
        {
            alreadyShared = await _db.ModelMediaShares.AnyAsync(
                s => s.ModelSlug == modelSlug && s.MediaKey == mediaKey && s.VisitorToken == visitorToken,
                cancellationToken);
        }
        else
        {
            alreadyShared = false;
        }

        if (!alreadyShared)
        {
            _db.ModelMediaShares.Add(new ModelMediaShare
            {
                ModelSlug = modelSlug,
                MediaType = mediaType,
                MediaKey = mediaKey,
                ViewerId = viewerId,
                VisitorToken = visitorToken,
                CreatedAtUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(cancellationToken);
        }

        var total = await _db.ModelMediaShares.CountAsync(
            s => s.ModelSlug == modelSlug && s.MediaKey == mediaKey,
            cancellationToken);

        return Ok(new ModelMediaShareResponse { ShareCount = total });
    }

    /// <summary>Toggles a member's like on one photograph or clip.</summary>
    [Authorize]
    [HttpPost("like")]
    [ProducesResponseType(typeof(ModelMediaLikeResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ModelMediaLikeResponse>> ToggleLike(
        [FromRoute] string modelSlug,
        [FromBody] ToggleModelMediaLikeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidModelSlug(modelSlug))
            return BadRequest(new { error = "The model slug is invalid." });

        var mediaKey = request.MediaKey?.Trim() ?? string.Empty;
        var mediaType = request.MediaType?.Trim().ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrEmpty(mediaKey) || (mediaType != "photo" && mediaType != "video"))
            return BadRequest(new { error = "A valid mediaKey and mediaType (photo|video) are required." });

        var userId = User.GetUserId();
        var existing = await _db.ModelMediaLikes.FirstOrDefaultAsync(
            l => l.ModelSlug == modelSlug && l.MediaKey == mediaKey && l.UserId == userId,
            cancellationToken);

        bool likedByMe;
        if (existing != null)
        {
            _db.ModelMediaLikes.Remove(existing);
            likedByMe = false;
        }
        else
        {
            _db.ModelMediaLikes.Add(new ModelMediaLike
            {
                ModelSlug = modelSlug,
                MediaType = mediaType,
                MediaKey = mediaKey,
                UserId = userId,
                CreatedAtUtc = DateTime.UtcNow
            });
            likedByMe = true;
        }

        await _db.SaveChangesAsync(cancellationToken);

        var total = await _db.ModelMediaLikes.CountAsync(
            l => l.ModelSlug == modelSlug && l.MediaKey == mediaKey,
            cancellationToken);

        return Ok(new ModelMediaLikeResponse { LikedByMe = likedByMe, LikeCount = total });
    }

    /// <summary>Returns a page of comments for one photograph or clip, newest first.</summary>
    [HttpGet("comments")]
    [ProducesResponseType(typeof(PagedResult<ModelMediaCommentDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ModelMediaCommentDto>>> ListComments(
        [FromRoute] string modelSlug,
        [FromQuery] string mediaKey,
        [FromQuery] string mediaType = "photo",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidModelSlug(modelSlug))
            return BadRequest(new { error = "The model slug is invalid." });

        mediaKey = mediaKey?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(mediaKey))
            return BadRequest(new { error = "mediaKey is required." });

        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > MaximumPageSize) pageSize = MaximumPageSize;

        var query = _db.ModelMediaComments
            .AsNoTracking()
            .Where(c => c.ModelSlug == modelSlug && c.MediaKey == mediaKey)
            .Include(c => c.Author)
                .ThenInclude(author => author!.Country)
            .Include(c => c.Author)
                .ThenInclude(author => author!.City);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(c => c.CreatedAtUtc)
            .ThenByDescending(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Ok(new PagedResult<ModelMediaCommentDto>
        {
            Items = items.Select(ToCommentDto).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    /// <summary>Posts a comment on a photograph or clip as the signed-in active Community member.</summary>
    [Authorize]
    [HttpPost("comments")]
    [ProducesResponseType(typeof(ModelMediaCommentDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<ModelMediaCommentDto>> CreateComment(
        [FromRoute] string modelSlug,
        [FromBody] CreateModelMediaCommentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidModelSlug(modelSlug))
            return BadRequest(new { error = "The model slug is invalid." });

        var mediaKey = request.MediaKey?.Trim() ?? string.Empty;
        var mediaType = request.MediaType?.Trim().ToLowerInvariant() ?? string.Empty;
        var text = request.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(mediaKey) || (mediaType != "photo" && mediaType != "video"))
            return BadRequest(new { error = "A valid mediaKey and mediaType (photo|video) are required." });
        if (text.Length is < 1 or > 1000)
            return BadRequest(new { error = "A comment must contain between 1 and 1,000 characters." });

        var author = await _db.Users
            .Include(user => user.Country)
            .Include(user => user.City)
            .FirstOrDefaultAsync(user => user.Id == User.GetUserId(), cancellationToken);
        if (author == null || !string.Equals(author.Status, UserProfileStatus.Active, StringComparison.OrdinalIgnoreCase))
            return Forbid();

        var comment = new ModelMediaComment
        {
            ModelSlug = modelSlug,
            MediaType = mediaType,
            MediaKey = mediaKey,
            AuthorId = author.Id,
            Author = author,
            Text = text,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.ModelMediaComments.Add(comment);
        await _db.SaveChangesAsync(cancellationToken);

        var response = ToCommentDto(comment);
        return CreatedAtAction(
            nameof(ListComments),
            new { modelSlug, mediaKey = comment.MediaKey, page = 1, pageSize = 50 },
            response);
    }

    private async Task<int?> ResolveViewerIdAsync(CancellationToken cancellationToken)
    {
        if (User?.Identity?.IsAuthenticated != true) return null;
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(raw, out var userId)) return null;
        return await _db.Users.AnyAsync(user => user.Id == userId, cancellationToken) ? userId : null;
    }

    private static bool IsValidModelSlug(string? modelSlug) =>
        !string.IsNullOrWhiteSpace(modelSlug) &&
        modelSlug.Length <= 120 &&
        ModelSlugPattern.IsMatch(modelSlug);

    private static ModelMediaCommentDto ToCommentDto(ModelMediaComment comment) => new()
    {
        Id = comment.Id,
        MediaKey = comment.MediaKey,
        MediaType = comment.MediaType,
        Text = comment.Text,
        CreatedAtUtc = comment.CreatedAtUtc,
        Author = comment.Author == null ? new AuthorDto() : AuthorMapper.From(comment.Author)
    };
}
