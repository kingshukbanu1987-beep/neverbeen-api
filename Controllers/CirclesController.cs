using System.Text.Json;
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
/// Travel circles: create, edit, archive, members / admins management and the
/// circle's group chat.
/// </summary>
[Route("api/circles")]
[ApiController]
[Authorize]
public class CirclesController : ControllerBase
{
    private readonly AppDbContext _db;

    public CirclesController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>Circles the signed-in member belongs to (archived ones need ?includeArchived=true).</summary>
    [HttpGet]
    public async Task<ActionResult<List<CircleDto>>> GetCircles(
        [FromQuery] bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        var myId = User.GetUserId();
        var myCircleIds = _db.CircleMembers.Where(m => m.UserId == myId).Select(m => m.CircleId);

        var circles = await _db.Circles.AsNoTracking()
            .Where(c => myCircleIds.Contains(c.Id))
            .Where(c => includeArchived || c.ArchivedAtUtc == null)
            .OrderByDescending(c => c.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return Ok(await BuildCircleDtos(circles, myId, cancellationToken));
    }

    /// <summary>One circle with its members.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<CircleDto>> GetCircle(int id, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        var circle = await _db.Circles.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (circle == null)
            return NotFound();
        if (!await _db.CircleMembers.AnyAsync(m => m.CircleId == id && m.UserId == myId, cancellationToken))
            return Forbid();

        return Ok((await BuildCircleDtos(new List<Circle> { circle }, myId, cancellationToken)).First());
    }

    /// <summary>Largest circle photo accepted as a data URL (the website allows 1 MB before encoding).</summary>
    private const int MaxCirclePhotoChars = 2 * 1024 * 1024;

    /// <summary>Creates a circle (the creator becomes owner + admin + member).</summary>
    [HttpPost]
    public async Task<ActionResult<CircleDto>> Create([FromBody] SaveCircleRequest request, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Circle name is required." });
        var problem = ValidateRequest(request);
        if (problem != null)
            return BadRequest(new { error = problem });

        var circle = new Circle
        {
            Name = request.Name.Trim(),
            Description = request.Description,
            Icon = request.Icon,
            Color = request.Color,
            PhotoUrl = request.PhotoUrl,
            OwnerId = myId
        };
        _db.Circles.Add(circle);
        await _db.SaveChangesAsync(cancellationToken);

        _db.CircleMembers.Add(new CircleMember { CircleId = circle.Id, UserId = myId, IsAdmin = true });
        foreach (var userId in (request.MemberIds ?? new List<int>()).Where(id => id != myId).Distinct())
        {
            if (await _db.Users.AnyAsync(u => u.Id == userId, cancellationToken))
                _db.CircleMembers.Add(new CircleMember { CircleId = circle.Id, UserId = userId });
        }
        await _db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetCircle), new { id = circle.Id },
            (await BuildCircleDtos(new List<Circle> { circle }, myId, cancellationToken)).First());
    }

    /// <summary>Edits a circle (admins only).</summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult<CircleDto>> Update(int id, [FromBody] SaveCircleRequest request, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        var problem = ValidateRequest(request);
        if (problem != null)
            return BadRequest(new { error = problem });
        var circle = await _db.Circles.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (circle == null)
            return NotFound();
        if (!await IsAdmin(id, myId, cancellationToken))
            return Forbid();

        if (!string.IsNullOrWhiteSpace(request.Name)) circle.Name = request.Name.Trim();
        circle.Description = request.Description;
        circle.Icon = request.Icon;
        circle.Color = request.Color;
        circle.PhotoUrl = request.PhotoUrl;
        await _db.SaveChangesAsync(cancellationToken);

        return Ok((await BuildCircleDtos(new List<Circle> { circle }, myId, cancellationToken)).First());
    }

    /// <summary>Deletes a circle (owner only): the circle is archived and recoverable.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        var circle = await _db.Circles.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (circle == null)
            return NotFound();
        if (circle.OwnerId != myId)
            return Forbid();

        circle.ArchivedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Adds a member (admins only).</summary>
    [HttpPost("{id:int}/members/{userId:int}")]
    public async Task<IActionResult> AddMember(int id, int userId, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        if (!await IsAdmin(id, myId, cancellationToken))
            return Forbid();
        if (!await _db.Users.AnyAsync(u => u.Id == userId, cancellationToken))
            return NotFound();

        if (!await _db.CircleMembers.AnyAsync(m => m.CircleId == id && m.UserId == userId, cancellationToken))
        {
            _db.CircleMembers.Add(new CircleMember { CircleId = id, UserId = userId });
            await _db.SaveChangesAsync(cancellationToken);
        }
        return NoContent();
    }

    /// <summary>Removes a member (admins may remove others; anyone may leave).</summary>
    [HttpDelete("{id:int}/members/{userId:int}")]
    public async Task<IActionResult> RemoveMember(int id, int userId, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        if (myId != userId && !await IsAdmin(id, myId, cancellationToken))
            return Forbid();

        var rows = await _db.CircleMembers
            .Where(m => m.CircleId == id && m.UserId == userId)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
            return NotFound();

        _db.CircleMembers.RemoveRange(rows);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Promotes / demotes a member as circle admin (owner only).</summary>
    [HttpPost("{id:int}/admins/{userId:int}")]
    public async Task<IActionResult> SetAdmin(int id, int userId, [FromQuery] bool admin = true, CancellationToken cancellationToken = default)
    {
        var myId = User.GetUserId();
        var circle = await _db.Circles.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (circle == null)
            return NotFound();
        if (circle.OwnerId != myId)
            return Forbid();

        var member = await _db.CircleMembers.FirstOrDefaultAsync(m => m.CircleId == id && m.UserId == userId, cancellationToken);
        if (member == null)
            return NotFound();

        member.IsAdmin = admin;
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Group-chat history of a circle.</summary>
    [HttpGet("{id:int}/messages")]
    public async Task<ActionResult<List<CircleMessageDto>>> GetMessages(
        int id, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var myId = User.GetUserId();
        if (!await _db.CircleMembers.AnyAsync(m => m.CircleId == id && m.UserId == myId, cancellationToken))
            return Forbid();

        var rows = await _db.CircleMessages.AsNoTracking()
            .Where(m => m.CircleId == id)
            .OrderByDescending(m => m.SentAtUtc)
            .Take(Math.Clamp(pageSize, 1, 200))
            .ToListAsync(cancellationToken);

        var userIds = rows.Select(m => m.SenderId).Distinct().ToList();
        var names = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        return Ok(rows
            .OrderBy(m => m.SentAtUtc)
            .Select(m => new CircleMessageDto
            {
                Id = m.Id,
                CircleId = m.CircleId,
                SenderId = m.SenderId,
                SenderName = names.TryGetValue(m.SenderId, out var n) ? n ?? string.Empty : string.Empty,
                Text = m.Text,
                SentAtUtc = m.SentAtUtc,
                ReplyToMessageId = m.ReplyToMessageId,
                Reactions = string.IsNullOrWhiteSpace(m.Reactions)
                    ? null
                    : JsonSerializer.Deserialize<Dictionary<string, int>>(m.Reactions)
            })
            .ToList());
    }

    /// <summary>Posts a message to a circle's group chat.</summary>
    [HttpPost("{id:int}/messages")]
    public async Task<ActionResult<CircleMessageDto>> SendMessage(int id, [FromBody] SendCircleMessageRequest request, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { error = "Message text is required." });
        if (!await _db.CircleMembers.AnyAsync(m => m.CircleId == id && m.UserId == myId, cancellationToken))
            return Forbid();

        var message = new CircleMessage
        {
            CircleId = id,
            SenderId = myId,
            Text = request.Text.Trim(),
            ReplyToMessageId = request.ReplyToMessageId
        };
        _db.CircleMessages.Add(message);
        await _db.SaveChangesAsync(cancellationToken);

        var me = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == myId, cancellationToken);
        return Ok(new CircleMessageDto
        {
            Id = message.Id,
            CircleId = message.CircleId,
            SenderId = message.SenderId,
            SenderName = me.FullName ?? string.Empty,
            Text = message.Text,
            SentAtUtc = message.SentAtUtc,
            ReplyToMessageId = message.ReplyToMessageId
        });
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// Checks the circle fields against the stored column sizes and the accepted photo formats,
    /// so an out-of-range value is answered with a clear message instead of a failed insert.
    /// </summary>
    private static string? ValidateRequest(SaveCircleRequest request)
    {
        if (request.Name != null && request.Name.Trim().Length > 120)
            return "Circle name must be 120 characters or fewer.";
        if (request.Description != null && request.Description.Length > 500)
            return "Circle description must be 500 characters or fewer.";
        if (request.Icon != null && request.Icon.Length > 60)
            return "Circle icon is too long.";
        if (request.Color != null && request.Color.Length > 20)
            return "Circle colour is too long.";

        var photo = request.PhotoUrl?.Trim();
        if (string.IsNullOrEmpty(photo))
            return null;
        if (photo.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            if (!Regex.IsMatch(photo, @"^data:image/(png|jpe?g|gif|webp);base64,", RegexOptions.IgnoreCase))
                return "Circle photo must be a PNG, JPEG, GIF or WebP image.";
            if (photo.Length > MaxCirclePhotoChars)
                return "Circle photo must be 1 MB or smaller.";
        }
        else if (photo.Length > 1024)
        {
            return "Circle photo link must be 1024 characters or fewer.";
        }
        return null;
    }

    private async Task<bool> IsAdmin(int circleId, int userId, CancellationToken cancellationToken)
    {
        var circle = await _db.Circles.AsNoTracking().FirstOrDefaultAsync(c => c.Id == circleId, cancellationToken);
        if (circle == null)
            return false;
        if (circle.OwnerId == userId)
            return true;
        return await _db.CircleMembers.AnyAsync(m => m.CircleId == circleId && m.UserId == userId && m.IsAdmin, cancellationToken);
    }

    private async Task<List<CircleDto>> BuildCircleDtos(List<Circle> circles, int myId, CancellationToken cancellationToken)
    {
        if (circles.Count == 0)
            return new List<CircleDto>();

        var circleIds = circles.Select(c => c.Id).ToList();
        var members = await _db.CircleMembers.AsNoTracking()
            .Where(m => circleIds.Contains(m.CircleId))
            .ToListAsync(cancellationToken);
        var byCircle = members.GroupBy(m => m.CircleId).ToDictionary(g => g.Key, g => g.ToList());

        return circles.Select(c =>
        {
            byCircle.TryGetValue(c.Id, out var rows);
            rows ??= new List<CircleMember>();
            return new CircleDto
            {
                Id = c.Id,
                Name = c.Name,
                Description = c.Description ?? string.Empty,
                Icon = c.Icon ?? string.Empty,
                Color = c.Color ?? string.Empty,
                PhotoUrl = c.PhotoUrl,
                MemberIds = rows.Select(m => m.UserId).ToList(),
                AdminIds = rows.Where(m => m.IsAdmin).Select(m => m.UserId).ToList(),
                OwnerId = c.OwnerId,
                CreatedAtUtc = c.CreatedAtUtc,
                ArchivedAtUtc = c.ArchivedAtUtc
            };
        }).ToList();
    }
}
