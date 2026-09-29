using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NeverBeen.API.Common;
using NeverBeen.API.Data;
using NeverBeen.API.Dtos;
using NeverBeen.API.Entities;

namespace NeverBeen.API.Controllers;

/// <summary>
/// The NeverBeen Community Message Book:
///  - every member can post a text comment (top-level post),
///  - the poster's details (name, photo, profession) are shown with the comment,
///  - other members can reply to a post (one level of nesting),
///  - members can like or dislike any comment (toggle behaviour).
/// Reading is open; posting, reacting and deleting require a JWT.
/// </summary>
[ApiController]
[Route("api/messagebook")]
public class MessageBookController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<MessageBookController> _logger;

    public MessageBookController(AppDbContext db, ILogger<MessageBookController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Paged list of top-level posts, newest first, with replies and the current
    /// visitor's reactions included.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<CommentDto>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool includeReplies = true,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var topLevel = await _db.CommunityComments
            .AsNoTracking()
            .Where(c => c.ParentId == null)
            .Include(c => c.Author)
            .OrderByDescending(c => c.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var totalCount = await _db.CommunityComments
            .CountAsync(c => c.ParentId == null, cancellationToken);

        var commentIds = topLevel.Select(c => c.Id).ToList();
        var myReactions = new Dictionary<int, int>();
        var replyLookup = new Dictionary<int, List<CommunityComment>>();

        if (commentIds.Count > 0)
        {
            var replies = await _db.CommunityComments
                .AsNoTracking()
                .Where(c => c.ParentId != null && commentIds.Contains(c.ParentId.Value))
                .Include(c => c.Author)
                .OrderBy(c => c.CreatedAtUtc)
                .ToListAsync(cancellationToken);

            replyLookup = replies
                .GroupBy(r => r.ParentId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            var allIds = commentIds.Concat(replies.Select(r => r.Id)).ToList();
            myReactions = await LoadMyReactionsAsync(allIds, cancellationToken);
        }

        var items = topLevel.Select(comment =>
        {
            var dto = CommentMapper.Map(comment, myReactions);
            dto.ReplyCount = replyLookup.TryGetValue(comment.Id, out var replyList) ? replyList.Count : 0;
            if (includeReplies && replyLookup.TryGetValue(comment.Id, out var list))
                dto.Replies = list.Select(r => CommentMapper.Map(r, myReactions)).ToList();
            return dto;
        }).ToList();

        return Ok(new PagedResult<CommentDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    /// <summary>A single top-level post with all of its replies.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<CommentDto>> Get(int id, CancellationToken cancellationToken)
    {
        var comment = await _db.CommunityComments
            .AsNoTracking()
            .Include(c => c.Author)
            .FirstOrDefaultAsync(c => c.Id == id && c.ParentId == null, cancellationToken);
        if (comment == null)
            return NotFound();

        var replies = await _db.CommunityComments
            .AsNoTracking()
            .Where(c => c.ParentId == id)
            .Include(c => c.Author)
            .OrderBy(c => c.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var myReactions = await LoadMyReactionsAsync(
            new List<int> { comment.Id }.Concat(replies.Select(r => r.Id)).ToList(),
            cancellationToken);

        var dto = CommentMapper.Map(comment, myReactions);
        dto.ReplyCount = replies.Count;
        dto.Replies = replies.Select(r => CommentMapper.Map(r, myReactions)).ToList();
        return Ok(dto);
    }

    /// <summary>Creates a new top-level post, or a reply when ParentId is supplied.</summary>
    [Authorize]
    [HttpPost]
    [ProducesResponseType(typeof(CommentDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<CommentDto>> Create([FromBody] CommentCreateRequest request, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();

        if (request.ParentId.HasValue)
        {
            var parent = await _db.CommunityComments
                .FirstOrDefaultAsync(c => c.Id == request.ParentId.Value, cancellationToken);
            if (parent == null)
                return NotFound(new { error = "The parent comment was not found." });
            if (parent.ParentId.HasValue)
                return BadRequest(new { error = "Replies to replies are not supported. Post your comment at the top level of the message book." });
        }

        var comment = new CommunityComment
        {
            Text = request.Text.Trim(),
            AuthorId = userId,
            ParentId = request.ParentId,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.CommunityComments.Add(comment);
        await _db.SaveChangesAsync(cancellationToken);

        var created = await _db.CommunityComments
            .AsNoTracking()
            .Include(c => c.Author)
            .FirstAsync(c => c.Id == comment.Id, cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = comment.Id },
            CommentMapper.Map(created, new Dictionary<int, int>()));
    }

    /// <summary>
    /// Likes or dislikes a comment. Sending the same type again removes the reaction
    /// (toggle); sending the opposite type switches it.
    /// </summary>
    [Authorize]
    [HttpPost("{id:int}/reactions")]
    public async Task<ActionResult<ReactionResultDto>> React(int id, [FromBody] ReactionRequest request, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();

        if (!ReactionTypes.NameToType.TryGetValue(request.Type, out var type))
            return BadRequest(new { error = "'type' must be one of: Like, Dislike, Love, Smile, Laugh, Cry, Heart, Clapping, Confused, Shocked, Angry, Fire." });

        var comment = await _db.CommunityComments
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (comment == null)
            return NotFound();

        var existing = await _db.CommentReactions
            .FirstOrDefaultAsync(r => r.CommentId == id && r.UserId == userId, cancellationToken);

        string? myReactionAfter;
        if (existing == null)
        {
            _db.CommentReactions.Add(new CommentReaction
            {
                CommentId = id,
                UserId = userId,
                ReactionType = type,
                CreatedAtUtc = DateTime.UtcNow
            });
            if (type == ReactionTypes.Dislike) comment.DislikeCount += 1; else comment.LikeCount += 1;
            myReactionAfter = ReactionTypes.TypeToName.GetValueOrDefault(type, "Like");
        }
        else if (existing.ReactionType == type)
        {
            // Same reaction again -> toggle off.
            _db.CommentReactions.Remove(existing);
            if (type == ReactionTypes.Dislike) comment.DislikeCount -= 1; else comment.LikeCount -= 1;
            myReactionAfter = null;
        }
        else
        {
            // Switch reaction.
            var previousType = existing.ReactionType;
            existing.ReactionType = type;
            if (previousType == ReactionTypes.Dislike) comment.DislikeCount -= 1; else comment.LikeCount -= 1;
            if (type == ReactionTypes.Dislike) comment.DislikeCount += 1; else comment.LikeCount += 1;
            myReactionAfter = ReactionTypes.TypeToName.GetValueOrDefault(type, "Like");
        }

        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new ReactionResultDto
        {
            LikeCount = comment.LikeCount,
            DislikeCount = comment.DislikeCount,
            MyReaction = myReactionAfter
        });
    }

    /// <summary>Deletes the signed-in user's own comment (replies and reactions are removed with it).</summary>
    [Authorize]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        var comment = await _db.CommunityComments
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (comment == null)
            return NotFound();
        if (comment.AuthorId != userId)
            return Forbid();

        var replyIds = await _db.CommunityComments
            .Where(r => r.ParentId == id)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var allIds = new List<int> { id };
        allIds.AddRange(replyIds);

        await _db.CommentReactions
            .Where(r => allIds.Contains(r.CommentId))
            .ExecuteDeleteAsync(cancellationToken);

        if (replyIds.Count > 0)
            await _db.CommunityComments
                .Where(r => replyIds.Contains(r.Id))
                .ExecuteDeleteAsync(cancellationToken);

        _db.CommunityComments.Remove(comment);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<Dictionary<int, int>> LoadMyReactionsAsync(List<int> commentIds, CancellationToken cancellationToken)
    {
        if (commentIds.Count == 0)
            return new Dictionary<int, int>();

        if (User.Identity?.IsAuthenticated != true)
            return new Dictionary<int, int>();

        var userId = User.GetUserId();
        return await _db.CommentReactions
            .AsNoTracking()
            .Where(r => r.UserId == userId && commentIds.Contains(r.CommentId))
            .ToDictionaryAsync(r => r.CommentId, r => r.ReactionType, cancellationToken);
    }
}
