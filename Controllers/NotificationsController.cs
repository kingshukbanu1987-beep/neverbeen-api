using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NeverBeen.API.Common;
using NeverBeen.API.Data;
using NeverBeen.API.Dtos;

namespace NeverBeen.API.Controllers;

/// <summary>Notifications page: list, unread badge count, mark read.</summary>
[Route("api/notifications")]
[ApiController]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _db;

    public NotificationsController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>Notifications of the signed-in member, newest first.</summary>
    [HttpGet]
    public async Task<ActionResult<List<NotificationDto>>> GetNotifications(
        [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var myId = User.GetUserId();
        var rows = await _db.Notifications.AsNoTracking()
            .Where(n => n.UserId == myId)
            .OrderByDescending(n => n.CreatedAtUtc)
            .Take(Math.Clamp(pageSize, 1, 200))
            .ToListAsync(cancellationToken);

        var fromIds = rows.Select(n => n.FromUserId).Distinct().ToList();
        var users = await _db.Users.AsNoTracking()
            .Include(u => u.Country).Include(u => u.City)
            .Where(u => fromIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        return Ok(rows.Select(n => new NotificationDto
        {
            Id = n.Id,
            Type = n.Type,
            Message = n.Message,
            CreatedAtUtc = n.CreatedAtUtc,
            IsRead = n.IsRead,
            RequestId = n.RequestId,
            Status = n.Status,
            FromUser = users.TryGetValue(n.FromUserId, out var u) ? AuthorMapper.From(u) : new AuthorDto { Id = n.FromUserId }
        }).ToList());
    }

    /// <summary>Unread notifications count (header bell badge).</summary>
    [HttpGet("unread-count")]
    public async Task<ActionResult<int>> GetUnreadCount(CancellationToken cancellationToken = default)
    {
        var myId = User.GetUserId();
        return Ok(await _db.Notifications.CountAsync(n => n.UserId == myId && !n.IsRead, cancellationToken));
    }

    /// <summary>Marks one notification read.</summary>
    [HttpPost("{id:long}/read")]
    public async Task<IActionResult> MarkRead(long id, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        var notification = await _db.Notifications
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == myId, cancellationToken);
        if (notification == null)
            return NotFound();

        notification.IsRead = true;
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Marks every notification read (drives the "clear badges" action).</summary>
    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        var rows = await _db.Notifications
            .Where(n => n.UserId == myId && !n.IsRead)
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
            row.IsRead = true;
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
