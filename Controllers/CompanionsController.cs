using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NeverBeen.API.Common;
using NeverBeen.API.Data;
using NeverBeen.API.Dtos;
using NeverBeen.API.Entities;

namespace NeverBeen.API.Controllers;

/// <summary>
/// Companionships: request, accept, reject, cancel, remove, the companions list
/// and the mutual-companions count.
/// </summary>
[Route("api/companions")]
[ApiController]
[Authorize]
public class CompanionsController : ControllerBase
{
    private readonly AppDbContext _db;

    public CompanionsController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>Companions of the signed-in member (filter with ?status=connected|pending).</summary>
    [HttpGet]
    public async Task<ActionResult<List<CompanionDto>>> GetCompanions(
        [FromQuery] string? status,
        CancellationToken cancellationToken = default)
    {
        var myId = User.GetUserId();
        var rows = await _db.Companionships.AsNoTracking()
            .Where(c => c.UserId == myId || c.CompanionId == myId)
            .ToListAsync(cancellationToken);

        var partnerIds = rows.Select(c => c.UserId == myId ? c.CompanionId : c.UserId).Distinct().ToList();
        var users = await _db.Users.AsNoTracking()
            .Include(u => u.Country).Include(u => u.City)
            .Where(u => partnerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        var result = new List<CompanionDto>();
        foreach (var row in rows)
        {
            var partnerId = row.UserId == myId ? row.CompanionId : row.UserId;
            if (!users.TryGetValue(partnerId, out var user))
                continue;

            var relative = RelativeStatus(row, myId);
            if (!string.IsNullOrWhiteSpace(status) && relative != status)
                continue;

            result.Add(await ToCompanionDto(user, row, myId, cancellationToken));
        }
        return Ok(result);
    }

    /// <summary>One traveler as seen by the signed-in member (status, mutuals, preview).</summary>
    [HttpGet("{userId:int}")]
    public async Task<ActionResult<CompanionDto>> GetCompanion(int userId, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        var user = await _db.Users.AsNoTracking()
            .Include(u => u.Country).Include(u => u.City)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user == null)
            return NotFound();

        var row = await FindPair(myId, userId, cancellationToken);
        return Ok(await ToCompanionDto(user, row, myId, cancellationToken));
    }

    /// <summary>Sends a companionship request (20-digit unique ids also accepted).</summary>
    [HttpPost("{userId:int}/request")]
    public async Task<ActionResult<CompanionshipResultDto>> Request(int userId, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        if (userId == myId)
            return BadRequest(new { error = "You cannot send a companionship request to yourself." });

        if (!await _db.Users.AnyAsync(u => u.Id == userId, cancellationToken))
            return NotFound();

        var existing = await FindPair(myId, userId, cancellationToken);
        if (existing != null)
        {
            if (existing.Status == CompanionshipStatus.Connected)
                return BadRequest(new { error = "You are already companions." });
            if (existing.RequesterId == myId)
                return BadRequest(new { error = "Request already sent." });
            return await AcceptInternal(existing, myId, cancellationToken);
        }

        var pair = NormalizedPair(myId, userId);
        _db.Companionships.Add(new Companionship
        {
            UserId = pair.a,
            CompanionId = pair.b,
            RequesterId = myId,
            Status = CompanionshipStatus.Pending
        });

        _db.Notifications.Add(new CommunityNotification
        {
            UserId = userId,
            FromUserId = myId,
            Type = NotificationTypes.CompanionshipRequest,
            Message = "sent you a companionship request",
            Status = "pending"
        });

        // Following the other traveler happens by default on a request.
        await EnsureFollow(myId, userId, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new CompanionshipResultDto { Status = "pending_outgoing", Message = "Companionship request sent." });
    }

    /// <summary>Accepts an incoming companionship request.</summary>
    [HttpPost("{userId:int}/accept")]
    public async Task<ActionResult<CompanionshipResultDto>> Accept(int userId, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        var row = await FindPair(myId, userId, cancellationToken);
        if (row == null || row.Status != CompanionshipStatus.Pending || row.RequesterId == myId)
            return BadRequest(new { error = "There is no incoming companionship request from this traveler." });

        return await AcceptInternal(row, myId, cancellationToken);
    }

    /// <summary>Rejects an incoming companionship request.</summary>
    [HttpPost("{userId:int}/reject")]
    public async Task<ActionResult<CompanionshipResultDto>> Reject(int userId, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        var row = await FindPair(myId, userId, cancellationToken);
        if (row == null || row.Status != CompanionshipStatus.Pending || row.RequesterId == myId)
            return BadRequest(new { error = "There is no incoming companionship request from this traveler." });

        await AnswerRequestNotifications(myId, row.RequesterId, "rejected", cancellationToken);
        _db.Companionships.Remove(row);
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new CompanionshipResultDto { Status = "none", Message = "Companionship request rejected." });
    }

    /// <summary>Cancels own outgoing companionship request.</summary>
    [HttpDelete("{userId:int}/request")]
    public async Task<ActionResult<CompanionshipResultDto>> CancelRequest(int userId, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        var row = await FindPair(myId, userId, cancellationToken);
        if (row == null || row.Status != CompanionshipStatus.Pending || row.RequesterId != myId)
            return BadRequest(new { error = "There is no outgoing companionship request to this traveler." });

        await AnswerRequestNotifications(userId, myId, "cancelled", cancellationToken);
        _db.Companionships.Remove(row);
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new CompanionshipResultDto { Status = "none", Message = "Companionship request cancelled." });
    }

    /// <summary>Removes a companion (breaks the companionship both ways).</summary>
    [HttpDelete("{userId:int}")]
    public async Task<ActionResult<CompanionshipResultDto>> Remove(int userId, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        var row = await FindPair(myId, userId, cancellationToken);
        if (row == null)
            return NotFound();

        _db.Companionships.Remove(row);
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new CompanionshipResultDto { Status = "none", Message = "Companionship removed." });
    }

    /// <summary>Mutual companions shared by the signed-in member and ?userId=.</summary>
    [HttpGet("mutual/{userId:int}")]
    public async Task<ActionResult<List<CompanionDto>>> GetMutual(int userId, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        var mine = await ConnectedCompanionIds(myId, cancellationToken);
        var theirs = await ConnectedCompanionIds(userId, cancellationToken);
        var mutualIds = mine.Intersect(theirs).ToList();

        var users = await _db.Users.AsNoTracking()
            .Include(u => u.Country).Include(u => u.City)
            .Where(u => mutualIds.Contains(u.Id))
            .ToListAsync(cancellationToken);

        var result = new List<CompanionDto>();
        foreach (var user in users)
        {
            var row = await FindPair(myId, user.Id, cancellationToken);
            result.Add(await ToCompanionDto(user, row, myId, cancellationToken));
        }
        return Ok(result);
    }

    // ------------------------------------------------------------------ helpers

    private async Task<ActionResult<CompanionshipResultDto>> AcceptInternal(Companionship row, int myId, CancellationToken cancellationToken)
    {
        row.Status = CompanionshipStatus.Connected;
        row.ConnectedAtUtc = DateTime.UtcNow;

        // The request notification the member received is answered, so the Notifications
        // page shows "Companionship Approved" instead of offering Approve / Reject again.
        await AnswerRequestNotifications(myId, row.RequesterId, "approved", cancellationToken);

        _db.Notifications.Add(new CommunityNotification
        {
            UserId = row.RequesterId,
            FromUserId = myId,
            Type = NotificationTypes.CompanionshipAccepted,
            Message = "accepted your companionship request",
            Status = "approved"
        });

        await EnsureFollow(myId, row.RequesterId, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new CompanionshipResultDto { Status = "connected", Message = "You are now companions." });
    }

    /// <summary>
    /// Stores the answer ("approved", "rejected" or "cancelled") on the companionship request
    /// notifications that <paramref name="requesterId"/> sent to <paramref name="recipientId"/>.
    /// The caller saves the change together with its own companionship update.
    /// </summary>
    private async Task AnswerRequestNotifications(int recipientId, int requesterId, string status, CancellationToken cancellationToken)
    {
        var pending = await _db.Notifications
            .Where(n => n.UserId == recipientId
                        && n.FromUserId == requesterId
                        && n.Type == NotificationTypes.CompanionshipRequest
                        && n.Status == "pending")
            .ToListAsync(cancellationToken);

        foreach (var notification in pending)
        {
            notification.Status = status;
            notification.IsRead = true;
        }
    }

    private async Task EnsureFollow(int followerId, int followeeId, CancellationToken cancellationToken)
    {
        if (!await _db.Follows.AnyAsync(f => f.FollowerId == followerId && f.FolloweeId == followeeId, cancellationToken))
            _db.Follows.Add(new Follow { FollowerId = followerId, FolloweeId = followeeId });
    }

    private static (int a, int b) NormalizedPair(int x, int y) => x < y ? (x, y) : (y, x);

    private async Task<Companionship?> FindPair(int x, int y, CancellationToken cancellationToken)
    {
        var pair = NormalizedPair(x, y);
        return await _db.Companionships
            .FirstOrDefaultAsync(c => c.UserId == pair.a && c.CompanionId == pair.b, cancellationToken);
    }

    private static string RelativeStatus(Companionship row, int myId)
    {
        if (row.Status == CompanionshipStatus.Connected)
            return "connected";
        return row.RequesterId == myId ? "pending_outgoing" : "pending_incoming";
    }

    private async Task<List<int>> ConnectedCompanionIds(int userId, CancellationToken cancellationToken)
        => await _db.Companionships.AsNoTracking()
            .Where(c => c.Status == CompanionshipStatus.Connected && (c.UserId == userId || c.CompanionId == userId))
            .Select(c => c.UserId == userId ? c.CompanionId : c.UserId)
            .ToListAsync(cancellationToken);

    private async Task<CompanionDto> ToCompanionDto(UserProfile user, Companionship? row, int myId, CancellationToken cancellationToken)
    {
        var mine = await ConnectedCompanionIds(myId, cancellationToken);
        var theirs = await ConnectedCompanionIds(user.Id, cancellationToken);
        var mutuals = mine.Intersect(theirs).Count();
        // Presence as the requesting member sees it: Away after 15 minutes without use.
        var presence = PresenceRules.Effective(user.ActiveStatus, user.LastSeenUtc, DateTime.UtcNow);
        // Only Inactive is offline: Busy, Don't Disturb, Away and Custom members stay in
        // "Online Now" (with the status the website shows beside their name).
        var online = PresenceRules.IsOnline(presence);

        return new CompanionDto
        {
            Id = user.Id,
            UniqueId = user.UniqueId ?? UserProfileStatus.GenerateUniqueId(user.Id),
            FullName = user.FullName ?? string.Empty,
            ProfilePhotoUrl = user.ProfilePhotoData != null
                ? $"/api/profile/{user.Id}/photo"
                : user.ProfilePhotoUrl ?? user.ExternalProfilePictureUrl ?? string.Empty,
            CoverPhotoUrl = user.CoverPhotoUrl,
            Country = user.Country?.Name ?? string.Empty,
            City = user.City?.Name ?? string.Empty,
            Profession = user.Profession ?? string.Empty,
            IsOnline = online,
            MutualCompanionsCount = mutuals,
            Status = row == null ? "none" : RelativeStatus(row, myId),
            Bio = user.AboutMe,
            AboutMe = user.AboutMe,
            AboutMeDetailsJson = user.AboutMeDetailsJson,
            IsProfileLocked = user.IsProfileLocked,
            ActiveStatus = presence,
            CustomStatusText = user.CustomStatusText,
            LastSeenUtc = user.LastSeenUtc,
            IsVerified = user.IsVerified,
            ConnectedCompanionIds = theirs
        };
    }
}
