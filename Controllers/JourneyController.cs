using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NeverBeen.API.Common;
using NeverBeen.API.Data;
using NeverBeen.API.Dtos;
using NeverBeen.API.Entities;

namespace NeverBeen.API.Controllers;

/// <summary>
/// Journey feeds: posts (photos, location, mood, hashtags, audience, shares, wall
/// posts), nested comments, 11 hold reactions, tags and "hide from my feed".
/// </summary>
[Route("api/journey")]
[ApiController]
public class JourneyController : ControllerBase
{
    private readonly AppDbContext _db;

    public JourneyController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>Feed of the signed-in member (or a single traveler's wall with ?authorId=).</summary>
    [Authorize]
    [HttpGet]
    public async Task<ActionResult<PagedResult<JourneyPostDto>>> GetFeed(
        [FromQuery] int? authorId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var myId = User.GetUserId();
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 50) pageSize = 10;

        var companionIds = await ConnectedCompanionIds(myId, cancellationToken);
        var blockedIds = await BlockedIds(myId, cancellationToken);
        // Requirement A: following a traveler pulls that traveler's posts into this
        // member's Journey feed, so B's posts start appearing as soon as A follows B.
        var followedIds = await _db.Follows.AsNoTracking()
            .Where(f => f.FollowerId == myId)
            .Select(f => f.FolloweeId)
            .ToListAsync(cancellationToken);

        var query = _db.JourneyPosts.AsNoTracking()
            .Where(p => !_db.HiddenPosts.Any(h => h.UserId == myId && h.PostId == p.Id))
            .Where(p => !blockedIds.Contains(p.AuthorId));

        if (authorId.HasValue)
        {
            var target = authorId.Value;
            query = query.Where(p => p.AuthorId == target || p.WallOwnerId == target);
        }
        else
        {
            query = query.Where(p =>
                p.AuthorId == myId ||
                p.WallOwnerId == myId ||
                companionIds.Contains(p.AuthorId) ||
                followedIds.Contains(p.AuthorId));
        }

        query = query.Where(p =>
            p.AuthorId == myId ||
            p.AudienceMode == JourneyPostAudience.Public ||
            (p.AudienceMode == JourneyPostAudience.Companions && (companionIds.Contains(p.AuthorId) || followedIds.Contains(p.AuthorId))) ||
            (p.AudienceMode == JourneyPostAudience.Custom &&
                _db.JourneyPostAudienceEntries.Any(a => a.PostId == p.Id && a.UserId == myId && a.Kind == "allow") &&
                !_db.JourneyPostAudienceEntries.Any(a => a.PostId == p.Id && a.UserId == myId && a.Kind == "deny")));

        var totalCount = await query.CountAsync(cancellationToken);
        var posts = await query
            .OrderByDescending(p => p.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = await BuildPostDtos(posts, myId, cancellationToken);
        return Ok(new PagedResult<JourneyPostDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    /// <summary>One Journey post with its comment tree.</summary>
    [Authorize]
    [HttpGet("{id:long}")]
    public async Task<ActionResult<JourneyPostDto>> GetPost(long id, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        var post = await _db.JourneyPosts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (post == null)
            return NotFound();

        var dto = (await BuildPostDtos(new List<JourneyPost> { post }, myId, cancellationToken, includeComments: true)).First();
        return Ok(dto);
    }

    /// <summary>Creates a Journey post (or a share of another post when OriginalPostId is set).</summary>
    [Authorize]
    [HttpPost]
    public async Task<ActionResult<JourneyPostDto>> Create([FromBody] SaveJourneyPostRequest request, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        if (string.IsNullOrWhiteSpace(request.Text) && (request.ImageUrls == null || request.ImageUrls.Count == 0))
            return BadRequest(new { error = "A Journey post needs text or at least one photo." });

        if (request.WallOwnerId.HasValue && request.WallOwnerId.Value != myId)
        {
            var wallOwner = await _db.Users.FindAsync(new object?[] { request.WallOwnerId.Value }, cancellationToken);
            if (wallOwner == null)
                return BadRequest(new { error = "Unknown wall owner." });
        }

        var post = new JourneyPost
        {
            AuthorId = myId,
            WallOwnerId = request.WallOwnerId,
            Text = request.Text?.Trim() ?? string.Empty,
            ImageUrls = request.ImageUrls?.ToArray(),
            Location = request.Location,
            Mood = request.Mood,
            PlaceId = request.PlaceId,
            Hashtags = request.Hashtags?.ToArray(),
            AudienceMode = request.Audience?.Mode ?? JourneyPostAudience.Public,
            OriginalPostId = request.OriginalPostId,
            SharedText = request.SharedText
        };

        _db.JourneyPosts.Add(post);
        await _db.SaveChangesAsync(cancellationToken);

        await SaveAudience(post.Id, request.Audience, cancellationToken);
        await ApplyTags(post.Id, request.TaggedCompanionIds, replace: false, cancellationToken);
        if (request.OriginalPostId.HasValue)
        {
            var original = await _db.JourneyPosts.FindAsync(new object?[] { request.OriginalPostId.Value }, cancellationToken);
            if (original != null)
            {
                original.ShareCount++;
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        var dto = (await BuildPostDtos(new List<JourneyPost> { post }, myId, cancellationToken, includeComments: true)).First();
        return CreatedAtAction(nameof(GetPost), new { id = post.Id }, dto);
    }

    /// <summary>Edits the text, audience, mood / location of own post.</summary>
    [Authorize]
    [HttpPut("{id:long}")]
    public async Task<ActionResult<JourneyPostDto>> Update(long id, [FromBody] SaveJourneyPostRequest request, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        var post = await _db.JourneyPosts.FirstOrDefaultAsync(p => p.Id == id && p.AuthorId == myId, cancellationToken);
        if (post == null)
            return NotFound();

        post.Text = request.Text?.Trim() ?? post.Text;
        if (request.ImageUrls != null) post.ImageUrls = request.ImageUrls.ToArray();
        post.Location = request.Location;
        post.Mood = request.Mood;
        post.PlaceId = request.PlaceId;
        if (request.Hashtags != null) post.Hashtags = request.Hashtags.ToArray();
        if (request.Audience != null) post.AudienceMode = request.Audience.Mode;
        post.IsEdited = true;
        post.EditedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        await SaveAudience(post.Id, request.Audience, cancellationToken);
        if (request.TaggedCompanionIds != null)
            await ApplyTags(post.Id, request.TaggedCompanionIds, replace: true, cancellationToken);

        var dto = (await BuildPostDtos(new List<JourneyPost> { post }, myId, cancellationToken, includeComments: true)).First();
        return Ok(dto);
    }

    /// <summary>Deletes own post (and its comments / reactions / tags).</summary>
    [Authorize]
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        var post = await _db.JourneyPosts.FirstOrDefaultAsync(p => p.Id == id && p.AuthorId == myId, cancellationToken);
        if (post == null)
            return NotFound();

        _db.JourneyPosts.Remove(post);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Sets the signed-in member's reaction (re-submitting the same type removes it).</summary>
    [Authorize]
    [HttpPost("{id:long}/reactions")]
    public async Task<ActionResult> React(long id, [FromBody] ReactRequest request, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        if (!JourneyReactionTypes.All.Contains(request.ReactionType))
            return BadRequest(new { error = $"Reaction must be one of: {string.Join(", ", JourneyReactionTypes.All)}." });

        var post = await _db.JourneyPosts.FindAsync(new object?[] { id }, cancellationToken);
        if (post == null)
            return NotFound();

        var existing = await _db.JourneyPostReactions
            .FirstOrDefaultAsync(r => r.PostId == id && r.UserId == myId, cancellationToken);

        if (existing != null && existing.ReactionType == request.ReactionType)
        {
            _db.JourneyPostReactions.Remove(existing);
            post.LikeCount = Math.Max(0, post.LikeCount - 1);
        }
        else if (existing != null)
        {
            existing.ReactionType = request.ReactionType;
            existing.CreatedAtUtc = DateTime.UtcNow;
        }
        else
        {
            _db.JourneyPostReactions.Add(new JourneyPostReaction
            {
                PostId = id,
                UserId = myId,
                ReactionType = request.ReactionType
            });
            post.LikeCount++;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new
        {
            likeCount = post.LikeCount,
            myReaction = await _db.JourneyPostReactions
                .Where(r => r.PostId == id && r.UserId == myId)
                .Select(r => r.ReactionType)
                .FirstOrDefaultAsync(cancellationToken)
        });
    }

    /// <summary>Everyone who reacted to a post (drives the "top reaction" badge and the likers modal).</summary>
    [Authorize]
    [HttpGet("{id:long}/reactions")]
    public async Task<ActionResult<List<ReactionDto>>> GetReactions(long id, CancellationToken cancellationToken)
    {
        var rows = await _db.JourneyPostReactions.AsNoTracking()
            .Where(r => r.PostId == id)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var userIds = rows.Select(r => r.UserId).Distinct().ToList();
        var users = await _db.Users.AsNoTracking()
            .Include(u => u.Country).Include(u => u.City)
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        return Ok(rows
            .Where(r => users.ContainsKey(r.UserId))
            .Select(r => new ReactionDto
            {
                User = AuthorMapper.From(users[r.UserId]),
                Type = r.ReactionType,
                ReactedAtUtc = r.CreatedAtUtc
            })
            .ToList());
    }

    /// <summary>Adds a comment (or a reply when ParentId is set) to a Journey post.</summary>
    [Authorize]
    [HttpPost("{id:long}/comments")]
    public async Task<ActionResult<JourneyCommentDto>> Comment(long id, [FromBody] CreateJourneyCommentRequest request, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { error = "Comment text is required." });

        var post = await _db.JourneyPosts.FindAsync(new object?[] { id }, cancellationToken);
        if (post == null)
            return NotFound();

        if (request.ParentId.HasValue)
        {
            var parent = await _db.JourneyComments.FindAsync(new object?[] { request.ParentId.Value }, cancellationToken);
            if (parent == null || parent.PostId != id)
                return BadRequest(new { error = "Unknown parent comment." });
        }

        var comment = new JourneyComment
        {
            PostId = id,
            AuthorId = myId,
            ParentId = request.ParentId,
            Text = request.Text.Trim(),
            ImageUrl = request.ImageUrl
        };
        _db.JourneyComments.Add(comment);
        post.CommentCount++;
        await _db.SaveChangesAsync(cancellationToken);

        var author = await _db.Users.Include(u => u.Country).Include(u => u.City)
            .FirstAsync(u => u.Id == myId, cancellationToken);
        return Ok(new JourneyCommentDto
        {
            Id = comment.Id,
            PostId = comment.PostId,
            ParentId = comment.ParentId,
            Text = comment.Text,
            ImageUrl = comment.ImageUrl,
            CreatedAtUtc = comment.CreatedAtUtc,
            Author = AuthorMapper.From(author)
        });
    }

    /// <summary>Comment tree of a post: top-level comments, each with its replies.</summary>
    [Authorize]
    [HttpGet("{id:long}/comments")]
    public async Task<ActionResult<List<JourneyCommentDto>>> GetComments(long id, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        var rows = await _db.JourneyComments.AsNoTracking()
            .Where(c => c.PostId == id)
            .OrderBy(c => c.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return Ok(await BuildCommentTree(rows, myId, cancellationToken));
    }

    /// <summary>Deletes own comment / reply (and its replies).</summary>
    [Authorize]
    [HttpDelete("comments/{id:long}")]
    public async Task<IActionResult> DeleteComment(long id, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        var comment = await _db.JourneyComments
            .FirstOrDefaultAsync(c => c.Id == id && c.AuthorId == myId, cancellationToken);
        if (comment == null)
            return NotFound();

        var post = await _db.JourneyPosts.FindAsync(new object?[] { comment.PostId }, cancellationToken);

        // Remove the whole reply subtree explicitly (self-reference is Restrict).
        var replyIds = await _db.JourneyComments
            .Where(c => c.ParentId == id)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);
        if (replyIds.Count > 0)
        {
            var replies = await _db.JourneyComments.Where(c => replyIds.Contains(c.Id)).ToListAsync(cancellationToken);
            _db.JourneyComments.RemoveRange(replies);
            if (post != null) post.CommentCount = Math.Max(0, post.CommentCount - replies.Count);
        }

        _db.JourneyComments.Remove(comment);
        if (post != null) post.CommentCount = Math.Max(0, post.CommentCount - 1);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Sets the signed-in member's reaction on a comment (toggle).</summary>
    [Authorize]
    [HttpPost("comments/{id:long}/reactions")]
    public async Task<ActionResult> ReactToComment(long id, [FromBody] ReactRequest request, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        if (!JourneyReactionTypes.All.Contains(request.ReactionType))
            return BadRequest(new { error = $"Reaction must be one of: {string.Join(", ", JourneyReactionTypes.All)}." });

        var comment = await _db.JourneyComments.FindAsync(new object?[] { id }, cancellationToken);
        if (comment == null)
            return NotFound();

        var existing = await _db.JourneyCommentReactions
            .FirstOrDefaultAsync(r => r.CommentId == id && r.UserId == myId, cancellationToken);

        if (existing != null && existing.ReactionType == request.ReactionType)
        {
            _db.JourneyCommentReactions.Remove(existing);
            comment.LikeCount = Math.Max(0, comment.LikeCount - 1);
        }
        else if (existing != null)
        {
            existing.ReactionType = request.ReactionType;
        }
        else
        {
            _db.JourneyCommentReactions.Add(new JourneyCommentReaction
            {
                CommentId = id,
                UserId = myId,
                ReactionType = request.ReactionType
            });
            comment.LikeCount++;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { likeCount = comment.LikeCount });
    }

    /// <summary>Tags a companion in a post.</summary>
    [Authorize]
    [HttpPost("{id:long}/tags/{userId:int}")]
    public async Task<IActionResult> Tag(long id, int userId, CancellationToken cancellationToken)
    {
        var post = await _db.JourneyPosts.FindAsync(new object?[] { id }, cancellationToken);
        if (post == null)
            return NotFound();

        var exists = await _db.JourneyPostTags.AnyAsync(t => t.PostId == id && t.UserId == userId, cancellationToken);
        if (!exists)
        {
            _db.JourneyPostTags.Add(new JourneyPostTag { PostId = id, UserId = userId });
            await _db.SaveChangesAsync(cancellationToken);
        }
        return NoContent();
    }

    /// <summary>Removes a tag from a post.</summary>
    [Authorize]
    [HttpDelete("{id:long}/tags/{userId:int}")]
    public async Task<IActionResult> Untag(long id, int userId, CancellationToken cancellationToken)
    {
        var rows = await _db.JourneyPostTags.Where(t => t.PostId == id && t.UserId == userId).ToListAsync(cancellationToken);
        if (rows.Count == 0)
            return NotFound();
        _db.JourneyPostTags.RemoveRange(rows);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Hides a post from the signed-in member's own feed (the post stays for others).</summary>
    [Authorize]
    [HttpPost("{id:long}/hide")]
    public async Task<IActionResult> Hide(long id, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        if (!await _db.JourneyPosts.AnyAsync(p => p.Id == id, cancellationToken))
            return NotFound();

        if (!await _db.HiddenPosts.AnyAsync(h => h.UserId == myId && h.PostId == id, cancellationToken))
        {
            _db.HiddenPosts.Add(new HiddenPost { UserId = myId, PostId = id });
            await _db.SaveChangesAsync(cancellationToken);
        }
        return NoContent();
    }

    /// <summary>
    /// Applies the tagged companions of a post. With <paramref name="replace"/> the
    /// stored tag set is replaced (edit), otherwise ids are added that are not tagged
    /// yet (create). Tagging yourself is allowed; unknown ids are ignored.
    /// </summary>
    private async Task ApplyTags(long postId, List<int>? userIds, bool replace, CancellationToken cancellationToken)
    {
        if (userIds == null || userIds.Count == 0)
        {
            if (replace)
            {
                var stale = await _db.JourneyPostTags.Where(t => t.PostId == postId).ToListAsync(cancellationToken);
                if (stale.Count > 0)
                {
                    _db.JourneyPostTags.RemoveRange(stale);
                    await _db.SaveChangesAsync(cancellationToken);
                }
            }
            return;
        }

        var knownIds = (await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => u.Id)
            .ToListAsync(cancellationToken)).ToHashSet();

        var existing = await _db.JourneyPostTags
            .Where(t => t.PostId == postId)
            .ToListAsync(cancellationToken);

        if (replace)
        {
            var keep = existing.Where(t => knownIds.Contains(t.UserId) && userIds.Contains(t.UserId)).ToList();
            var dropped = existing.Except(keep).ToList();
            if (dropped.Count > 0)
                _db.JourneyPostTags.RemoveRange(dropped);
            existing = keep;
        }

        foreach (var userId in userIds)
        {
            if (!knownIds.Contains(userId) || existing.Any(t => t.UserId == userId))
                continue;
            _db.JourneyPostTags.Add(new JourneyPostTag { PostId = postId, UserId = userId });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    // ------------------------------------------------------------------ helpers

    private async Task SaveAudience(long postId, PostAudienceDto? audience, CancellationToken cancellationToken)
    {
        if (audience == null || audience.Mode != JourneyPostAudience.Custom)
        {
            var old = await _db.JourneyPostAudienceEntries.Where(a => a.PostId == postId).ToListAsync(cancellationToken);
            _db.JourneyPostAudienceEntries.RemoveRange(old);
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        var existing = await _db.JourneyPostAudienceEntries.Where(a => a.PostId == postId).ToListAsync(cancellationToken);
        _db.JourneyPostAudienceEntries.RemoveRange(existing);

        foreach (var userId in audience.AllowIds ?? new List<int>())
            _db.JourneyPostAudienceEntries.Add(new JourneyPostAudienceEntry { PostId = postId, UserId = userId, Kind = "allow" });
        foreach (var userId in audience.DenyIds ?? new List<int>())
            _db.JourneyPostAudienceEntries.Add(new JourneyPostAudienceEntry { PostId = postId, UserId = userId, Kind = "deny" });
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<List<int>> ConnectedCompanionIds(int userId, CancellationToken cancellationToken)
        => await _db.Companionships.AsNoTracking()
            .Where(c => c.Status == CompanionshipStatus.Connected && (c.UserId == userId || c.CompanionId == userId))
            .Select(c => c.UserId == userId ? c.CompanionId : c.UserId)
            .ToListAsync(cancellationToken);

    private async Task<List<int>> BlockedIds(int userId, CancellationToken cancellationToken)
        => await _db.BlockedUsers.AsNoTracking()
            .Where(b => b.UserId == userId || b.BlockedUserId == userId)
            .Select(b => b.UserId == userId ? b.BlockedUserId : b.UserId)
            .ToListAsync(cancellationToken);

    private async Task<List<JourneyPostDto>> BuildPostDtos(
        List<JourneyPost> posts, int myId, CancellationToken cancellationToken, bool includeComments = false)
    {
        if (posts.Count == 0)
            return new List<JourneyPostDto>();

        var postIds = posts.Select(p => p.Id).ToList();
        var userIds = posts.Select(p => p.AuthorId)
            .Concat(posts.Where(p => p.WallOwnerId.HasValue).Select(p => p.WallOwnerId!.Value))
            .Distinct()
            .ToList();

        var users = await _db.Users.AsNoTracking()
            .Include(u => u.Country).Include(u => u.City)
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        var myReactions = await _db.JourneyPostReactions.AsNoTracking()
            .Where(r => postIds.Contains(r.PostId) && r.UserId == myId)
            .ToDictionaryAsync(r => r.PostId, r => r.ReactionType, cancellationToken);

        var reactionRows = await _db.JourneyPostReactions.AsNoTracking()
            .Where(r => postIds.Contains(r.PostId))
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        var reactionsByPost = reactionRows
            .GroupBy(r => r.PostId)
            .ToDictionary(g => g.Key, g => g.Take(10).ToList());

        var tagRows = await _db.JourneyPostTags.AsNoTracking()
            .Where(t => postIds.Contains(t.PostId))
            .ToListAsync(cancellationToken);
        var tagsByPost = tagRows
            .GroupBy(t => t.PostId)
            .ToDictionary(g => g.Key, g => g.Select(t => t.UserId).ToList());

        var originalIds = posts.Where(p => p.OriginalPostId.HasValue).Select(p => p.OriginalPostId!.Value).Distinct().ToList();
        var originals = originalIds.Count == 0
            ? new Dictionary<long, JourneyPost>()
            : await _db.JourneyPosts.AsNoTracking().Where(p => originalIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);

        Dictionary<long, List<JourneyComment>> commentsByPost = new();
        if (includeComments)
        {
            var comments = await _db.JourneyComments.AsNoTracking()
                .Where(c => postIds.Contains(c.PostId))
                .OrderBy(c => c.CreatedAtUtc)
                .ToListAsync(cancellationToken);
            commentsByPost = comments.GroupBy(c => c.PostId).ToDictionary(g => g.Key, g => g.ToList());
        }

        var dtos = new List<JourneyPostDto>();
        foreach (var post in posts)
        {
            users.TryGetValue(post.AuthorId, out var author);
            var dto = new JourneyPostDto
            {
                Id = post.Id,
                Author = author != null ? AuthorMapper.From(author) : new AuthorDto { Id = post.AuthorId },
                Text = post.Text,
                CreatedAtUtc = post.CreatedAtUtc,
                ImageUrls = post.ImageUrls?.ToList() ?? new List<string>(),
                ImageUrl = post.ImageUrls?.FirstOrDefault(),
                Location = post.Location,
                Mood = post.Mood,
                PlaceId = post.PlaceId,
                Hashtags = post.Hashtags?.ToList() ?? new List<string>(),
                LikeCount = post.LikeCount,
                ShareCount = post.ShareCount,
                SharesCount = post.ShareCount,
                CommentCount = post.CommentCount,
                IsLiked = myReactions.ContainsKey(post.Id),
                MyReaction = myReactions.TryGetValue(post.Id, out var mine) ? mine : null,
                IsShared = post.OriginalPostId.HasValue,
                SharedText = post.SharedText,
                WallOwnerId = post.WallOwnerId,
                WallOwnerName = post.WallOwnerId.HasValue && users.TryGetValue(post.WallOwnerId.Value, out var wallOwner)
                    ? wallOwner.FullName
                    : null,
                EditedAtUtc = post.EditedAtUtc
            };

            if (reactionsByPost.TryGetValue(post.Id, out var reactions))
            {
                dto.Reactions = reactions
                    .Where(r => users.TryGetValue(r.UserId, out _))
                    .Select(r => new ReactionDto
                    {
                        User = AuthorMapper.From(users[r.UserId]),
                        Type = r.ReactionType,
                        ReactedAtUtc = r.CreatedAtUtc
                    })
                    .ToList();
            }

            if (tagsByPost.TryGetValue(post.Id, out var taggedIds))
            {
                dto.TaggedCompanions = taggedIds
                    .Distinct()
                    .Select(id => users.TryGetValue(id, out var u) ? AuthorMapper.From(u) : new AuthorDto { Id = id })
                    .ToList();
            }

            if (post.AudienceMode == JourneyPostAudience.Custom)
            {
                var entries = await _db.JourneyPostAudienceEntries.AsNoTracking()
                    .Where(a => a.PostId == post.Id)
                    .ToListAsync(cancellationToken);
                dto.Audience = new PostAudienceDto
                {
                    Mode = JourneyPostAudience.Custom,
                    AllowIds = entries.Where(a => a.Kind == "allow").Select(a => a.UserId).ToList(),
                    DenyIds = entries.Where(a => a.Kind == "deny").Select(a => a.UserId).ToList()
                };
            }
            else
            {
                dto.Audience = new PostAudienceDto { Mode = post.AudienceMode };
            }

            if (post.OriginalPostId.HasValue && originals.TryGetValue(post.OriginalPostId.Value, out var original))
            {
                users.TryGetValue(original.AuthorId, out var originalAuthor);
                dto.OriginalPost = new JourneyPostDto
                {
                    Id = original.Id,
                    Author = originalAuthor != null ? AuthorMapper.From(originalAuthor) : new AuthorDto { Id = original.AuthorId },
                    Text = original.Text,
                    CreatedAtUtc = original.CreatedAtUtc,
                    ImageUrls = original.ImageUrls?.ToList() ?? new List<string>(),
                    ImageUrl = original.ImageUrls?.FirstOrDefault(),
                    Location = original.Location,
                    Mood = original.Mood,
                    Hashtags = original.Hashtags?.ToList() ?? new List<string>(),
                    LikeCount = original.LikeCount,
                    ShareCount = original.ShareCount
                };
            }

            if (includeComments && commentsByPost.TryGetValue(post.Id, out var commentRows))
            {
                dto.Comments = await BuildCommentTree(commentRows, myId, cancellationToken);
            }

            dtos.Add(dto);
        }

        return dtos;
    }

    private async Task<List<JourneyCommentDto>> BuildCommentTree(
        List<JourneyComment> rows, int myId, CancellationToken cancellationToken)
    {
        var userIds = rows.Select(c => c.AuthorId).Distinct().ToList();
        var users = await _db.Users.AsNoTracking()
            .Include(u => u.Country).Include(u => u.City)
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        var commentIds = rows.Select(c => c.Id).ToList();
        var myReactions = await _db.JourneyCommentReactions.AsNoTracking()
            .Where(r => commentIds.Contains(r.CommentId) && r.UserId == myId)
            .ToDictionaryAsync(r => r.CommentId, r => r.ReactionType, cancellationToken);

        var commentReactionRows = await _db.JourneyCommentReactions.AsNoTracking()
            .Where(r => commentIds.Contains(r.CommentId))
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        var reactionsByComment = commentReactionRows
            .GroupBy(r => r.CommentId)
            .ToDictionary(g => g.Key, g => g.Take(10).ToList());

        JourneyCommentDto Map(JourneyComment c)
        {
            users.TryGetValue(c.AuthorId, out var author);
            var dto = new JourneyCommentDto
            {
                Id = c.Id,
                PostId = c.PostId,
                ParentId = c.ParentId,
                Text = c.Text,
                ImageUrl = c.ImageUrl,
                CreatedAtUtc = c.CreatedAtUtc,
                LikeCount = c.LikeCount,
                Author = author != null ? AuthorMapper.From(author) : new AuthorDto { Id = c.AuthorId },
                IsLiked = myReactions.ContainsKey(c.Id),
                MyReaction = myReactions.TryGetValue(c.Id, out var mine) ? mine : null
            };
            if (reactionsByComment.TryGetValue(c.Id, out var reactions))
            {
                dto.Reactions = reactions
                    .Where(r => users.ContainsKey(r.UserId))
                    .Select(r => new ReactionDto
                    {
                        User = AuthorMapper.From(users[r.UserId]),
                        Type = r.ReactionType,
                        ReactedAtUtc = r.CreatedAtUtc
                    })
                    .ToList();
            }
            return dto;
        }

        var byParent = rows.Where(c => c.ParentId != null).GroupBy(c => c.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        void AttachReplies(JourneyCommentDto dto)
        {
            if (byParent.TryGetValue(dto.Id, out var replies))
            {
                foreach (var reply in replies)
                {
                    var replyDto = Map(reply);
                    AttachReplies(replyDto);
                    dto.Replies.Add(replyDto);
                }
            }
        }

        var roots = new List<JourneyCommentDto>();
        foreach (var row in rows.Where(c => c.ParentId == null))
        {
            var dto = Map(row);
            AttachReplies(dto);
            roots.Add(dto);
        }

        await Task.CompletedTask;
        return roots;
    }
}
