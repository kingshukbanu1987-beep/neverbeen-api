using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NeverBeen.API.Common;
using NeverBeen.API.Data;
using NeverBeen.API.Dtos;
using NeverBeen.API.Entities;

namespace NeverBeen.API.Controllers;

/// <summary>
/// Safety: abuse reports ("report this post / comment / message"), blocking other
/// travelers (mutual invisibility) and hiding posts from own feed.
/// </summary>
[Route("api/moderation")]
[ApiController]
[Authorize]
public class ModerationController : ControllerBase
{
    private readonly AppDbContext _db;

    public ModerationController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>Files an abuse report.</summary>
    [HttpPost("reports")]
    public async Task<ActionResult<AbuseReportDto>> Report([FromBody] CreateAbuseReportRequest request, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { error = "A reason is required." });

        var report = new AbuseReport
        {
            TargetType = request.TargetType,
            TargetId = request.TargetId,
            ReportedAuthorId = request.ReportedAuthorId,
            ReportedByUserId = myId,
            Reason = request.Reason.Trim(),
            Details = request.Details,
            ReporterEmail = request.ReporterEmail
        };
        _db.AbuseReports.Add(report);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new AbuseReportDto
        {
            Id = report.Id,
            TargetType = report.TargetType,
            TargetId = report.TargetId,
            ReportedByUserId = myId,
            Reason = report.Reason,
            Details = report.Details ?? string.Empty,
            ReporterEmail = report.ReporterEmail,
            CreatedAtUtc = report.CreatedAtUtc,
            Status = report.Status
        });
    }

    /// <summary>Abuse reports (newest first) — feeds the Admin Console moderation queue.</summary>
    [HttpGet("reports")]
    public async Task<ActionResult<List<AbuseReportDto>>> GetReports(CancellationToken cancellationToken = default)
    {
        var rows = await _db.AbuseReports.AsNoTracking()
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(200)
            .ToListAsync(cancellationToken);

        var authorIds = rows.Select(r => r.ReportedAuthorId).Distinct().ToList();
        var users = await _db.Users.AsNoTracking()
            .Include(u => u.Country).Include(u => u.City)
            .Where(u => authorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        return Ok(rows.Select(r => new AbuseReportDto
        {
            Id = r.Id,
            TargetType = r.TargetType,
            TargetId = r.TargetId,
            ReportedAuthor = users.TryGetValue(r.ReportedAuthorId, out var u) ? AuthorMapper.From(u) : new AuthorDto { Id = r.ReportedAuthorId },
            ReportedByUserId = r.ReportedByUserId,
            Reason = r.Reason,
            Details = r.Details ?? string.Empty,
            ReporterEmail = r.ReporterEmail,
            CreatedAtUtc = r.CreatedAtUtc,
            Status = r.Status
        }).ToList());
    }

    /// <summary>Travelers the signed-in member has blocked.</summary>
    [HttpGet("blocks")]
    public async Task<ActionResult<List<BlockedUserDto>>> GetBlocks(CancellationToken cancellationToken = default)
    {
        var myId = User.GetUserId();
        var rows = await _db.BlockedUsers.AsNoTracking()
            .Where(b => b.UserId == myId)
            .OrderByDescending(b => b.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var userIds = rows.Select(b => b.BlockedUserId).Distinct().ToList();
        var users = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        return Ok(rows
            .Where(b => users.ContainsKey(b.BlockedUserId))
            .Select(b =>
            {
                var u = users[b.BlockedUserId];
                return new BlockedUserDto
                {
                    Id = u.Id,
                    UniqueId = u.UniqueId ?? UserProfileStatus.GenerateUniqueId(u.Id),
                    FullName = u.FullName ?? string.Empty,
                    ProfilePhotoUrl = u.ProfilePhotoData != null
                        ? $"/api/profile/{u.Id}/photo"
                        : u.ProfilePhotoUrl ?? u.ExternalProfilePictureUrl ?? string.Empty,
                    BlockedAtUtc = b.CreatedAtUtc
                };
            })
            .ToList());
    }

    /// <summary>Blocks a traveler (mutual invisibility).</summary>
    [HttpPost("blocks/{userId:int}")]
    public async Task<IActionResult> Block(int userId, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        if (userId == myId)
            return BadRequest(new { error = "You cannot block yourself." });
        if (!await _db.Users.AnyAsync(u => u.Id == userId, cancellationToken))
            return NotFound();

        if (!await _db.BlockedUsers.AnyAsync(b => b.UserId == myId && b.BlockedUserId == userId, cancellationToken))
        {
            _db.BlockedUsers.Add(new BlockedUser { UserId = myId, BlockedUserId = userId });

            // Blocking breaks any companionship / follow both ways.
            var pair = myId < userId ? (myId, userId) : (userId, myId);
            var companionship = await _db.Companionships
                .FirstOrDefaultAsync(c => c.UserId == pair.Item1 && c.CompanionId == pair.Item2, cancellationToken);
            if (companionship != null)
                _db.Companionships.Remove(companionship);

            var follows = await _db.Follows
                .Where(f => (f.FollowerId == myId && f.FolloweeId == userId) ||
                            (f.FollowerId == userId && f.FolloweeId == myId))
                .ToListAsync(cancellationToken);
            _db.Follows.RemoveRange(follows);

            await _db.SaveChangesAsync(cancellationToken);
        }
        return NoContent();
    }

    /// <summary>Unblocks a traveler.</summary>
    [HttpDelete("blocks/{userId:int}")]
    public async Task<IActionResult> Unblock(int userId, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        var rows = await _db.BlockedUsers
            .Where(b => b.UserId == myId && b.BlockedUserId == userId)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
            return NotFound();

        _db.BlockedUsers.RemoveRange(rows);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Posts the signed-in member hid from their own feed.</summary>
    [HttpGet("hidden-posts")]
    public async Task<ActionResult<List<long>>> GetHiddenPosts(CancellationToken cancellationToken = default)
    {
        var myId = User.GetUserId();
        return Ok(await _db.HiddenPosts.AsNoTracking()
            .Where(h => h.UserId == myId)
            .Select(h => h.PostId)
            .ToListAsync(cancellationToken));
    }

    /// <summary>Hides a Journey post from the signed-in member's feed.</summary>
    [HttpPost("hidden-posts/{postId:long}")]
    public async Task<IActionResult> HidePost(long postId, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        if (!await _db.HiddenPosts.AnyAsync(h => h.UserId == myId && h.PostId == postId, cancellationToken))
        {
            _db.HiddenPosts.Add(new HiddenPost { UserId = myId, PostId = postId });
            await _db.SaveChangesAsync(cancellationToken);
        }
        return NoContent();
    }

    /// <summary>Unhides a Journey post.</summary>
    [HttpDelete("hidden-posts/{postId:long}")]
    public async Task<IActionResult> UnhidePost(long postId, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        var rows = await _db.HiddenPosts
            .Where(h => h.UserId == myId && h.PostId == postId)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
            return NotFound();

        _db.HiddenPosts.RemoveRange(rows);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
