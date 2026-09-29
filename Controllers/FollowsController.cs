using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NeverBeen.API.Common;
using NeverBeen.API.Data;
using NeverBeen.API.Dtos;
using NeverBeen.API.Entities;

namespace NeverBeen.API.Controllers;

/// <summary>Followers / following: follow, unfollow, lists and counters.</summary>
[Route("api/follows")]
[ApiController]
[Authorize]
public class FollowsController : ControllerBase
{
    private readonly AppDbContext _db;

    public FollowsController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>Follows (and counters) between the signed-in member and ?userId=.</summary>
    [HttpGet("counts/{userId:int}")]
    public async Task<ActionResult<FollowCountsDto>> GetCounts(int userId, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        var followers = await _db.Follows.CountAsync(f => f.FolloweeId == userId, cancellationToken);
        var following = await _db.Follows.CountAsync(f => f.FollowerId == userId, cancellationToken);
        var isFollowing = await _db.Follows.AnyAsync(f => f.FollowerId == myId && f.FolloweeId == userId, cancellationToken);
        return Ok(new FollowCountsDto { Followers = followers, Following = following, IsFollowing = isFollowing });
    }

    /// <summary>Members following the signed-in member (or ?userId=).</summary>
    [HttpGet("followers")]
    public async Task<ActionResult<List<FollowDto>>> GetFollowers([FromQuery] int? userId, CancellationToken cancellationToken)
    {
        var targetId = userId ?? User.GetUserId();
        var rows = await _db.Follows.AsNoTracking()
            .Where(f => f.FolloweeId == targetId)
            .OrderByDescending(f => f.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        return await BuildList(rows, f => f.FollowerId, cancellationToken);
    }

    /// <summary>Members the signed-in member (or ?userId=) follows.</summary>
    [HttpGet("following")]
    public async Task<ActionResult<List<FollowDto>>> GetFollowing([FromQuery] int? userId, CancellationToken cancellationToken)
    {
        var targetId = userId ?? User.GetUserId();
        var rows = await _db.Follows.AsNoTracking()
            .Where(f => f.FollowerId == targetId)
            .OrderByDescending(f => f.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        return await BuildList(rows, f => f.FolloweeId, cancellationToken);
    }

    /// <summary>Follows a traveler.</summary>
    [HttpPost("{userId:int}")]
    public async Task<IActionResult> Follow(int userId, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        if (userId == myId)
            return BadRequest(new { error = "You cannot follow yourself." });
        if (!await _db.Users.AnyAsync(u => u.Id == userId, cancellationToken))
            return NotFound();

        if (!await _db.Follows.AnyAsync(f => f.FollowerId == myId && f.FolloweeId == userId, cancellationToken))
        {
            _db.Follows.Add(new Follow { FollowerId = myId, FolloweeId = userId });
            _db.Notifications.Add(new CommunityNotification
            {
                UserId = userId,
                FromUserId = myId,
                Type = NotificationTypes.Followed,
                Message = "started following you"
            });
            await _db.SaveChangesAsync(cancellationToken);
        }
        return NoContent();
    }

    /// <summary>Unfollows a traveler.</summary>
    [HttpDelete("{userId:int}")]
    public async Task<IActionResult> Unfollow(int userId, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        var rows = await _db.Follows
            .Where(f => f.FollowerId == myId && f.FolloweeId == userId)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
            return NotFound();

        _db.Follows.RemoveRange(rows);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<ActionResult<List<FollowDto>>> BuildList(
        List<Follow> rows, Func<Follow, int> pickUserId, CancellationToken cancellationToken)
    {
        var userIds = rows.Select(pickUserId).Distinct().ToList();
        var users = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        return Ok(rows
            .Where(r => users.ContainsKey(pickUserId(r)))
            .Select(r =>
            {
                var u = users[pickUserId(r)];
                return new FollowDto
                {
                    Id = u.Id,
                    UniqueId = u.UniqueId ?? UserProfileStatus.GenerateUniqueId(u.Id),
                    FullName = u.FullName ?? string.Empty,
                    ProfilePhotoUrl = u.ProfilePhotoData != null
                        ? $"/api/profile/{u.Id}/photo"
                        : u.ProfilePhotoUrl ?? u.ExternalProfilePictureUrl ?? string.Empty,
                    Profession = u.Profession,
                    FollowedAtUtc = r.CreatedAtUtc
                };
            })
            .ToList());
    }
}
